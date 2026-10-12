"""Reachability census: which actions can the AI ever actually take?

The brief's "player-path divergence" goal is only meaningful if the branches a
player can reach actually exist. This tool answers two questions with counts
rather than impressions:

1. **Day 8** -- for each of the ten options, in how many runs is it
   *available*, how often does it land in the deterministic top-3 by raw
   utility, and how often is it *chosen*? An option that is available in many
   runs but chosen in none is dead content; an option that is available in
   ~0 runs is unreachable content.

2. **Days 1-7** -- for each agent decision point, how often is each action
   offered, and how often is it chosen? Same two failure modes.

A third question makes the difference between "dead content" and "balanced
rarely": for every action that is offered but never chosen, how far is it from
the winner *after* the planner's seeded noise is applied? A positive gap on
every single run is structural domination (the action can never win, whatever
the personality); a gap that sometimes sits just below zero is a balance
choice. ``ActionPlanner.choose`` is wrapped to capture the real score table.

Read-only: production code is untouched. ``agent_decide``, ``day8_utility`` and
``ActionPlanner.choose`` are wrapped only for the duration of the run.

Usage::

    python -m analysis.tools.reachability_census --seeds 200
    python -m analysis.tools.reachability_census --seeds 200 --json out/census.json
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter, defaultdict
from typing import Dict, List, Tuple

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from core import full_game
from core.full_game import DAY8_OPTIONS, run_full_game
from analysis.strategy_lab import ALL_STRATEGIES, injected_profiles


# ------------------------------------------------------------------ capture

class Census:
    """Collects availability / rank / choice evidence from wrapped internals."""

    def __init__(self) -> None:
        # Day 8: option -> counters
        self.day8_available: Counter = Counter()
        self.day8_chosen: Counter = Counter()
        self.day8_det_top3: Counter = Counter()
        self.day8_utility_sum: Dict[str, float] = defaultdict(float)
        self.day8_utility_max: Dict[str, float] = defaultdict(lambda: float("-inf"))
        self.day8_utility_min: Dict[str, float] = defaultdict(lambda: float("inf"))
        # Days 1-7: (day, decision_id, action) -> counts
        self.mid_offered: Counter = Counter()
        self.mid_chosen: Counter = Counter()
        self.mid_points: Counter = Counter()
        # (day, decision_id, action) -> deficit to the winner, in noisy score
        self.mid_deficit: Dict[Tuple, List[float]] = defaultdict(list)
        # Day-8 noise sensitivity: how often the seeded noise changes the set.
        self.day8_noise_flip = 0
        self.day8_gate_contested = 0
        self.day8_gate_flip = 0
        self.runs = 0

    # -- Day 8 -------------------------------------------------------------
    def record_day8(self, st, utilities: Dict[str, float]) -> None:
        """``utilities`` is the table captured *at decision time*.

        Availability must be read from this table's keys, not re-evaluated
        afterwards: the settlement body opens the doors it chose, so a post-hoc
        ``is_day8_action_available`` call reports "开隔离门 unavailable" exactly
        when the agent *did* open it, which inverts the statistic.
        """
        available = list(utilities)
        self.runs += 1
        for o in available:
            self.day8_available[o] += 1
            u = utilities[o]
            self.day8_utility_sum[o] += u
            self.day8_utility_max[o] = max(self.day8_utility_max[o], u)
            self.day8_utility_min[o] = min(self.day8_utility_min[o], u)
        # Deterministic ranking ignores the settlement noise, so it shows what
        # the *rules* alone would pick. Gate exclusivity is honoured (the three
        # door-gate actions share one slot); battery limits are not, because we
        # want "could this ever be top-3", not "did the battery run out".
        ranked = sorted(available, key=lambda o: utilities[o], reverse=True)
        picked: List[str] = []
        gate = False
        for o in ranked:
            if len(picked) == 3:
                break
            if o in full_game.DAY8_DOOR_GATE_ACTIONS:
                if gate:
                    continue
                gate = True
            picked.append(o)
        for o in picked:
            self.day8_det_top3[o] += 1
        for o in st.day8_actions:
            self.day8_chosen[o] += 1

        # Noise sensitivity. ``picked`` is the rules-only outcome; the settlement
        # adds seeded gauss(0, 0.15) to every score, so comparing the two shows
        # how much of the most important scene is mechanism and how much is
        # luck. The gate slot is reported separately because the three
        # door-gate actions often sit within one noise width of each other.
        if frozenset(picked) != frozenset(st.day8_actions):
            self.day8_noise_flip += 1
        gate_available = [o for o in available if o in full_game.DAY8_DOOR_GATE_ACTIONS]
        if len(gate_available) >= 2:
            self.day8_gate_contested += 1
            det_gate = next((o for o in picked if o in full_game.DAY8_DOOR_GATE_ACTIONS), None)
            act_gate = next((o for o in st.day8_actions
                             if o in full_game.DAY8_DOOR_GATE_ACTIONS), None)
            if det_gate != act_gate:
                self.day8_gate_flip += 1

    # -- Days 1-7 ----------------------------------------------------------
    def record_mid(self, day: int, decision_id: str, available, chosen: str) -> None:
        key = (day, decision_id)
        self.mid_points[key] += 1
        for a in available:
            self.mid_offered[(key, a)] += 1
        self.mid_chosen[(key, chosen)] += 1

    def record_scores(self, day: int, decision_id: str, scored: Dict) -> None:
        """Deficit of every scored action to the winner, on the noisy scores.

        ``scored`` maps name -> (base, base + seeded noise); the planner sorts on
        the second element, so the deficit computed here is exactly the margin
        that had to be overcome.
        """
        if not scored:
            return
        key = (day, decision_id)
        winner = max(v[1] for v in scored.values())
        for name, (_base, noisy) in scored.items():
            self.mid_deficit[(key, name)].append(winner - noisy)


def collect(strategies, seeds) -> Census:
    from core.planner import ActionPlanner

    census = Census()
    original_decide = full_game.agent_decide
    original_utility = full_game.day8_utility
    original_choose = ActionPlanner.choose

    def spy_choose(self, ctx, available=None):
        chosen, scored = original_choose(self, ctx, available)
        census.record_scores(ctx.day, ctx.decision_id, scored)
        return chosen, scored

    def spy_decide(st, planner, decision_id, available, rng, verbose):
        name = original_decide(st, planner, decision_id, available, rng, verbose)
        census.record_mid(st.world.day, decision_id, list(available), name)
        return name

    def spy_settlement(st, planner, rng, verbose):
        # Wrap the settlement so we can snapshot the utility table after it has
        # been computed for every legal option.
        original_utility_fn = full_game.day8_utility
        captured: Dict[str, float] = {}

        def inner(inner_st, opt):
            u = original_utility_fn(inner_st, opt)
            captured[opt] = u
            return u

        full_game.day8_utility = inner
        try:
            result = original_settlement(st, planner, rng, verbose)
        finally:
            full_game.day8_utility = original_utility_fn
        census.record_day8(st, captured)
        return result

    original_settlement = full_game.day8_settlement
    full_game.agent_decide = spy_decide
    full_game.day8_settlement = spy_settlement
    ActionPlanner.choose = spy_choose
    try:
        with injected_profiles():
            for strategy in strategies:
                for seed in seeds:
                    run_full_game(seed, strategy)
    finally:
        full_game.agent_decide = original_decide
        full_game.day8_settlement = original_settlement
        full_game.day8_utility = original_utility
        ActionPlanner.choose = original_choose
    return census


# ------------------------------------------------------------------ reporting

def _pct(n: int, total: int) -> str:
    return f"{100.0 * n / total:5.1f}%" if total else "  n/a"


def format_day8(census: Census) -> str:
    n = census.runs
    lines = [f"\n== 第八天行动可达性 (n={n} 局) ==",
             f"{'行动':<12} {'可用':>7} {'规则Top3':>9} {'实际选中':>9} {'效用均值':>9} {'效用区间':>16}"]
    for o in DAY8_OPTIONS:
        lo = census.day8_utility_min[o]
        hi = census.day8_utility_max[o]
        span = "n/a" if lo == float("inf") else f"[{lo:.2f}, {hi:.2f}]"
        avg = (census.day8_utility_sum[o] / census.day8_available[o]
               if census.day8_available[o] else float("nan"))
        lines.append(f"{o:<12} {_pct(census.day8_available[o], n):>7} "
                     f"{_pct(census.day8_det_top3[o], n):>9} "
                     f"{_pct(census.day8_chosen[o], n):>9} {avg:>9.3f} {span:>16}")
    lines.append(
        f"\n噪声敏感度：种子噪声把「规则 Top3」改成别的组合的局数 "
        f"{census.day8_noise_flip}/{n} ({100.0 * census.day8_noise_flip / n:.1f}%)")
    if census.day8_gate_contested:
        lines.append(
            f"  其中门控名额被两个以上门控行动争夺的局数 {census.day8_gate_contested}，"
            f"其中噪声把门控名额判给另一个行动的 {census.day8_gate_flip} "
            f"({100.0 * census.day8_gate_flip / census.day8_gate_contested:.1f}%)")
    lines.append("  注：规则 Top3 在效用接近并列时对平局顺序敏感，门控三项常落在一个噪声宽度内。")
    return "\n".join(lines)


def format_mid(census: Census) -> str:
    lines = ["\n== 第 1-7 天 agent 决策点可达性 =="]
    keys = sorted(census.mid_points, key=lambda k: (k[0], k[1]))
    dead: List[Tuple] = []
    for key in keys:
        day, decision_id = key
        total = census.mid_points[key]
        offered = sorted(a for (k, a) in census.mid_offered if k == key)
        lines.append(f"\n-- 第{day}天 {decision_id}  (n={total})")
        for a in offered:
            chosen = census.mid_chosen.get((key, a), 0)
            flag = "  <== 从未被选" if chosen == 0 else ""
            lines.append(f"   {a:<14} 提供 {_pct(census.mid_offered[(key, a)], total):>7}"
                         f"   选中 {_pct(chosen, total):>7}  ({chosen}){flag}")
            if chosen == 0:
                dead.append((day, decision_id, a, total))
    if dead:
        lines.append(f"\n!! {len(dead)} 个「提供但从未被选」的 agent 行动：")
        for day, decision_id, a, total in dead:
            gaps = census.mid_deficit.get(((day, decision_id), a), [])
            if gaps:
                gaps_sorted = sorted(gaps)
                lo = gaps_sorted[0]
                hi = gaps_sorted[-1]
                med = gaps_sorted[len(gaps_sorted) // 2]
                near = sum(1 for g in gaps if g <= 0.02)
                lines.append(
                    f"   - 第{day}天 {decision_id}: {a} (0/{total})"
                    f"  与胜者分差 最小 {lo:+.3f} / 中位 {med:+.3f} / 最大 {hi:+.3f}"
                    f"  (擦肩 ≤0.02 的局数 {near})")
            else:
                lines.append(f"   - 第{day}天 {decision_id}: {a} (0/{total})")
    else:
        lines.append("\n所有提供的 agent 行动都至少被选过一次。")
    return "\n".join(lines)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--seeds", type=int, default=200, help="每策略种子数")
    ap.add_argument("--strategies", nargs="*", default=None)
    ap.add_argument("--json", default=None, help="把原始计数写入该路径")
    args = ap.parse_args()

    strategies = args.strategies or list(ALL_STRATEGIES)
    seeds = range(args.seeds)
    census = collect(strategies, seeds)

    print(f"策略 {len(strategies)} 条 × 种子 {args.seeds} = {census.runs} 局")
    print(format_day8(census))
    print(format_mid(census))

    if args.json:
        path = args.json
        os.makedirs(os.path.dirname(path), exist_ok=True) if os.path.dirname(path) else None
        payload = {
            "runs": census.runs,
            "day8": {o: {"available": census.day8_available[o],
                         "det_top3": census.day8_det_top3[o],
                         "chosen": census.day8_chosen[o]}
                     for o in DAY8_OPTIONS},
            "mid": {f"{k[0]}|{k[1]}|{a}": census.mid_chosen.get((k, a), 0)
                    for (k, a) in census.mid_offered},
            "mid_offered": {f"{k[0]}|{k[1]}|{a}": census.mid_offered[(k, a)]
                            for (k, a) in census.mid_offered},
            "mid_points": {f"{k[0]}|{k[1]}": v for k, v in census.mid_points.items()},
        }
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(payload, fh, ensure_ascii=False, indent=2)
        print(f"\n原始计数 -> {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
