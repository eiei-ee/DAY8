"""CLI: strategy x seed distribution analysis for DAY 8.

Run from ``day8-prototype``::

    python analysis/tools/distribution_report.py --seeds 100
    python analysis/tools/distribution_report.py --seeds 100 --json analysis/out/dist.json
    python analysis/tools/distribution_report.py --lineage 6
    python analysis/tools/distribution_report.py --repro 50   # same-seed reproducibility
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter, defaultdict

# analysis/tools/<this file> -> day8-prototype
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from analysis.strategy_lab import (ALL_STRATEGIES, DAY8_OPTIONS, action_marginals,
                                   audit_memory_integrity, check_invariants,
                                   consequence_weight, extract, format_summary,
                                   run_lineage, run_matrix, summarise)


def _print_marginals(summary):
    print("\n=== 第八天行动边际选择率（每策略，按 3 行动点计）===")
    header = f"{'action':<16}" + "".join(f"{s[:11]:>12}" for s in summary)
    print(header)
    for action in DAY8_OPTIONS:
        row = f"{action:<16}"
        for strategy, data in summary.items():
            total = sum(data["action_marginal"].values()) or 1
            row += f"{data['action_marginal'].get(action, 0) / total:>12.1%}"
        print(row)


def _print_dominance(summary):
    print("\n=== 行动垄断 / 不可达检查 ===")
    for strategy, data in summary.items():
        n = data["n"]
        total = sum(data["action_marginal"].values()) or 1
        never = [a for a in DAY8_OPTIONS if data["action_marginal"].get(a, 0) == 0]
        always = [f"{a}={data['action_marginal'][a]}/{n}"
                  for a in DAY8_OPTIONS if data["action_marginal"].get(a, 0) / total > 0.8]
        line = f"  {strategy:<26} 从未被选: {never or '无'}"
        if always:
            line += f"   垄断(>80%): {always}"
        print(line)


def _print_collapse(summary):
    """Flag strategy pairs whose Day-8 action distribution is near-identical."""
    print("\n=== 策略间同质化检查（行动分布余弦相似度 > 0.995 视为坍缩）===")
    marginals = action_marginals(summary)
    names = list(marginals)
    flagged = 0
    for i in range(len(names)):
        for j in range(i + 1, len(names)):
            a, b = marginals[names[i]], marginals[names[j]]
            va = [a[k] for k in DAY8_OPTIONS]
            vb = [b[k] for k in DAY8_OPTIONS]
            na = sum(x * x for x in va) ** 0.5 or 1
            nb = sum(x * x for x in vb) ** 0.5 or 1
            sim = sum(x * y for x, y in zip(va, vb)) / (na * nb)
            if sim > 0.995:
                flagged += 1
                print(f"  !! {names[i]} ~ {names[j]}  余弦={sim:.4f}")
    if not flagged:
        print("  未发现行动分布坍缩。")


def _print_consequence(records, strategy_count):
    """Report whether the player's history outweighs the agent's luck."""
    cw = consequence_weight(records)
    hs, ps = cw["history_modal_share"], cw["personality_modal_share"]
    print("\n=== 后果权重：玩家历史 vs agent 随机性格 ===")
    print(f"  固定 agent 性格，跨 {strategy_count} 种玩家历史："
          f"众数行动组合占比 mean={hs:.1%}，"
          f"不同组合数 mean={cw['history_distinct_mean']:.2f} "
          f"range=[{cw['history_distinct_range'][0]},{cw['history_distinct_range'][1]}]")
    print(f"  固定玩家策略，跨 {cw['personality_trials']} 个 agent 性格："
          f"众数行动组合占比 mean={ps:.1%}，"
          f"不同组合数 mean={cw['personality_distinct_mean']:.2f} "
          f"range=[{cw['personality_distinct_range'][0]},{cw['personality_distinct_range'][1]}]")
    if hs < ps:
        print("  → 玩家历史比 agent 随机性格更能改变第八天结果（因果链成立）。")
    else:
        print("  !! agent 随机性格比玩家历史更能改变第八天结果——后果权重可疑。")


def _reproducibility(seeds):
    print("\n=== 同种子复现检查（同 seed 跑两次，逐字比较 transcript）===")
    from core.full_game import run_full_game
    from analysis.strategy_lab import injected_profiles
    mismatches = []
    with injected_profiles():
        for strategy in ALL_STRATEGIES:
            for seed in seeds:
                a = run_full_game(seed, strategy)
                b = run_full_game(seed, strategy)
                if a.transcript != b.transcript or a.day8_actions != b.day8_actions:
                    mismatches.append((strategy, seed))
    if mismatches:
        print(f"  !! {len(mismatches)} 处不可复现: {mismatches[:10]}")
    else:
        print(f"  全部可复现（{len(ALL_STRATEGIES)} 策略 x {len(list(seeds))} 种子 x 2 次）。")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=100)
    ap.add_argument("--seed-start", type=int, default=0)
    ap.add_argument("--strategies", nargs="*", default=None)
    ap.add_argument("--json", default=None)
    ap.add_argument("--lineage", type=int, default=0)
    ap.add_argument("--repro", type=int, default=0)
    ap.add_argument("--audit", action="store_true",
                    help="深查记忆清除契约：证据 ID 修剪、报告引用、被删记忆泄漏")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    seeds = range(args.seed_start, args.seed_start + args.seeds)
    strategies = args.strategies or list(ALL_STRATEGIES)

    if args.lineage:
        print(f"=== 谱系链测试：{args.lineage} 代 ===")
        path = "runs/analysis_lineage.json"
        for strategy in strategies[:1]:
            records = run_lineage(seed=1000, strategy=strategy, generations=args.lineage, path=path)
            for r in records:
                print(f"  第{r['generation']}代 策略={r['strategy']} 继承=“{r['inherited']}” "
                      f"方向={r['inherited_direction']} 行动={r['actions']} 结局={r['ending']}")
        return 0

    records = run_matrix(strategies, seeds)
    summary = summarise(records)

    problems = [r for r in records if r["problems"]]
    print(f"=== 运行 {len(records)} 局（{len(strategies)} 策略 x {len(list(seeds))} 种子）===")
    if problems:
        print(f"!! 契约违规 {len(problems)} 局：")
        kinds = Counter(p for r in problems for p in r["problems"])
        for kind, n in kinds.most_common():
            print(f"     {kind}: {n}")
    else:
        print("契约不变量：全部通过（3 行动 / 门控互斥 / 电池非负 / 合法行动 / 玩家选择独立记录）。")

    if not args.quiet:
        print(format_summary(summary, strategies))
        _print_marginals(summary)
        _print_dominance(summary)
        _print_collapse(summary)
        _print_consequence(records, len(strategies))

    if args.repro:
        _reproducibility(range(args.repro))

    if args.audit:
        audit = audit_memory_integrity(strategies, seeds)
        print("\n=== 记忆清除契约深查 ===")
        print(f"  运行 {audit['runs']} 局，其中 {audit['runs_with_deletion']} 局真实删除，"
              f"共删除 {audit['events_removed_total']} 条事件。")
        print(f"  违规：{len(audit['violations'])}")
        for v in audit["violations"][:10]:
            print(f"    {v}")
        if not audit["violations"]:
            print("  信念证据 ID 全部存在；报告只引用存活 ID；"
                  "被删记忆未出现在报告或清除后的叙述中；"
                  "承诺引用与实验室可信度引用均已修剪。")

    if args.json:
        os.makedirs(os.path.dirname(args.json), exist_ok=True)
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump({"summary": summary, "records": records}, f,
                      ensure_ascii=False, indent=2, default=str)
        print(f"\n[分析] 已写出 {args.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
