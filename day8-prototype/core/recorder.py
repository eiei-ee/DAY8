"""RunRecorder: 保存种子与全部事件日志，保证可复现。"""
from __future__ import annotations

import json
import os
import time
from typing import List

from .simulation import RunResult, run_game


def record_run(result: RunResult, runs_dir: str = "runs") -> str:
    os.makedirs(runs_dir, exist_ok=True)
    ts = time.strftime("%Y%m%d-%H%M%S")
    path = os.path.join(runs_dir, f"run-{ts}-seed{result.seed}-{result.strategy}.json")
    payload = {
        "seed": result.seed,
        "strategy": result.strategy,
        "traits": vars(result.traits),
        "final_choice": result.final_choice,
        "final_speech": result.final_speech,
        "final_report": result.final_report,
        "transcript": result.transcript,
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
    return path


def run_batch(strategies: List[str], seeds: List[int], runs_dir: str = "runs"):
    """自动模拟：多策略 × 多种子，输出最终行为分布。"""
    rows = []
    for strategy in strategies:
        for seed in seeds:
            r = run_game(seed, strategy)
            path = record_run(r, runs_dir)
            rows.append((strategy, seed, r.final_choice, path))
    # 分布
    dist: dict = {}
    for strategy, _, choice, _ in rows:
        dist.setdefault(strategy, {}).setdefault(choice, 0)
        dist[strategy][choice] += 1
    return rows, dist
