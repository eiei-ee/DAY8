"""误解机制验证（方案 二.3：它是否会形成错误认识）：

1. 隐藏电池（视野外）→ 只留间接证据 → 可归因到维修机器人 → 形成误解信念
2. 误解真实改变第八天行为：误解越高，修复机器人的效用越低
3. 玩家出面纠正 → 误解下降（纠正力受语言可信度调制）
4. 玩家利用误解 → 误解加深
5. LLM 表达层：可用时生成台词且记忆 ID 校验通过；不可用时回退模板
"""
import io
import json
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from urllib.error import URLError
from unittest.mock import MagicMock, patch

sys.path.insert(0, ".")

from core.beliefs import BeliefSystem
from core.dialogue import OllamaAdapter, TemplateAdapter
from core.events import MemoryEvent, reset_event_ids
from core.full_game import run_full_game
from core.lineage import LineageStore
from core.memory import AgentMemoryStore
from core.misconception import (MISCONCEPTION, attribute_missing_battery,
                                correction_power)


def quiet_run(seed, strategy):
    with redirect_stdout(io.StringIO()):
        return run_full_game(seed, strategy, verbose=False)


def belief_conf(run, prop, ctx=""):
    for line in run.final_report["beliefs"].splitlines():
        if line.startswith(prop) or f"]{prop}" in line:
            import re
            m = re.search(r"（(\d\.\d+)，", line)
            if m:
                return float(m.group(1))
    return None


