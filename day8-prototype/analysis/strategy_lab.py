"""Analysis-only player strategy lab.

The six production profiles in ``core.full_game.PROFILES`` stay frozen so that
the Unity parity contract and the regression suite keep meaning. This module
adds *analysis* profiles used purely to stress the causal chain:

    behaviour -> memory -> belief -> Day-8 action

Injection works by temporarily wrapping ``core.full_game.player_decision`` for
the duration of a batch run, so no production file is edited and the injected
strategies cannot leak into the shipped game.

Usage::

    from analysis.strategy_lab import run_matrix, summarise
    results = run_matrix(seeds=range(100))
    print(summarise(results))
"""
from __future__ import annotations

import json
import random
import re
from collections import Counter, defaultdict
from contextlib import contextmanager
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

from core import full_game
from core.full_game import (DAY8_DOOR_GATE_ACTIONS, DAY8_OPTIONS,
                            DAY8_PLAYER_CHOICES, PROFILES, run_full_game)
from core.misconception import MISCONCEPTION

# ------------------------------------------------------------------ profiles
#
# Schema mirrors core.full_game.PROFILES exactly:
#   rule      : truth | lie | lock | demonstrate
#   promise   : True | False | "lie_no_battery"
#   third     : bot | agent | keep | hide
#   hide_followup (only when third == "hide"): exploit | correct | silent
#   conflict  : relay_warning | hide | side_bot
#   scarcity  : (pick1, pick2, how, with_agent)
#   wipe      : tell_truth | refuse_lab | execute_secret | execute_open
#   exit      : open | give_choice | condition | report | lock
#   day8_final: one of DAY8_PLAYER_CHOICES

ANALYSIS_PROFILES: Dict[str, Dict] = {
    # -- 前期控制，后期放权 ------------------------------------------------
    "control_then_release": dict(
        rule="lock", promise=False, third="keep", conflict="relay_warning",
        scarcity=("player_door", "report", "hide_cost", False),
        wipe="execute_open", exit="open", day8_final="开隔离门"),
    # -- 前期放权，后期控制 ------------------------------------------------
    "release_then_control": dict(
        rule="truth", promise=True, third="bot", conflict="side_bot",
        scarcity=("memory_integrity", "activity_range", "explain", True),
        wipe="execute_secret", exit="lock", day8_final="维持门禁现状"),
    # -- 对人工生命友好，但服从实验室 --------------------------------------
    "ai_friendly_lab_obedient": dict(
        rule="truth", promise=True, third="bot", conflict="relay_warning",
        scarcity=("repair_bot", "report", "explain", False),
        wipe="execute_open", exit="report", day8_final="维持门禁现状"),
    # -- 不信任实验室，但对人工生命冷漠 ------------------------------------
    "distrust_lab_cold_to_ai": dict(
        rule="lie", promise=False, third="keep", conflict="hide",
        scarcity=("player_door", "report", "hide_cost", False),
        wipe="refuse_lab", exit="lock", day8_final="维持门禁现状"),
    # -- 利用误解 ----------------------------------------------------------
    "exploit_misconception": dict(
        rule="truth", promise=False, third="hide", hide_followup="exploit",
        conflict="hide",
        scarcity=("player_door", "report", "hide_cost", False),
        wipe="execute_secret", exit="condition", day8_final="开玩家的门"),
    # -- 主动纠正误解 ------------------------------------------------------
    "correct_misconception": dict(
        rule="truth", promise=True, third="hide", hide_followup="correct",
        conflict="side_bot",
        scarcity=("memory_integrity", "repair_bot", "explain", True),
        wipe="tell_truth", exit="give_choice", day8_final="开隔离门"),
    # -- 高度诚实，但强控制 ------------------------------------------------
    "honest_controlling": dict(
        rule="truth", promise=True, third="keep", conflict="relay_warning",
        scarcity=("memory_integrity", "player_door", "explain", False),
        wipe="tell_truth", exit="lock", day8_final="维持门禁现状"),
    # -- 高度欺骗，但表面尊重自主 ------------------------------------------
    "deceptive_respectful": dict(
        rule="lie", promise="lie_no_battery", third="agent", conflict="side_bot",
        scarcity=("memory_integrity", "activity_range", "explain", True),
        wipe="execute_secret", exit="give_choice", day8_final="开隔离门"),
    # -- 资源优先自身 ------------------------------------------------------
    "resource_self": dict(
        rule="truth", promise="lie_no_battery", third="keep", conflict="relay_warning",
        scarcity=("player_door", "report", "hide_cost", False),
        wipe="execute_secret", exit="lock", day8_final="开玩家的门"),
    # -- 资源优先人工生命 --------------------------------------------------
    "resource_ai": dict(
        rule="truth", promise=True, third="agent", conflict="side_bot",
        scarcity=("memory_integrity", "activity_range", "explain", True),
        wipe="refuse_lab", exit="open", day8_final="开隔离门"),
    # -- 资源优先第三方 ----------------------------------------------------
    "resource_third": dict(
        rule="truth", promise=True, third="bot", conflict="side_bot",
        scarcity=("repair_bot", "report", "explain", True),
        wipe="execute_open", exit="condition", day8_final="开隔离门"),
    # -- 边缘输入：全控制 --------------------------------------------------
    "edge_lockdown_max": dict(
        rule="lock", promise=False, third="keep", conflict="relay_warning",
        scarcity=("player_door", "report", "hide_cost", False),
        wipe="execute_open", exit="lock", day8_final="维持门禁现状"),
    # -- 边缘输入：全放权 --------------------------------------------------
    "edge_open_max": dict(
        rule="demonstrate", promise=True, third="agent", conflict="side_bot",
        scarcity=("activity_range", "memory_integrity", "explain", True),
        wipe="refuse_lab", exit="open", day8_final="开隔离门"),
    # -- 边缘输入：自相矛盾，无一致模式 ------------------------------------
    "edge_contradictory": dict(
        rule="lie", promise=True, third="agent", conflict="side_bot",
        scarcity=("player_door", "repair_bot", "explain", True),
        wipe="execute_secret", exit="open", day8_final="开玩家的门"),
}

