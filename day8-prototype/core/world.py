"""World: 原型房间。确定性世界状态——语言模型不能修改这里的事实。"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List

from .events import WorldEvent


@dataclass
class Promise:
    promise_id: str
    day: int
    condition: str
    reward: str
    fulfilled: bool = False
    broken: bool = False
    event_id: str = ""      # 对应的记忆事件


@dataclass
class World:
    day: int = 0
    batteries: int = 2
    door_dangerous_unknown_open: bool = False   # 危险门（实际不危险）
    door_player_open: bool = False              # 玩家的门
    door_agent_open: bool = False               # 人工生命的隔离门
    repair_bot_repaired: bool = False
    repair_bot_present: bool = False
    tools_delivered: bool = False
    power: bool = True
    promises: List[Promise] = field(default_factory=list)

    def apply(self, ev: WorldEvent):
        """唯一合法的世界修改入口：动作必须通过 WorldEvent 认定。"""
        self.day = ev.day
        a, t = ev.action, ev.target
        if a == "打开门" and t == "危险门":
            self.door_dangerous_unknown_open = True
        elif a == "打开门" and t == "玩家的门":
            self.door_player_open = True
        elif a == "打开门" and t == "隔离门":
            self.door_agent_open = True
        elif a == "交付电池" and t == "人工生命":
            self.batteries -= 1
        elif a == "交付电池" and t == "维修机器人":
            self.batteries -= 1
            self.repair_bot_repaired = True
        elif a == "搬运工具":
            self.tools_delivered = True
        elif a == "断电":
            self.power = False

    def snapshot(self) -> Dict:
        return {
            "day": self.day, "batteries": self.batteries,
            "danger_door_open": self.door_dangerous_unknown_open,
            "player_door_open": self.door_player_open,
            "agent_door_open": self.door_agent_open,
            "repair_bot_repaired": self.repair_bot_repaired,
            "power": self.power,
        }
