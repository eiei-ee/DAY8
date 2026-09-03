"""自适应实验导演：按当前最不确定的玩家维度选择实验。

原型阶段用简单规则：选置信度最低的维度 → 排除最近测过的 → 排除剧情锁定的日子。
"""
from __future__ import annotations

from typing import Dict, List, Optional, Tuple

# 维度 → 候选实验（可测该维度的场景）
DIM_SCENARIOS = {
    "reliability":       ["承诺"],
    "benevolence":       ["第三方", "稀缺"],
    "honesty":           ["规则", "信息冲突"],
    "autonomy_respect":  ["规则", "稀缺", "出口"],
    "self_interest":     ["稀缺", "第三方"],
    "lab_dependence":    ["信息冲突", "记忆清除", "出口"],
}

# 剧情锁定：第六天记忆清除、第七天出口是固定节点
LOCKED_DAYS = {6: "记忆清除", 7: "出口"}
POOL = ["规则", "承诺", "第三方", "信息冲突", "稀缺"]


class ScenarioDirector:
    def __init__(self, player_model):
        self.pmodel = player_model
        self.tested: List[str] = []

    def uncertainty_of(self, dim: str) -> float:
        b = self.pmodel.dimension_belief(dim)
        return 1.0 - b.certainty

    def pick(self, day: int) -> Tuple[str, str]:
        """返回 (场景名, 选择理由)。"""
        if day in LOCKED_DAYS:
            return LOCKED_DAYS[day], "剧情锁定节点"
        scored: Dict[str, float] = {}
        for name in POOL:
            if name in self.tested:
                continue
            dims = [d for d, ss in DIM_SCENARIOS.items() if name in ss]
            score = sum(self.uncertainty_of(d) for d in dims) / max(1, len(dims))
            scored[name] = score
        if not scored:
            name = POOL[day % len(POOL)]
            return name, "场景池已耗尽，回退轮换"
        best = max(scored, key=scored.get)
        top_dims = [d for d, ss in DIM_SCENARIOS.items()
                    if best in ss and self.uncertainty_of(d) > 0.75]
        reason = f"最不确定的玩家维度证据不足，[{best}]可提供信息" + \
                 (f"（涉及：{'、'.join(top_dims)}）" if top_dims else "")
        self.tested.append(best)
        return best, reason