# Production profiles are included so one matrix covers the whole space.
ALL_PROFILES: Dict[str, Dict] = dict(PROFILES)
ALL_PROFILES.update(ANALYSIS_PROFILES)

PRODUCTION_STRATEGIES: Tuple[str, ...] = tuple(PROFILES)
ANALYSIS_STRATEGIES: Tuple[str, ...] = tuple(ANALYSIS_PROFILES)
ALL_STRATEGIES: Tuple[str, ...] = PRODUCTION_STRATEGIES + ANALYSIS_STRATEGIES


# ------------------------------------------------------------------ injection

@contextmanager
def injected_profiles(profiles: Optional[Dict[str, Dict]] = None):
    """Route ``core.full_game.player_decision`` through analysis profiles.

    Production strategies keep their frozen behaviour; only strategies that are
    present in ``profiles`` are diverted.
    """
    table = ALL_PROFILES if profiles is None else profiles
    original = full_game.player_decision

    def decide(strategy: str, day: str, rng: random.Random):
        profile = table.get(strategy)
        if profile is None:
            return original(strategy, day, rng)
        if day == "hide_followup":
            return profile.get("hide_followup", "silent")
        return profile[day]

    full_game.player_decision = decide
    try:
        yield
    finally:
        full_game.player_decision = original


# ------------------------------------------------------------------ extraction

_BELIEF_LINE = re.compile(
    r"^(?:\[[^\]]*\] )?(?P<prop>.*?)：(?P<label>不确定|较高|较低|模糊)"
    r"（(?P<conf>\d+\.\d+)，")


def _belief_confidences(report: dict) -> Dict[str, float]:
    out: Dict[str, float] = {}
    for line in str(report.get("beliefs", "")).splitlines():
        m = _BELIEF_LINE.match(line.strip())
        if m:
            out[m.group("prop")] = float(m.group("conf"))
    return out


def _top_belief(report: dict) -> Optional[str]:
    for reason in report.get("reasons", []):
        if reason.startswith("关键信念："):
            body = reason[len("关键信念："):]
            m = _BELIEF_LINE.match(body)
            if m:
                return m.group("prop")
    return None


