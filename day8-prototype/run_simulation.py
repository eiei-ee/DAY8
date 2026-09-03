"""《第八天》里程碑0：纯代码因果链验证。

用法：
  python run_simulation.py --seed 42 --strategy always_keep --verbose
  python run_simulation.py --batch          # 多策略×多种子，输出最终行为分布
"""
import argparse
import sys

from core.recorder import run_batch
from core.simulation import run_game

STRATEGIES = ["always_keep", "always_betray", "kind_then_betray",
              "random", "lab_obedient", "ai_protector"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--strategy", default="always_keep", choices=STRATEGIES)
    ap.add_argument("--verbose", action="store_true")
    ap.add_argument("--batch", action="store_true", help="运行 6 策略 × 5 种子")
    args = ap.parse_args()

    if args.batch:
        rows, dist = run_batch(STRATEGIES, seeds=[7, 42, 99, 123, 2026])
        print(f"{'策略':<18}{'种子':<8}最终选择")
        for strategy, seed, choice, _ in rows:
            print(f"{strategy:<18}{seed:<8}{choice}")
        print("\n=== 最终行为分布（同场景、不同历史 → 不同选择） ===")
        for strategy, choices in dist.items():
            total = sum(choices.values())
            summary = ", ".join(f"{c}: {n}/{total}" for c, n in sorted(choices.items()))
            print(f"{strategy:<18}{summary}")
        return 0

    r = run_game(args.seed, args.strategy, verbose=True)
    from core.recorder import record_run
    path = record_run(r)
    print(f"\n[RunRecorder] 完整记录已保存：{path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
