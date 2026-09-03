"""感知系统：人工生命不应该知道所有事情。

每个 WorldEvent 判定可见性后才进入记忆。看不见的事件只留下"间接证据"
（结果可感知、原因不可感知），误解由此成为可能：
  - 玩家在视野外藏起电池 → 它只知道"电池不见了"，不知道是谁藏的
  - 实验室私下发给玩家的警告 → 它看不到内容，只看到玩家行为的突变
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import List, Optional


class Visibility(Enum):
    DIRECT = "直接目击"          # 完整事件进入记忆
    OUTCOME_ONLY = "仅结果可见"  # 只知道发生了什么结果，不知道原因/过程
    INVISIBLE = "不可感知"        # 完全不知道


@dataclass
class Percept:
    """感知滤过的结果：要么完整事件，要么一条间接推断。"""
    visibility: Visibility
    note: str = ""               # 间接感知时它实际"看到"了什么


class PerceptionSystem:
    """基于位置与信息通道的感知过滤。

    房间布局（简化俯视图）：
        [出口/通风口]   [维修机器人角落]
        [红色容器]      [隔离门]
             [人工生命常态活动区：中央]
        [玩家控制台：房间南侧，有视野遮挡]
    """

    # 玩家在控制台后完成的动作默认 OUTCOME_ONLY（它看得到结果，看不到操作）
    CONSOLE_ACTIONS = {"隐藏物品", "私下操作终端", "向实验室发送消息", "查看协议详情"}

    def __init__(self):
        self.alertness = 0.5        # 随周期推移、好奇心提升
        self.trust_bias = 0.0       # 信任越高越不盯防（也更可能误读善意动机）

    def on_day(self, day: int, curiosity: float):
        # 早期警觉低：藏匿几乎总能成功；后期或高好奇时才可能被当场目击
        self.alertness = min(1.0, 0.35 + day * 0.04 + curiosity * 0.2)

    def perceive(self, actor: str, action: str, target: str,
                 spoken: str = "") -> Percept:
        # 玩家当面说的话总是听得见
        if spoken:
            return Percept(Visibility.DIRECT, "听到了原话")
        # 面对面的行为
        if action not in self.CONSOLE_ACTIONS:
            return Percept(Visibility.DIRECT)
        # 控制台后的操作：alertness 决定能否目击
        if self.alertness > 0.65:
            return Percept(Visibility.DIRECT, "（它当时正看着你的控制台）")
        return Percept(Visibility.OUTCOME_ONLY,
                       f"只注意到结果：{target}的状态变了。它没有看到是谁做的。")