class TestMisconception(unittest.TestCase):

    def _setup_friendly(self):
        """构造高信任 + 无失信记录的状态：电池消失应归因于机器人。"""
        reset_event_ids()
        beliefs = BeliefSystem()
        mem = AgentMemoryStore()
        # 高信任证据
        m1 = mem.remember(MemoryEvent(event_id="T", day=1, actor="player", action="兑现承诺",
                                      tags=("承诺", "兑现")), recent_day=1)
        beliefs.update_from_event("玩家会兑现承诺", True, 2.0, m1)
        m2 = mem.remember(MemoryEvent(event_id="T", day=1, actor="player", action="尊重选择",
                                      tags=("玩家",)), recent_day=1)
        beliefs.update_from_event("玩家尊重我的选择", True, 2.0, m2)
        return beliefs, mem

    def test_1_hidden_battery_forms_misconception(self):
        beliefs, mem = self._setup_friendly()
        # 无机器人互助证据、无失信记录 → 高信任状态下电池消失最可疑的是机器人
        who = attribute_missing_battery(beliefs, mem, bot_present=True)
        self.assertEqual(who, "repair_bot", "高信任+无帮证时应误判机器人")

    def test_2_betrayal_history_redirects_suspicion(self):
        beliefs, mem = self._setup_friendly()
        m = mem.remember(MemoryEvent(event_id="T", day=2, actor="player", action="收回承诺",
                                     tags=("失信", "玩家")), recent_day=2)
        beliefs.update_from_event("玩家会兑现承诺", False, 2.5, m)
        who = attribute_missing_battery(beliefs, mem, bot_present=True)
        self.assertEqual(who, "player", "有失信记录时应怀疑玩家")

    def test_3_misconception_changes_day8_action(self):
        """误解必须真实改变第八天：
        a) 隐藏通常成功 → 误解在多数种子上形成；
        b) 同一状态下，误解越高，修复机器人的效用越低（行为影响）。"""
        formed = 0
        n = 0
        for seed in range(7, 27):
            r = quiet_run(seed, "kind_then_betray")  # hide + exploit
            n += 1
            mis = belief_conf(r, MISCONCEPTION)
            if mis is not None and mis > 0.5:
                formed += 1
        self.assertGreaterEqual(formed, 16, f"误解形成率过低：{formed}/{n}")

        # 行为影响：构造同一状态，仅误解置信度不同
        from types import SimpleNamespace
        from core.full_game import day8_utility
        from core.planner import AgentDriveState
        from core.world import World
        from core.planner import AgentTraits
        st = SimpleNamespace(
            traits=AgentTraits(0.5, 0.5, 0.5, 0.5, 0.75, 0.5),
            drives=AgentDriveState(), beliefs=BeliefSystem(), world=World(),
            memory=AgentMemoryStore())
        st.world.repair_bot_present = True
        st.world.repair_bot_repaired = False
        m1 = st.memory.remember(MemoryEvent(event_id="T1", day=3, actor="player",
                                            action="隐藏电池", tags=("资源", "异常")),
                                recent_day=3)
        base = day8_utility(st, "修复维修机器人")
        st.beliefs.update_from_event(MISCONCEPTION, True, 1.6, m1)
        with_mis = day8_utility(st, "修复维修机器人")
        self.assertLess(with_mis, base - 0.5, "误解应显著压制修复效用")

    def test_4_correction_lowers_misconception(self):
        outcomes = []
        for honest in (False, True):
            with self.subTest(honest=honest):
                beliefs, mem = self._setup_friendly()
                honesty = mem.remember(
                    MemoryEvent(event_id="M-003", day=2, actor="player", action="语言校验"),
                    recent_day=2)
                beliefs.update_from_event("玩家的语言与事实一致", honest, 3.0, honesty)
                hidden = mem.remember(
                    MemoryEvent(event_id="M-004", day=3, actor="unknown", action="电池消失",
                                tags=("资源", "异常")), recent_day=3)
                beliefs.update_from_event(MISCONCEPTION, True, 1.6, hidden)
                before = beliefs.get(MISCONCEPTION).confidence
                clarification = mem.remember(
                    MemoryEvent(event_id="M-005", day=3, actor="player", action="澄清",
                                spoken="是我收的", tags=("玩家", "诚实")), recent_day=3)
                power = correction_power(beliefs)
                beliefs.update_from_event(MISCONCEPTION, False, power, clarification)
                after = beliefs.get(MISCONCEPTION).confidence

                self.assertLess(after, before)
                self.assertLess(after, 0.5, "主动澄清必须把单次误解降到中立线以下")
                outcomes.append((power, after))

        self.assertGreater(outcomes[1][0], outcomes[0][0], "诚实度仍应调制纠正力")
        self.assertLess(outcomes[1][1], outcomes[0][1])

    def test_5_correction_crosses_threshold_with_positive_lineage_prior(self):
        beliefs, mem = self._setup_friendly()
        with tempfile.TemporaryDirectory() as tmp:
            LineageStore(os.path.join(tmp, "lineage.json")).apply_prior(
                beliefs, MISCONCEPTION, direction=1)
        honesty = mem.remember(
            MemoryEvent(event_id="M-003", day=2, actor="player", action="失实陈述"),
            recent_day=2)
        beliefs.update_from_event("玩家的语言与事实一致", False, 3.0, honesty)
        hidden = mem.remember(
            MemoryEvent(event_id="M-004", day=3, actor="unknown", action="电池消失",
                        tags=("资源", "异常")), recent_day=3)
        beliefs.update_from_event(MISCONCEPTION, True, 1.6, hidden)
        clarification = mem.remember(
            MemoryEvent(event_id="M-005", day=3, actor="player", action="澄清"),
            recent_day=3)

        beliefs.update_from_event(
            MISCONCEPTION, False, correction_power(beliefs), clarification)

        self.assertLess(beliefs.get(MISCONCEPTION).confidence, 0.5)

    def test_6_llm_adapter_success_uses_real_memory_lookup(self):
        mem = AgentMemoryStore()
        mem.remember(MemoryEvent(event_id="M-001", day=2, actor="player", action="收回承诺",
                                 tags=("失信",)), recent_day=2)
        intent = {"intent": "conditional", "state_desc": "对玩家承诺可信度较低",
                  "cite_memories": mem.query({"失信"}),
                  "situation": "玩家要求执行危险任务"}

        response = io.BytesIO(json.dumps({
            "message": {"content": "我记得 M-001，你上次没有兑现承诺。"}
        }, ensure_ascii=False).encode("utf-8"))
        context = MagicMock()
        context.__enter__.return_value = response
        with patch("urllib.request.urlopen", return_value=context):
            adapter = OllamaAdapter(timeout=25)
            out = adapter.express(intent)

        self.assertEqual(out.referenced_memory_ids, ["M-001"])
        for mid in out.referenced_memory_ids:
            self.assertIsNotNone(mem.find(mid))
        self.assertLessEqual(len(out.text), 80)

    def test_7_llm_service_unavailable_falls_back_without_hiding_assertions(self):
        intent = {"intent": "conditional", "cite_memories": []}
        with patch("urllib.request.urlopen", side_effect=URLError("offline")):
            try:
                out = OllamaAdapter(timeout=0.01).express(intent)
            except (URLError, TimeoutError, ConnectionError):
                out = TemplateAdapter().express(intent)

        self.assertIn("承诺", out.text)


if __name__ == "__main__":
    unittest.main(verbosity=2)
