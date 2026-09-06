"""《第八天》完整版模拟 CLI（里程碑 0～5 的纯代码实现）。

用法：
  python run_full.py --seed 42 --strategy kind_then_betray --verbose
  python run_full.py --batch                     # 6 策略 × 5 种子 + 结局组合分布
  python run_full.py --lineage 3                 # 连续 3 代谱系传承
"""
import argparse
import sys
from collections import Counter

from core.full_game import run_full_game
from core.recorder import record_run
from core.lineage import LineageStore

STRATEGIES = ["always_keep", "always_betray", "kind_then_betray",
              "random", "lab_obedient", "ai_protector"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--strategy", default="kind_then_betray", choices=STRATEGIES)
    ap.add_argument("--verbose", action="store_true")
    ap.add_argument("--batch", action="store_true")
    ap.add_argument("--lineage", type=int, default=0, help="运行 N 代谱系")
    args = ap.parse_args()

    if args.lineage:
        import os
        path = "runs/lineage_demo.json"
        if os.path.exists(path):
            os.remove(path)
        lin = LineageStore(path)
        for gen in range(args.lineage):
            r = run_full_game(seed=gen * 11 + 5, strategy=STRATEGIES[gen % len(STRATEGIES)],
                              verbose=args.verbose, lineage=lin)
            print(f"第{gen + 1}代 seed={r.seed} 策略={r.strategy} 最终={r.final_choice} "
                  f"结局={r.ending} 遗传信念=“{r.genetic_belief}”")
        return 0

    if args.batch:
        rows = []
        endings = {}
        for strategy in STRATEGIES:
            for seed in [7, 42, 99, 123, 2026]:
                r = run_full_game(seed, strategy, verbose=args.verbose)
                record_run(r)
                rows.append((strategy, seed, r.ending))
                e = r.ending
                key = f"{e['自身']} / {e['对玩家']} / {e['对第三方']}"
                endings.setdefault(strategy, Counter())[key] += 1
        print("=== 结局组合分布（三维度：自身 / 对玩家 / 对第三方） ===")
        for strategy, counter in endings.items():
            print(f"\n{strategy}:")
            for combo, n in counter.most_common():
                print(f"  {combo}: {n}/5")
        return 0

    r = run_full_game(args.seed, args.strategy, verbose=args.verbose)
    path = record_run(r)
    print(f"\n[RunRecorder] 完整记录已保存：{path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
