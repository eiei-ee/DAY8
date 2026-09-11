"""Belief-channel audit: is every belief either written and read, or neither?

The causal chain the game is built on is

    behaviour -> memory -> belief -> Day-8 action

A proposition that is **written but never read** is a fact the agent records and
then ignores. A proposition that is **read but never written** is worse: every
action that depends on it is frozen at the prior, so the action's whole belief
channel is dead while it still looks causal in the report. Both are invisible
from reading the code one function at a time, because the write and the read
live in different files.

This tool measures it at runtime on the full game:

* writes -- ``BeliefSystem.update_from_event`` is wrapped;
* reads  -- ``BeliefSystem.get`` is wrapped, so *every* reader is counted no
            matter which module it lives in, and each read is attributed to the
            calling function's name.

Attributing by caller matters: an earlier version of this tool extracted the
Day-8 readers with a regex over ``day8_utility``'s source, which silently missed
``B(MISCONCEPTION)`` because the proposition is passed as a module constant
rather than a literal. Wrapping ``get`` cannot miss that.

Usage::

    python -m analysis.tools.belief_channels --seeds 100
    python -m analysis.tools.belief_channels --seeds 100 --json out/channels.json
"""
from __future__ import annotations

import argparse
import inspect
import json
import os
import re
import sys
from collections import Counter, defaultdict
from typing import Dict, List, Tuple

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from core import full_game
from core.beliefs import BeliefSystem
from core.full_game import run_full_game
from analysis.strategy_lab import ALL_STRATEGIES, injected_profiles


def collect(strategies, seeds) -> Dict:
    writes: Counter = Counter()
    readers: Counter = Counter()            # (prop, caller) -> calls

    orig_update = BeliefSystem.update_from_event
    orig_get = BeliefSystem.get

    def spy_update(self, prop, supporting, weight, memory_event, context=""):
        writes[prop] += 1
        return orig_update(self, prop, supporting, weight, memory_event, context)

    def spy_get(self, prop, context=""):
        readers[(prop, sys._getframe(1).f_code.co_name)] += 1
        return orig_get(self, prop, context)

    BeliefSystem.update_from_event = spy_update
    BeliefSystem.get = spy_get
    try:
        with injected_profiles():
            for strategy in strategies:
                for seed in seeds:
                    run_full_game(seed, strategy)
    finally:
        BeliefSystem.update_from_event = orig_update
        BeliefSystem.get = orig_get

    return {"writes": writes, "readers": readers,
            "runs": len(strategies) * len(seeds)}


def analyse(data: Dict) -> Tuple[List[Dict], List[Dict], List[Dict]]:
    writes = data["writes"]
    readers = data["readers"]
    reads: Counter = Counter()
    by_prop: Dict[str, Counter] = defaultdict(Counter)
    for (prop, caller), n in readers.items():
        reads[prop] += n
        by_prop[prop][caller] += n

    props = sorted(set(writes) | set(reads))
    rows = []
    for prop in props:
        callers = by_prop.get(prop, Counter())
        rows.append({
            "prop": prop,
            "writes": writes.get(prop, 0),
            "reads": reads.get(prop, 0),
            "callers": dict(callers.most_common()),
            "day8": callers.get("day8_utility", 0) > 0,
        })
    write_only = [r for r in rows if r["writes"] and not r["reads"]]
    never_written = [r for r in rows if r["reads"] and not r["writes"]]
    return rows, write_only, never_written


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--seeds", type=int, default=100)
    ap.add_argument("--json", default=None)
    args = ap.parse_args()

    data = collect(list(ALL_STRATEGIES), range(args.seeds))
    rows, write_only, never_written = analyse(data)

    print(f"策略 {len(ALL_STRATEGIES)} 条 × 种子 {args.seeds} = {data['runs']} 局")
    print(f"\n{'命题':<24} {'写入':>7} {'读取':>7} {'第8天':>6}  读取者")
    for r in rows:
        callers = ",".join(f"{k}×{v}" for k, v in list(r["callers"].items())[:4]) or "-"
        print(f"{r['prop']:<24} {r['writes']:>7} {r['reads']:>7} "
              f"{'是' if r['day8'] else '否':>6}  {callers}")

    print(f"\n!! 只写不读（记录后被忽略）{len(write_only)} 条：")
    for r in write_only:
        print(f"   - {r['prop']}  写入 {r['writes']} 次，无任何读者")
    if not write_only:
        print("   （无）")

    print(f"\n!! 只读不写（读者永远停在先验）{len(never_written)} 条：")
    for r in never_written:
        callers = ",".join(r["callers"])
        print(f"   - {r['prop']}  被读 {r['reads']} 次（{callers}），"
              f"完整版从未写入 → 该通道恒为 0")
    if not never_written:
        print("   （无）")

    if args.json:
        os.makedirs(os.path.dirname(args.json), exist_ok=True) if os.path.dirname(args.json) else None
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump({"runs": data["runs"], "rows": rows,
                       "write_only": [r["prop"] for r in write_only],
                       "never_written": [r["prop"] for r in never_written]},
                      fh, ensure_ascii=False, indent=2)
        print(f"\n原始计数 -> {args.json}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
