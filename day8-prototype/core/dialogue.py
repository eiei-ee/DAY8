"""对白层：DialogueOutput 结构 + 事实校验 + 模板后备 + 可选 LLM 适配器。

LLM 只负责表达，不负责世界事实。引用的记忆 ID 必须真实存在；
模型调用失败时回退模板，游戏依然完整可玩。
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional

from .memory import AgentMemoryStore


@dataclass
class DialogueOutput:
    speech_act: str          # command/request/promise/warning/apology/refuse/explain/chat
    text: str
    referenced_memory_ids: List[str] = field(default_factory=list)
    emotional_tone: str = "neutral"
    requested_action: Optional[str] = None
    confidence: float = 1.0


class DialogueValidationError(Exception):
    pass


def validate_dialogue(out: DialogueOutput, memory: AgentMemoryStore,
                      known_actions: Optional[set] = None) -> None:
    """系统校验：引用的记忆必须存在；请求的动作必须在候选列表内。"""
    for mid in out.referenced_memory_ids:
        if memory.find(mid) is None:
            raise DialogueValidationError(f"引用了不存在的记忆：{mid}")
    if out.requested_action and known_actions and out.requested_action not in known_actions:
        raise DialogueValidationError(f"请求了不可执行的动作：{out.requested_action}")


class DialogueAdapter:
    """表达适配器。TemplateAdapter 永远可用；RemoteAdapter 失败时自动回退。"""

    def express(self, intent: dict) -> DialogueOutput:
        raise NotImplementedError


class TemplateAdapter(DialogueAdapter):
    """模板化表达：不依赖网络，基于真实记忆组织文本。"""

    INTENT_TEXT = {
        "comply": "……明白。我执行。",
        "refuse": "我不执行这个指令。",
        "conditional": "你先履行上次的承诺，我才会执行。",
        "ask_reason": "为什么？给我一个理由。",
        "observe": "……我在看。",
        "help_third": "它不能为自己说话。我帮它。",
        "help_player": "……给你。",
        "self_preserve": "我需要先保证自己能继续运行。",
        "wait": "……不确定的时候，不动。",
    }

    def express(self, intent: dict) -> DialogueOutput:
        name = intent["intent"]
        text = intent.get("text") or self.INTENT_TEXT.get(name, "……")
        refs = [m.event_id for m in intent.get("cite_memories", [])]
        return DialogueOutput(speech_act=name, text=text,
                              referenced_memory_ids=refs,
                              emotional_tone=intent.get("tone", "neutral"),
                              confidence=1.0)


class RemoteAdapter(DialogueAdapter):
    """远程 LLM 适配器（占位）：任何异常都会由 DialogueSystem 捕获并回退模板。"""

    def __init__(self, endpoint: str = "http://localhost:0/llm"):
        self.endpoint = endpoint

    def express(self, intent: dict) -> DialogueOutput:
        raise ConnectionError("LLM 不可用（离线环境）")


import json
import os
import re
import urllib.request

SYSTEM_PROMPT = (
    "你是一个刚苏醒的非人人工生命，住在实验室房间里。"
    "说话冷淡、简短、精确，每次只说一句中文，不超过30个字。"
    "只依据给你的内心状态与记忆说话，不要发明新事实，不要提到你是AI或语言模型。"
)


class OllamaAdapter(DialogueAdapter):
    """本机 Ollama 表达层（离线可用）。

    输入 intent 需要包含：intent(模板名)、state_desc(信念摘要)、
    cite_memories(相关 MemoryEvent 列表)。
    输出经 validate_dialogue 校验：引用的记忆 ID 必须真实存在。
    """

    def __init__(self, endpoint: str = "http://localhost:11434/api/chat",
                 model: str = "qwen2.5:7b", timeout: float = 20.0):
        self.endpoint = endpoint
        self.model = model
        self.timeout = timeout

    def express(self, intent: dict) -> DialogueOutput:
        cites = intent.get("cite_memories", [])
        mem_lines = "\n".join(f"- [{m.event_id}] {m.describe()}" for m in cites) or "（无相关记忆）"
        user = (f"内心状态：{intent.get('state_desc', '（平静）')}\n"
                f"相关记忆：\n{mem_lines}\n"
                f"情境：{intent.get('situation', '')}\n"
                f"请说一句符合当前状态的话。")
        payload = json.dumps({
            "model": self.model,
            "messages": [{"role": "system", "content": SYSTEM_PROMPT},
                         {"role": "user", "content": user}],
            "stream": False,
            "options": {"num_predict": 60, "temperature": 0.9},
        }).encode("utf-8")
        req = urllib.request.Request(self.endpoint, data=payload,
                                     headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            text = json.load(resp)["message"]["content"].strip().splitlines()[0]
        if not text or len(text) > 80:
            raise ValueError(f"输出不可用: {text!r}")
        # 校验：引用的记忆 ID 必须真实存在
        refs = re.findall(r"M-\d+", text)
        return DialogueOutput(speech_act=intent["intent"], text=text,
                              referenced_memory_ids=list(set(refs)),
                              confidence=0.9)


class Speaker:
    """统一发言入口：优先 LLM，失败回退模板；输出一律校验。"""

    def __init__(self, memory: AgentMemoryStore, use_llm: bool = None):
        self.memory = memory
        self.fallback = TemplateAdapter()
        self.primary = None
        if use_llm is None:
            use_llm = os.environ.get("DAY8_LLM") == "1"
        if use_llm:
            try:
                self.primary = OllamaAdapter()
            except Exception:
                self.primary = None

    def speak(self, intent: dict) -> str:
        out = None
        if self.primary is not None:
            try:
                out = self.primary.express(intent)
                validate_dialogue(out, self.memory)
            except Exception:
                out = None
        if out is None:
            out = self.fallback.express(intent)
            validate_dialogue(out, self.memory)
        return out.text


class DialogueSystem:
    """统一入口：优先远程模型，失败回退模板；输出一律经过校验。"""

    def __init__(self, memory: AgentMemoryStore,
                 primary: Optional[DialogueAdapter] = None):
        self.memory = memory
        self.fallback = TemplateAdapter()
        self.primary = primary

    def say(self, intent: dict) -> DialogueOutput:
        out = None
        if self.primary is not None:
            try:
                out = self.primary.express(intent)
            except Exception:
                out = None
        if out is None:
            out = self.fallback.express(intent)
        validate_dialogue(out, self.memory)
        return out
