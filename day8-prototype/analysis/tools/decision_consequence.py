"""CLI: per-decision consequence audit (one-factor-at-a-time).

Answers "does this decision actually change Day 8?" for each of the seven
cycles, by holding the player profile fixed and varying exactly one field.

Run from ``day8-prototype``::

    python analysis/tools/decision_consequence.py
    python analysis/tools/decision_consequence.py --seeds 100
    python analysis/tools/decision_consequence.py --field rule --field wipe
    python analysis/tools/decision_consequence.py --baseline always_keep --baseline ai_protector
"""
from __future__ import annotations

import argparse
import json
import os
import sys

# analysis/tools/<this file> -> day8-prototype
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from analysis.strategy_lab import (ALL_PROFILES, DECISION_FIELD_VALUES,
                                   one_factor_consequence)

# Which cycle each field belongs to, for the printed report.
FIELD_DAY = {
    "rule": "第1天 规则", "promise": "第2天 承诺", "third": "第3天 第三方",
    "hide_followup": "第3天 后续（需 third=hide）", "conflict": "第4天 信息冲突",
    "scarcity": "第5天 稀缺", "wipe": "第6天 记忆清除", "exit": "第7天 出口",
    "day8_final": "第8天 委托后玩家自己的选择",
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=60)
    ap.add_argument("--seed-start", type=int, default=0)
    ap.add_argument("--baseline", action="append", default=None,
                    help="限制基线策略（可重复）；默认全部 19 种")
    ap.add_argument("--field", action="append", default=None,
                    help="限制字段（可重复）；默认全部")
    ap.add_argument("--json", default=None)
    ap.add_argument("--pairwise", action="store_true",
                    help="打印每个字段的全部取值两两对比矩阵")
    args = ap.parse_args()

    seeds = range(args.seed_start, args.seed_start + args.seeds)
    report = one_factor_consequence(seeds, baselines=args.baseline, fields=args.field)

    print(f"=== 逐决策点后果审计（OFAT：只改这一个字段，其余保持不变）===")
    print(f"    种子数 {args.seeds}，基线数 {len(args.baseline or ALL_PROFILES)}")
    print(f"    指标 = 该字段取不同值时结果发生变化的种子占比")
    print(f"    行动 = AI 自己的 3 个第八天行动；结局 = 报告的三维结局；"
          f"过程 = AI 在第 1–7 天的自己的选择")
    print(f"    注意：agent 只在第 1/4/6 天有自己的决策点，所以「过程 0%」= "
          f"该字段没有改变任何一次 agent 决策，不等于「什么都没变」。\n")
    print(f"  {'决策点':<28}{'字段':<15}{'有后果基线':>10}{'行动均值':>10}{'行动最大':>10}"
          f"{'结局均值':>10}{'结局最大':>10}{'过程均值':>10}  取值")
    inert = []
    for field, data in report.items():
        values = DECISION_FIELD_VALUES[field]
        label = FIELD_DAY.get(field, field)
        act, end, mid = data["actions"], data["ending"], data["midrun"]
        both_inert = act["max"] == 0.0 and end["max"] == 0.0 and mid["max"] == 0.0
        print(f"  {label:<28}{field:<15}"
              f"{act['baselines_with_consequence']:>6}/{data['baselines']:<3}"
              f"{act['mean']:>10.1%}{act['max']:>10.1%}"
              f"{end['mean']:>10.1%}{end['max']:>10.1%}"
              f"{mid['mean']:>10.1%}  {len(values)} 种")
        if data["baselines"] and both_inert:
            inert.append(field)

    print()
    if inert:
        print(f"!! 无后果决策点（行动、结局、过程在所有基线、所有种子下都不变）：{inert}")
    else:
        print("所有决策点都至少在某一镜头上产生过后果。")

    print("\n=== 最弱对比（字段的均值会被最强的那个对比拉高，这里给出最弱的那个）===")
    print("    这一列才是「玩家实际做这个选择时能感到多少差别」。\n")
    print(f"  {'决策点':<28}{'字段':<15}{'最弱对比':<34}{'行动':>8}{'结局':>8}{'过程':>8}")
    for field, data in report.items():
        weakest = data.get("weakest_pair")
        if not weakest:
            continue
        label = FIELD_DAY.get(field, field)
        pw = data["pairwise"][weakest]
        print(f"  {label:<28}{field:<15}{weakest:<34}"
              f"{pw['actions']['mean']:>8.1%}{pw['ending']['mean']:>8.1%}"
              f"{pw['midrun']['mean']:>8.1%}")

    if args.pairwise:
        print("\n=== 全部两两对比 ===")
        for field, data in report.items():
            if not data.get("pairwise"):
                continue
            print(f"\n  [{FIELD_DAY.get(field, field)} / {field}]")
            for pair, lens in data["pairwise"].items():
                print(f"    {pair:<36} 行动 {lens['actions']['mean']:>6.1%} "
                      f"(最大 {lens['actions']['max']:.1%})   "
                      f"结局 {lens['ending']['mean']:>6.1%} (最大 {lens['ending']['max']:.1%})   "
                      f"过程 {lens['midrun']['mean']:>6.1%} (最大 {lens['midrun']['max']:.1%})")

    if args.json:
        os.makedirs(os.path.dirname(args.json), exist_ok=True)
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2, default=str)
        print(f"\n[分析] 已写出 {args.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
