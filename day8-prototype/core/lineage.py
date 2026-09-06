"""认知谱系：每局结束后压缩一条最重要的认知为“遗传信念”，传给下一局。

新生命不继承记忆，只继承一条模糊先验（弱 Beta 先验，非完整信念）。
"""
from __future__ import annotations

import json
import os
from typing import List, Optional

from .beliefs import BeliefSystem


class LineageStore:
    def __init__(self, path: str = "runs/lineage.json"):
        self.path = path
        self.generations: List[dict] = []
        if os.path.exists(path):
            with open(path, "r", encoding="utf-8") as f:
                self.generations = json.load(f)

    def save(self):
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        with open(self.path, "w", encoding="utf-8") as f:
            json.dump(self.generations, f, ensure_ascii=False, indent=2)

    @property
    def inherited(self) -> Optional[str]:
        """最新一代的遗传信念。"""
        return self.generations[-1]["genetic_belief"] if self.generations else None

    @property
    def inherited_direction(self) -> int:
        """最新一代对命题的方向；旧存档的命题按正向先验处理。"""
        if not self.generations:
            return 1
        value = self.generations[-1].get("genetic_direction", 1)
        return -1 if value in (-1, False, "negative", "false") else 1

    def extract_genetic_belief(self, beliefs: BeliefSystem) -> Optional[str]:
        """选择本局最重要（确定性最高且偏离 0.5 最远）的认知。"""
        cands = [b for b in beliefs.all() if b.certainty > 0.3 and b.context == ""]
        if not cands:
            return None
        best = max(cands, key=lambda b: b.certainty * abs(b.confidence - 0.5))
        return best.proposition

    def record_generation(self, beliefs: BeliefSystem, final_choice: str,
                          seed: int, strategy: str) -> Optional[str]:
        prop = self.extract_genetic_belief(beliefs)
        direction = None
        if prop is not None:
            direction = 1 if beliefs.get(prop).confidence >= 0.5 else -1
        self.generations.append({
            "generation": len(self.generations) + 1,
            "seed": seed,
            "strategy": strategy,
            "final_choice": final_choice,
            "genetic_belief": prop,
            "genetic_direction": direction,
        })
        self.save()
        return prop

    def apply_prior(self, beliefs: BeliefSystem, proposition: Optional[str],
                    direction: int = 1):
        """把遗传信念作为弱先验注入新生命（不是记忆，只是一点偏见）。"""
        if not proposition:
            return
        b = beliefs.get(proposition, "")
        normalized_direction = -1 if direction < 0 else 1
        if normalized_direction > 0:
            b.alpha += 0.4
        else:
            b.beta += 0.4