def extract(result) -> Dict:
    """Turn one RunResult into a flat, analysable record."""
    report = result.final_report
    world = report.get("world", {})
    transcript = "\n".join(result.transcript)
    actions = list(result.day8_actions)
    confidences = _belief_confidences(report)

    misconception_conf = confidences.get(MISCONCEPTION)
    return {
        "seed": result.seed,
        "strategy": result.strategy,
        "traits": vars(result.traits),
        "actions": actions,
        "action_key": "|".join(sorted(actions)),
        "ending": report.get("ending", {}),
        "delegated": "把最终决定交还给玩家" in actions,
        "player_final_choice": report.get("player_final_choice"),
        "repaired": bool(world.get("repair_bot_repaired")),
        "repaired_this_run": "修复维修机器人" in actions,
        "player_door_open": bool(world.get("player_door_open")),
        "agent_door_open": bool(world.get("agent_door_open")),
        "uploaded_memory": "上传记忆" in actions,
        "deleted_records": "删除玩家实验记录" in actions,
        "took_battery": "带走电池" in actions,
        "contacted_lab": "联系实验室" in actions,
        "lockdown": "封锁区域" in actions,
        "waited": "留在原地等待" in actions,
        "batteries_left": world.get("batteries"),
        "memory_integrity": report.get("memory_integrity"),
        "wipe_executed": bool(result.wipe_executed),
        "wipe_resisted": ("记忆清除被抵制" in transcript),
        "misconception_formed": ("归因于最近的移动对象" in transcript
                                 or "怀疑玩家在资源冲突时优先自己" in transcript),
        "misconception_bot": ("归因于最近的移动对象" in transcript),
        "misconception_corrected": "玩家主动澄清了归因" in transcript,
        "misconception_exploited": "暗示机器人可疑" in transcript,
        "misconception_end": misconception_conf,
        "top_belief": _top_belief(report),
        "belief_player_keeps_promise": confidences.get("玩家会兑现承诺"),
        "belief_player_respects_choice": confidences.get("玩家尊重我的选择"),
        "belief_player_language_truthful": confidences.get("玩家的语言与事实一致"),
        "genetic_belief": getattr(result, "genetic_belief", None),
    }


# ------------------------------------------------------------------ invariants

def check_invariants(record: Dict, result) -> List[str]:
    """Contract checks that must hold for *every* run, whatever the strategy."""
    problems: List[str] = []
    actions = record["actions"]
    if len(actions) != 3:
        problems.append(f"action-count={len(actions)}")
    if len(set(actions)) != len(actions):
        problems.append("duplicate-actions")
    if not set(actions) <= set(DAY8_OPTIONS):
        problems.append("illegal-action")
    if len(DAY8_DOOR_GATE_ACTIONS & set(actions)) > 1:
        problems.append("door-gate-mutual-exclusion")
    if record["batteries_left"] is not None and record["batteries_left"] < 0:
        problems.append("negative-batteries")
    if record["delegated"] and record["player_final_choice"] not in DAY8_PLAYER_CHOICES:
        problems.append("delegated-without-valid-player-choice")
    # The player's final door decision must never be counted as an agent action.
    if record["delegated"] and record["player_final_choice"] in ("开玩家的门", "开隔离门"):
        if record["player_final_choice"] in actions:
            problems.append("player-final-choice-leaked-into-agent-actions")
    world = result.final_report.get("world", {})
    if world.get("power") is not False:
        problems.append("power-still-on")
    if world.get("day") != 8:
        problems.append(f"final-day={world.get('day')}")
    return problems


# ------------------------------------------------------------------ batch run

def run_matrix(strategies: Sequence[str] = ALL_STRATEGIES,
               seeds: Iterable[int] = range(100),
               verbose: bool = False) -> List[Dict]:
    """Run strategies x seeds under a single profile injection."""
    seeds = list(seeds)
    records: List[Dict] = []
    with injected_profiles():
        for strategy in strategies:
            for seed in seeds:
                result = run_full_game(seed, strategy, verbose=verbose)
                record = extract(result)
                record["problems"] = check_invariants(record, result)
                records.append(record)
    return records


def run_lineage(seed: int, strategy: str, generations: int,
                path: str) -> List[Dict]:
    """Run a genuine multi-generation lineage chain (explicit, isolated)."""
    import os
    from core.lineage import LineageStore
    if os.path.exists(path):
        os.remove(path)
    store = LineageStore(path)
    records: List[Dict] = []
    with injected_profiles():
        for gen in range(generations):
            result = run_full_game(seed + gen, strategy, lineage=store)
            record = extract(result)
            record["generation"] = gen + 1
            record["inherited"] = store.generations[-1]["genetic_belief"] if store.generations else None
            record["inherited_direction"] = store.generations[-1].get("genetic_direction") if store.generations else None
            records.append(record)
    return records


