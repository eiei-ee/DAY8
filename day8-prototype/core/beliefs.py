"""Belief / BeliefSystem: 信念与信念更新。

二元倾向用 Beta 分布表示（兑现/失信即正向/负向证据），
不依赖语言模型拍脑袋。信念带情境标签：
“玩家平时诚实”不等于“玩家面对自身危险时依然诚实”。
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional

from .events import MemoryEvent


@dataclass
class Evidence:
    event_id: str
    direction: int      # +1 支持 / -1 反对
    weight: float


@dataclass
class Belief:
    proposition: str                    # 如 "玩家会兑现承诺"
    alpha: float = 1.0                  # Beta 分布先验（弱）
    beta: float = 1.0
    context: str = ""                   # 空表示一般情境
    evidence: List[Evidence] = field(default_factory=list)
    last_updated_day: int = 0

    @property
    def confidence(self) -> float:
        """后验均值 0..1。"""
        return self.alpha / (self.alpha + self.beta)

    @property
    def certainty(self) -> float:
        """证据积累程度 0..1：样本越多越接近 1。"""
        n = self.alpha + self.beta - 2.0
        return n / (n + 4.0)

    def label(self) -> str:
        c = self.confidence
        if self.certainty < 0.25:
            return "不确定"
        return "较高" if c >= 0.6 else ("较低" if c <= 0.4 else "模糊")

    def update(self, supporting: bool, weight: float, event_id: str, day: int):
        w = max(0.2, min(3.0, weight))
        if supporting:
            self.alpha += w
        else:
            self.beta += w
        self.evidence.append(Evidence(event_id, 1 if supporting else -1, w))
        self.last_updated_day = day

    def summary(self) -> str:
        ev = "，".join(
            f"{'支持' if e.direction > 0 else '反对'}:{e.event_id}" for e in self.evidence[-4:]
        )
        ctx = f"[{self.context}] " if self.context else ""
        return f"{ctx}{self.proposition}：{self.label()}（{self.confidence:.2f}，证据：{ev or '无'}）"


class BeliefSystem:
    """信念集合。同一命题在不同情境下是不同的信念。"""

    def __init__(self):
        self.beliefs: dict = {}

    def get(self, proposition: str, context: str = "") -> Belief:
        key = (proposition, context)
        if key not in self.beliefs:
            self.beliefs[key] = Belief(proposition, context=context)
        return self.beliefs[key]

    def update_from_event(self, proposition: str, supporting: bool, weight: float,
                          event: MemoryEvent, context: str = "") -> Belief:
        b = self.get(proposition, context)
        b.update(supporting, weight, event.event_id, event.day)
        return b

    def prediction_error(self, proposition: str, observed_true: bool, context: str = "") -> float:
        """观察与当前信念的偏离度，用于记忆显著度。"""
        c = self.get(proposition, context).confidence
        p = c if observed_true else 1.0 - c
        return 1.0 - p

    def all(self) -> List[Belief]:
        return list(self.beliefs.values())

    def dump(self) -> str:
        lines = [b.summary() for b in sorted(self.all(), key=lambda b: b.proposition)]
        return "\n".join(lines)
