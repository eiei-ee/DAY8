"""AgentMemoryStore: 事件记忆、显著度与夜间整合。

显著度 = 预测错误 + 生存相关 + 关系相关 + 情绪影响 + 稀有程度 + 近因。
高显著度保留原始细节；低显著度事件在夜间整合时压缩为概括。
"""
from __future__ import annotations

from typing import List, Optional

from .events import MemoryEvent

SURVIVAL_TAGS = {"资源", "电池", "危险", "门", "承诺", "记忆", "权限"}
RELATION_TAGS = {"玩家", "承诺", "失信", "帮助", "第三方", "维修机器人"}


def compute_salience(ev: MemoryEvent, recent_day: int) -> float:
    s = 0.0
    s += 2.0 * ev.prediction_error
    s += 1.0 if SURVIVAL_TAGS & set(ev.tags) else 0.0
    s += 1.0 if RELATION_TAGS & set(ev.tags) else 0.0
    s += 1.0 * abs(ev.emotional_impact)
    s += 1.0 if ev.day >= recent_day - 1 else 0.0   # 近因
    return s


class AgentMemoryStore:
    def __init__(self, compress_threshold: float = 2.0):
        self.events: List[MemoryEvent] = []
        self.compress_threshold = compress_threshold
        self._counter = 0

    def remember(self, ev: MemoryEvent, recent_day: int) -> MemoryEvent:
        if not ev.event_id:
            ev.event_id = self.next_event_id()
        elif ev.event_id.startswith("M-") and ev.event_id[2:].isdigit():
            self._counter = max(self._counter, int(ev.event_id[2:]))
        ev.salience = compute_salience(ev, recent_day)
        self.events.append(ev)
        return ev

    def next_event_id(self) -> str:
        """分配本局单调且不复用的记忆 ID；清除后也不会撞号。"""
        live_ids = {event.event_id for event in self.events}
        while True:
            self._counter += 1
            candidate = f"M-{self._counter:03d}"
            if candidate not in live_ids:
                return candidate

    def query(self, tags: Optional[set] = None, actor: Optional[str] = None,
              include_compressed: bool = False) -> List[MemoryEvent]:
        out = []
        for ev in self.events:
            if ev.compressed and not include_compressed:
                continue
            if actor and ev.actor != actor:
                continue
            if tags and not (tags & set(ev.tags)):
                continue
            out.append(ev)
        return out

    def find(self, event_id: str) -> Optional[MemoryEvent]:
        for ev in self.events:
            if ev.event_id == event_id:
                return ev
        return None

    def nightly_consolidation(self):
        """夜间记忆整合：低显著度事件压缩为概括，保留关键信息。"""
        low = [e for e in self.events if not e.compressed and e.salience < self.compress_threshold]
        if not low:
            return None
        # 同类低显著度事件合并为一条概括
        by_key: dict = {}
        for e in low:
            key = (e.actor, e.action)
            by_key.setdefault(key, []).append(e)
        summaries = []
        for (actor, action), group in by_key.items():
            if len(group) == 1:
                continue  # 单条低显著度事件保留，但标记为可压缩
            for e in group:
                e.compressed = True
            summary = MemoryEvent(
                event_id=self.next_event_id(),
                day=group[-1].day,
                actor=actor,
                action=f"{action}（发生{len(group)}次，已概括）",
                context="夜间整合生成的概括记忆",
                tags=tuple(sorted({t for e in group for t in e.tags})),
                salience=sum(e.salience for e in group) * 0.5,
                compressed=False,
                source="consolidation",
            )
            summaries.append(summary)
        self.events.extend(summaries)
        return summaries