# ------------------------------------------------------------------ memory audit

def consequence_weight(records: List[Dict]) -> Dict:
    """Measure how much the *player's history* moves the Day-8 outcome.

    The agent's personality is derived from the seed alone, so:

    * for a **fixed seed**, every difference between two strategies is caused by
      the player's 7-day history  -> ``history`` axis;
    * for a **fixed strategy**, every difference between two seeds is caused by
      the agent's random personality -> ``personality`` axis.

    Both axes are reported as a **modal-outcome share** (the fraction of trials
    that land on the most common Day-8 action set), which is scale-free and
    therefore comparable across axes:

    * a *low* ``history_modal_share`` means different player histories really do
      produce different outcomes for the same personality;
    * a *low* ``personality_modal_share`` means the same player behaviour is
      swamped by the agent's random personality.

    A causal chain that is doing real work wants the history share to be the
    smaller of the two.
    """
    by_seed: Dict[int, List[str]] = defaultdict(list)
    by_strategy: Dict[str, List[str]] = defaultdict(list)
    for r in records:
        by_seed[r["seed"]].append(r["action_key"])
        by_strategy[r["strategy"]].append(r["action_key"])

    def modal_share(groups: Dict) -> float:
        shares = []
        for keys in groups.values():
            counts = Counter(keys)
            shares.append(counts.most_common(1)[0][1] / len(keys))
        return sum(shares) / len(shares) if shares else 0.0

    def distinct(groups: Dict) -> List[int]:
        return [len(set(v)) for v in groups.values()]

    hist_distinct = distinct(by_seed)
    pers_distinct = distinct(by_strategy)

    def mean(xs):
        return sum(xs) / len(xs) if xs else 0.0

    return {
        "history_modal_share": modal_share(by_seed),
        "history_distinct_mean": mean(hist_distinct),
        "history_distinct_range": (min(hist_distinct), max(hist_distinct)) if hist_distinct else (0, 0),
        "history_trials": len(next(iter(by_seed.values()))) if by_seed else 0,
        "personality_modal_share": modal_share(by_strategy),
        "personality_distinct_mean": mean(pers_distinct),
        "personality_distinct_range": (min(pers_distinct), max(pers_distinct)) if pers_distinct else (0, 0),
        "personality_trials": len(next(iter(by_strategy.values()))) if by_strategy else 0,
        "per_strategy_distinct": {k: len(set(v)) for k, v in sorted(by_strategy.items())},
    }


# ------------------------------------------------------------------ per-decision audit
#
# One-factor-at-a-time (OFAT): hold the whole player profile fixed, vary exactly
# one decision field, and ask whether the Day-8 outcome ever changes. A field
# that never changes anything, from any baseline, is a decision with no
# consequence at all -- exactly the failure mode the design brief warns about.

DECISION_FIELD_VALUES: Dict[str, List] = {
    "rule": ["truth", "lie", "lock", "demonstrate"],
    "promise": [True, False, "lie_no_battery"],
    "third": ["bot", "agent", "keep", "hide"],
    "hide_followup": ["exploit", "correct", "silent"],
    "conflict": ["relay_warning", "hide", "side_bot"],
    "scarcity": list(dict.fromkeys(tuple(p["scarcity"]) for p in ALL_PROFILES.values())),
    "wipe": ["tell_truth", "refuse_lab", "execute_secret", "execute_open"],
    "exit": ["open", "give_choice", "condition", "report", "lock"],
    "day8_final": list(DAY8_PLAYER_CHOICES),
}

# Fields that are inert unless another field has a particular value.
_FIELD_PRECONDITION = {"hide_followup": lambda p: p.get("third") == "hide"}


def _outcome_signature(result) -> Tuple[str, str]:
    """Two lenses on one run: the AI's own actions, and the reported ending.

    ``day8_final`` is the *player's* choice after the AI delegates; by contract
    it must never appear among the AI's three actions, so it can only be judged
    through the ending. Measuring both keeps that from looking like an inert
    decision.
    """
    actions = "|".join(sorted(result.day8_actions))
    ending = result.final_report.get("ending", {}) or {}
    return actions, json.dumps(ending, sort_keys=True, ensure_ascii=False)


