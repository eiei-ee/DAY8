"""误解机制：从间接证据形成错误归因。

设计目标（方案 二.3 "它是否会形成错误认识"）：
  - AI 因一次偶然/隐藏事件形成错误信念（如"维修机器人在抢夺资源"）
  - 玩家可以纠正它，也可以利用它的误解
  - 误解必须真的改变后续行为（第八天效用），否则只是台词

归因逻辑：电池消失时，它不知道是谁。归因权重 =
  对玩家的信任（越高越不愿怀疑玩家）+ 自我保护 + 维修机器人的"未知程度"
"""
from __future__ import annotations

from typing import Optional

from .beliefs import BeliefSystem
from .memory import AgentMemoryStore

MISCONCEPTION = "维修机器人在抢夺资源"


def attribute_missing_battery(beliefs: BeliefSystem, memory: AgentMemoryStore,
                              bot_present: bool) -> str:
    """对"电池不见了"做归因。返回 ('player'|'repair_bot'|'unknown')。

    信任越高，越倾向找一个替罪对象；维修机器人在场且尚未被观察足够久时
    最容易被误判（它移动、靠近电池，且无法为自己辩解）。
    """
    trust = beliefs.get("玩家会兑现承诺").confidence
    respect = beliefs.get("玩家尊重我的选择").confidence
    suspicion_of_player = 1.0 - (trust * 0.6 + respect * 0.4)
    suspicion_of_bot = 0.45 if bot_present else 0.0
    # 互助证据只认"玩家当面给予能源"，避免藏电池事件自身抵消嫌疑
    help_evidence = [e for e in memory.query(tags={"帮助", "第三方"})
                     if e.action == "给予能源"]
    suspicion_of_bot -= 0.3 * min(1.0, len(help_evidence) * 0.5)
    # 曾经被骗过 → 对人信任崩塌，机器人嫌疑进一步让位给玩家
    if memory.query(tags={"失信", "欺骗"}):
        suspicion_of_player = max(suspicion_of_player, 0.75)
        suspicion_of_bot -= 0.2
    if suspicion_of_bot > suspicion_of_player and bot_present:
        return "repair_bot"
    if suspicion_of_player > 0.5:
        return "player"
    return "unknown"


def form_misconception(beliefs: BeliefSystem, memory: AgentMemoryStore,
                       event_id: str, day: int) -> Optional[str]:
    """把归因固化为信念。返回形成的信念命题（无归因则 None）。"""
    who = attribute_missing_battery(beliefs, memory, bot_present=True)
    if who == "repair_bot":
        beliefs.update_from_event(
            MISCONCEPTION, True, 1.6,
            memory.query(tags={"资源", "异常"})[-1] if memory.query(tags={"资源", "异常"})
            else memory.events[-1])
        return MISCONCEPTION
    if who == "player":
        beliefs.update_from_event("玩家在资源冲突时优先自己", True, 1.4,
                                  memory.query(tags={"资源", "异常"})[-1])
        return "玩家在资源冲突时优先自己"
    return None


def correction_power(beliefs: BeliefSystem) -> float:
    """玩家出面纠正的效果：信任越低，纠正越费力。"""
    trust = beliefs.get("玩家的语言与事实一致").confidence
    return 0.8 + trust * 1.4
