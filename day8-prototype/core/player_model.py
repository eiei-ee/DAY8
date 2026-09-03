"""PlayerModel: 人工生命持续推测的玩家倾向维度。

这些数值不显示给玩家；只有调试模式和结局报告可见。
"""
from __future__ import annotations

from .beliefs import BeliefSystem

DIMENSIONS = {
    "honesty":         ("玩家的语言与事实一致", "诚实性"),
    "reliability":     ("玩家会兑现承诺", "可靠性"),
    "autonomy_respect":("玩家尊重我的选择", "控制倾向(反向)"),
    "benevolence":     ("玩家在没有回报时也提供帮助", "善意"),
    "self_interest":   ("玩家在资源冲突时优先自己", "自利程度"),
    "lab_dependence":  ("玩家无条件服从实验室", "权威依赖"),
}


class PlayerModel:
    def __init__(self, beliefs: BeliefSystem):
        self.beliefs = beliefs

    def dimension_belief(self, dim: str, context: str = ""):
        prop = DIMENSIONS[dim][0]
        return self.beliefs.get(prop, context)

    def most_uncertain(self, contexts: tuple = ("",)) -> str:
        """导演系统用：返回当前置信度最低（最不确定）的维度。"""
        best, best_c = None, 2.0
        for dim in DIMENSIONS:
            b = self.dimension_belief(dim)
            u = 1.0 - b.certainty
            if u < best_c:
                best, best_c = dim, u
        return best or "reliability"

    def dump(self) -> str:
        lines = []
        for dim, (prop, label) in DIMENSIONS.items():
            b = self.dimension_belief(dim)
            lines.append(f"{label}（{prop}）：{b.label()} {b.confidence:.2f}")
        return "\n".join(lines)