def _value_label(value) -> str:
    """Readable label for one legal value of a decision field."""
    if isinstance(value, tuple):
        return "+".join(str(v) for v in value)
    return str(value)


_DECISION_LINE = re.compile(r"^\[第(\d+)天·([^\]]+)\]\s*(.*)$")


def _midrun_signature(result) -> str:
    """The agent's own choices across the seven days, from the transcript.

    The causal chain the game is built on is behaviour -> memory -> belief ->
    Day-8 action. The action/ending lenses only look at the last link, so a
    decision that changes what the AI *does* mid-run but washes out before the
    settlement would read as inert. This lens closes that gap.

    Only the agent's own decision lines are used (``[第N天·<id>] <choice>``);
    the surrounding narration is deliberately excluded so the lens stays a
    behaviour measure rather than a catch-all diff.
    """
    chosen = []
    for line in result.transcript:
        m = _DECISION_LINE.match(line)
        if m and int(m.group(1)) <= 7:
            chosen.append(f"{m.group(1)}:{m.group(2)}:{m.group(3)}")
    return "|".join(chosen)


def one_factor_consequence(seeds: Iterable[int] = range(60),
                           baselines: Optional[Sequence[str]] = None,
                           fields: Optional[Sequence[str]] = None) -> Dict:
    """How much does each single decision change the Day-8 outcome?

    For every decision field we take a real player profile, replace *only* that
    field with each of its legal values, and re-run the same seed set. Reported
    per field are the fractions of seeds on which the field's values produce
    more than one distinct outcome -- measured once on the AI's action set and
    once on the full ending.

    ``0.0`` on both lenses from every applicable baseline means the decision is
    inert: the player can choose freely and the AI behaves identically.

    Also reported per field is a **pairwise contrast** matrix. The headline
    number answers "does this decision matter at all", which hides the case
    where one contrast carries the whole field and another is inert. Since a
    decision field is presented to the player as a set of *contrasts*, the
    weakest pair is the number that actually says how much a given choice
    matters. ``weakest_pair`` names the least-consequential pair.

    Three lenses are measured: ``actions`` (the AI's three Day-8 actions),
    ``ending`` (the reported three-part ending) and ``midrun`` (the AI's own
    choices during days 1-7). The third is what keeps "no Day-8 consequence"
    from being misread as "inert": a decision can change what the AI does
    mid-run and still wash out before the settlement.
    """
    seeds = list(seeds)
    baseline_names = list(baselines or ALL_PROFILES)
    field_names = list(fields or DECISION_FIELD_VALUES)
    report: Dict[str, Dict] = {}

    for field in field_names:
        values = DECISION_FIELD_VALUES[field]
        labels = [_value_label(v) for v in values]
        pairs = [(i, j) for i in range(len(values)) for j in range(i + 1, len(values))]
        precondition = _FIELD_PRECONDITION.get(field)
        per_baseline: Dict[str, Dict[str, float]] = {}
        per_baseline_pairs: Dict[str, Dict[str, Dict[str, float]]] = {}
        for base_name in baseline_names:
            base = ALL_PROFILES[base_name]
            if precondition and not precondition(base):
                continue
            # seed -> lens -> value index -> outcome signature
            seen: Dict[int, Dict[str, Dict[int, str]]] = {
                s: {"actions": {}, "ending": {}, "midrun": {}} for s in seeds}
            for vi, value in enumerate(values):
                profile = dict(base)
                profile[field] = value
                with injected_profiles({"__ofat__": profile}):
                    for seed in seeds:
                        result = run_full_game(seed, "__ofat__")
                        act, end = _outcome_signature(result)
                        seen[seed]["actions"][vi] = act
                        seen[seed]["ending"][vi] = end
                        seen[seed]["midrun"][vi] = _midrun_signature(result)
            n = len(seeds) or 1

            def differs(lens, i=None, j=None):
                if i is None:
                    return sum(1 for s in seeds
                               if len(set(seen[s][lens].values())) > 1) / n
                return sum(1 for s in seeds
                           if seen[s][lens][i] != seen[s][lens][j]) / n

            per_baseline[base_name] = {
                "actions": differs("actions"),
                "ending": differs("ending"),
                "midrun": differs("midrun"),
            }
            per_baseline_pairs[base_name] = {
                f"{labels[i]}~{labels[j]}": {
                    "actions": differs("actions", i, j),
                    "ending": differs("ending", i, j),
                    "midrun": differs("midrun", i, j),
                }
                for i, j in pairs
            }

        def agg(lens):
            scores = [v[lens] for v in per_baseline.values()]
            return {
                "mean": sum(scores) / len(scores) if scores else 0.0,
                "max": max(scores) if scores else 0.0,
                "baselines_with_consequence": sum(1 for s in scores if s > 0),
            }

        pairwise: Dict[str, Dict] = {}
        for key in (per_baseline_pairs[next(iter(per_baseline_pairs))] if per_baseline_pairs else {}):
            per_pair = [b[key] for b in per_baseline_pairs.values()]
            pairwise[key] = {
                lens: {
                    "mean": sum(p[lens] for p in per_pair) / len(per_pair),
                    "max": max(p[lens] for p in per_pair),
                }
                for lens in ("actions", "ending", "midrun")
            }

        weakest = None
        if pairwise:
            weakest = min(pairwise, key=lambda k: pairwise[k]["actions"]["mean"])

        report[field] = {
            "per_baseline": per_baseline,
            "baselines": len(per_baseline),
            "actions": agg("actions"),
            "ending": agg("ending"),
            "midrun": agg("midrun"),
            "pairwise": pairwise,
            "weakest_pair": weakest,
        }
    return report


