"""WorldEvent / MemoryEvent: 世界事实与结构化记忆事件。

世界模拟层产生 WorldEvent（确定性事实）；
感知系统决定人工生命能否感知，感知到的事件记为 MemoryEvent。
"""
from __future__ import annotations

import itertools
from dataclasses import dataclass, field
from typing import Optional

_event_counter = itertools.count(1)


def reset_event_ids():
    global _event_counter
    _event_counter = itertools.count(1)


@dataclass
class WorldEvent:
    """发生在世界中、完全由确定性代码认定的事实。"""
    day: int
    actor: str                      # player / agent / lab / repair_bot
    action: str
    target: Optional[str] = None
    context: str = ""
    spoken: str = ""                # 玩家说了什么
    expected_outcome: str = ""      # 承诺/预期
    actual_outcome: str = ""        # 实际发生了什么
    dangerous: bool = False         # 世界事实：是否真的危险
    tags: tuple = ()


@dataclass
class MemoryEvent:
    """人工生命感知并记住的事件。"""
    event_id: str
    day: int
    actor: str
    action: str
    target: Optional[str] = None
    context: str = ""
    spoken: str = ""
    expected_outcome: str = ""
    actual_outcome: str = ""
    prediction_error: float = 0.0   # 与当前信念的偏离程度 0..1
    emotional_impact: float = 0.0
    source: str = "perception"      # perception / player_statement / lab / inference
    tags: tuple = ()
    salience: float = 0.0
    compressed: bool = False        # 夜间整合后是否被压缩为概括

    def describe(self) -> str:
        base = f"[{self.event_id}] 第{self.day}天 {self.actor} {self.action}"
        if self.target:
            base += f" -> {self.target}"
        if self.spoken:
            base += f"（说：{self.spoken}）"
        if self.actual_outcome:
            base += f"（结果：{self.actual_outcome}）"
        return base