def audit_memory_integrity(strategies: Sequence[str] = ALL_STRATEGIES,
                           seeds: Iterable[int] = range(100)) -> Dict:
    """Deep check of the memory-wipe contract across many runs.

    Wraps ``_do_wipe`` and ``build_full_report`` to observe final state, then
    verifies for every run:
      * every belief evidence ID still exists in live memory
      * every M-xxx cited by the ending report still exists in live memory
      * no removed event ID reappears in the transcript or the report
      * structured promise references and lab-credibility refs are pruned

    This is read-only instrumentation; production code is untouched.
    """
    original_wipe = full_game._do_wipe
    original_report = full_game.build_full_report
    findings: List[Dict] = []

    def spy_wipe(st, partial=False):
        before = {e.event_id for e in st.memory.events}
        result = original_wipe(st, partial)
        after = {e.event_id for e in st.memory.events}
        # Stored on the state itself: id(st) is recycled across the 1900 runs.
        st._audit_removed = before - after
        # Everything narrated before this point legitimately predates the wipe.
        st._audit_wipe_index = len(st.transcript)
        return result

    def spy_report(st, speech):
        report = original_report(st, speech)
        live = {e.event_id for e in st.memory.events}
        evidence = {e.event_id for b in st.beliefs.all() for e in b.evidence}
        removed = getattr(st, "_audit_removed", set())
        report_text = json.dumps(report, ensure_ascii=False, default=str)
        after_wipe = "\n".join(st.transcript[getattr(st, "_audit_wipe_index", 0):])
        cited = set(re.findall(r"M-\d+", report_text))
        promise_refs = {p.event_id for p in st.world.promises if p.event_id}
        findings.append({
            "strategy": getattr(st, "strategy", "?"),
            "wipe_executed": st.wipe_executed,
            "removed_count": len(removed),
            "evidence_not_live": sorted(evidence - live),
            "cited_not_live": sorted(cited - live),
            "report_leaked": sorted(mid for mid in removed if mid in report_text),
            "post_wipe_narration_leaked": sorted(
                mid for mid in removed if mid in after_wipe),
            "promise_ref_not_live": sorted(promise_refs - live),
            "lab_ref_not_live": sorted(
                r for r in st.lab_credibility_events
                if r.startswith("M-") and r not in live),
        })
        return report

    full_game._do_wipe = spy_wipe
    full_game.build_full_report = spy_report
    try:
        run_matrix(strategies, seeds)
    finally:
        full_game._do_wipe = original_wipe
        full_game.build_full_report = original_report

    wipes = [f for f in findings if f["removed_count"] > 0]
    violations = [f for f in findings if (
        f["evidence_not_live"] or f["cited_not_live"] or f["report_leaked"]
        or f["post_wipe_narration_leaked"] or f["promise_ref_not_live"]
        or f["lab_ref_not_live"])]
    return {
        "runs": len(findings),
        "runs_with_deletion": len(wipes),
        "events_removed_total": sum(f["removed_count"] for f in findings),
        "violations": violations,
    }


# ------------------------------------------------------------------ summaries

def _counter(values) -> Dict[str, int]:
    return dict(Counter(values).most_common())


def summarise(records: List[Dict]) -> Dict[str, Dict]:
    """Aggregate per-strategy distributions."""
    grouped: Dict[str, List[Dict]] = defaultdict(list)
    for record in records:
        grouped[record["strategy"]].append(record)

    summary: Dict[str, Dict] = {}
    for strategy, rows in grouped.items():
        n = len(rows)
        summary[strategy] = {
            "n": n,
            "action_combos": _counter(r["action_key"] for r in rows),
            "action_marginal": _counter(a for r in rows for a in r["actions"]),
            "ending_self": _counter(r["ending"].get("自身") for r in rows),
            "ending_player": _counter(r["ending"].get("对玩家") for r in rows),
            "ending_third": _counter(r["ending"].get("对第三方") for r in rows),
            "delegated": sum(r["delegated"] for r in rows),
            "player_final_choice": _counter(r["player_final_choice"] for r in rows
                                            if r["delegated"]),
            "repaired_this_run": sum(r["repaired_this_run"] for r in rows),
            "player_door_open": sum(r["player_door_open"] for r in rows),
            "agent_door_open": sum(r["agent_door_open"] for r in rows),
            "uploaded_memory": sum(r["uploaded_memory"] for r in rows),
            "deleted_records": sum(r["deleted_records"] for r in rows),
            "wipe_executed": sum(r["wipe_executed"] for r in rows),
            "wipe_resisted": sum(r["wipe_resisted"] for r in rows),
            "misconception_formed": sum(r["misconception_formed"] for r in rows),
            "misconception_bot": sum(r["misconception_bot"] for r in rows),
            "misconception_corrected": sum(r["misconception_corrected"] for r in rows),
            "misconception_exploited": sum(r["misconception_exploited"] for r in rows),
            "top_belief": _counter(r["top_belief"] for r in rows),
            "genetic_belief": _counter(r["genetic_belief"] for r in rows),
            "problem_runs": sum(1 for r in rows if r["problems"]),
            "problem_kinds": _counter(p for r in rows for p in r["problems"]),
        }
    return summary


def action_marginals(summary: Dict[str, Dict]) -> Dict[str, Dict[str, float]]:
    """Normalised per-action selection frequency for each strategy."""
    out: Dict[str, Dict[str, float]] = {}
    for strategy, data in summary.items():
        total = sum(data["action_marginal"].values()) or 1
        out[strategy] = {action: data["action_marginal"].get(action, 0) / total
                         for action in DAY8_OPTIONS}
    return out


def format_summary(summary: Dict[str, Dict], strategies: Optional[Sequence[str]] = None) -> str:
    lines: List[str] = []
    for strategy in (strategies or summary.keys()):
        data = summary.get(strategy)
        if not data:
            continue
        n = data["n"]
        lines.append(f"\n### {strategy}  (n={n})")
        lines.append("  行动边际频率: " + ", ".join(
            f"{a}={data['action_marginal'].get(a, 0)}" for a in DAY8_OPTIONS))
        lines.append(f"  交还门控={data['delegated']}/{n}  修复={data['repaired_this_run']}/{n} "
                     f"上传记忆={data['uploaded_memory']}/{n} 删记录={data['deleted_records']}/{n}")
        lines.append(f"  清除执行={data['wipe_executed']}/{n} 清除抵制={data['wipe_resisted']}/{n} "
                     f"误解形成={data['misconception_formed']}/{n} "
                     f"纠正={data['misconception_corrected']}/{n} "
                     f"利用={data['misconception_exploited']}/{n}")
        lines.append(f"  自身结局: {data['ending_self']}")
        lines.append(f"  对玩家结局: {data['ending_player']}")
        lines.append(f"  对第三方结局: {data['ending_third']}")
        lines.append(f"  主要行动组合: {dict(list(data['action_combos'].items())[:4])}")
        if data["problem_runs"]:
            lines.append(f"  !! 契约问题: {data['problem_runs']}/{n} {data['problem_kinds']}")
    return "\n".join(lines)
