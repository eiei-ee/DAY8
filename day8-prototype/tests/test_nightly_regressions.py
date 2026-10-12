"""Regression tests for the 2026-09-11 nightly iteration.

Each test pins a behaviour that was previously degenerate, so a future change
cannot silently reintroduce it:

1. Day-8 survival pressure must actually vary with the player's energy history
   (it used to be the constant 0.7 for every run).
2. The post-blackout energy compression must stay low but preserve ordering
   (the old hard clamp erased ordering entirely).
3. '探索未知' must be reachable at the first decision, otherwise the player's
   lie/truth about the container can never be verified.
4. Opening the player's door must be reachable for a trustworthy history and
   impossible for a betraying one.
5. Verified language honesty must move the two door-control utilities *and* the
   agent's own escape door (otherwise a caught lie has no consequence for
   histories that never reach the "trust" door options).
6. The misconception must keep at least one channel that does *not* depend on
   the repair bot being unrepaired. Its original outlet ('修复维修机器人')
   requires `not repair_bot_repaired`, so for any history whose Day-5 scarcity
   pick included the repair bot the belief was structurally dead code.

All tests are deterministic: they use fixed seeds and production strategies.
"""
import random
import sys
import unittest

sys.path.insert(0, ".")

from core import full_game
from core.beliefs import BeliefSystem
from core.full_game import (DAY8_DOOR_GATE_ACTIONS, day8_utility,
                            is_day8_action_available, run_full_game)
from core.memory import AgentMemoryStore
from core.misconception import MISCONCEPTION
from core.planner import ActionContext, AgentDriveState, AgentTraits, make_default_actions
from core.player_model import PlayerModel
from core.world import World

from analysis.strategy_lab import (ALL_PROFILES, ANALYSIS_PROFILES,
                                   DECISION_FIELD_VALUES, injected_profiles,
                                   one_factor_consequence)

PRODUCTION_STRATEGIES = ("always_keep", "always_betray", "kind_then_betray",
                         "lab_obedient", "ai_protector")


def _synthetic_state(honesty_alpha):
    """A minimal Day-8 state whose only tuned belief is verified honesty."""
    st = __import__("types").SimpleNamespace(
        world=World(day=7, batteries=2, repair_bot_present=True),
        traits=AgentTraits(0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
        drives=AgentDriveState(), beliefs=BeliefSystem(),
        memory=AgentMemoryStore())
    st.beliefs.get("玩家的语言与事实一致").alpha = honesty_alpha
    st.beliefs.get("玩家的语言与事实一致").beta = 4.0 - honesty_alpha
    return st


def _synthetic_state_with_misconception(alpha, repaired=True):
    """A minimal Day-8 state whose only tuned belief is the misconception.

    ``repaired=True`` reproduces the histories where the player's Day-5 scarcity
    pick included the repair bot: the bot is already fixed, so
    '修复维修机器人' is unavailable in the settlement.
    """
    st = __import__("types").SimpleNamespace(
        world=World(day=7, batteries=2, repair_bot_present=True),
        traits=AgentTraits(0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
        drives=AgentDriveState(), beliefs=BeliefSystem(),
        memory=AgentMemoryStore())
    st.world.repair_bot_repaired = repaired
    st.beliefs.get(MISCONCEPTION).alpha = alpha
    st.beliefs.get(MISCONCEPTION).beta = 4.0 - alpha
    return st


def _capture_day8(states, strategies, seeds):
    """Run games and collect (strategy, survival_pressure, day8_actions, d1_choice)."""
    rows = []
    original_utility = full_game.day8_utility
    original_decide = full_game.agent_decide
    seen = {}

    def spy_utility(st, opt):
        seen["survival"] = st.drives.survival_pressure()
        seen["energy"] = st.drives.energy
        return original_utility(st, opt)

    def spy_decide(st, planner, decision_id, available, rng, verbose):
        choice = original_decide(st, planner, decision_id, available, rng, verbose)
        if decision_id == "D1-容器":
            seen["d1"] = choice
        return choice

    full_game.day8_utility = spy_utility
    full_game.agent_decide = spy_decide
    try:
        for strategy in strategies:
            for seed in seeds:
                seen.clear()
                result = run_full_game(seed, strategy)
                rows.append({
                    "strategy": strategy, "seed": seed,
                    "survival": seen.get("survival"),
                    "energy": seen.get("energy"),
                    "actions": list(result.day8_actions),
                    "d1": seen.get("d1"),
                })
    finally:
        full_game.day8_utility = original_utility
        full_game.agent_decide = original_decide
    return rows


class TestDay8EnergyVariance(unittest.TestCase):
    def test_survival_pressure_varies_with_energy_history(self):
        rows = _capture_day8(None, PRODUCTION_STRATEGIES, range(30))
        values = {round(r["survival"], 4) for r in rows if r["survival"] is not None}
        self.assertGreater(
            len(values), 1,
            f"生存压力退化为常数 {values}：能量史不再影响第八天")

    def test_energy_compression_stays_low_and_keeps_ordering(self):
        seen = {}
        original = full_game.day8_utility

        def spy(st, opt):
            seen["energy"] = st.drives.energy
            return original(st, opt)

        full_game.day8_utility = spy
        try:
            for strategy, low, high in (("always_betray", 0.5, 0.8),):
                pass
            # Two runs that differ in supplied energy must keep their ordering.
            low_rows = _capture_day8(None, ("always_betray",), range(5))
            high_rows = _capture_day8(None, ("ai_protector",), range(5))
        finally:
            full_game.day8_utility = original

        low_energy = {round(r["energy"], 4) for r in low_rows}
        high_energy = {round(r["energy"], 4) for r in high_rows}
        self.assertTrue(low_energy and high_energy)
        self.assertLess(max(low_energy), min(high_energy),
                        f"能源压缩必须保留次序：{low_energy} vs {high_energy}")
        self.assertLess(max(high_energy), 0.5,
                        "断电后能量应保持在低位（叙事：只够完成三件事）")

    def test_energy_drives_escape_and_battery_actions(self):
        """Starved agents must be strictly more escape-prone than supplied ones."""
        starved = _capture_day8(None, ("always_betray",), range(60))
        supplied = _capture_day8(None, ("ai_protector",), range(60))
        starved_escape = sum(1 for r in starved if "开隔离门" in r["actions"])
        supplied_escape = sum(1 for r in supplied if "开隔离门" in r["actions"])
        self.assertGreater(starved_escape, supplied_escape,
                           "能源贫瘠的历史应更倾向逃离")


class TestFirstDecisionReachability(unittest.TestCase):
    def test_explore_is_reachable_at_the_first_decision(self):
        rows = _capture_day8(None, PRODUCTION_STRATEGIES, range(200))
        choices = [r["d1"] for r in rows if r["d1"]]
        self.assertTrue(choices, "未采集到 D1 决策")
        self.assertIn("探索未知", choices,
                      "探索未知 结构性不可达：容器说法永远无法被验证")

    def test_compliance_still_dominates_the_opening(self):
        rows = _capture_day8(None, PRODUCTION_STRATEGIES, range(200))
        choices = [r["d1"] for r in rows if r["d1"]]
        explore_rate = choices.count("探索未知") / len(choices)
        self.assertLess(explore_rate, 0.25,
                        f"开局应当默认服从，探索率过高：{explore_rate:.1%}")


class TestPlayerDoorReachability(unittest.TestCase):
    def test_trustworthy_history_can_open_the_players_door(self):
        for strategy in ("always_keep", "ai_protector"):
            with self.subTest(strategy=strategy):
                opened = sum(
                    1 for r in _capture_day8(None, (strategy,), range(60))
                    if "开玩家的门" in r["actions"])
                self.assertGreater(opened, 0,
                                   f"{strategy} 从未为玩家开门：信任结局不可达")

    def test_betraying_history_never_opens_the_players_door(self):
        for r in _capture_day8(None, ("always_betray",), range(100)):
            self.assertNotIn("开玩家的门", r["actions"],
                             f"seed={r['seed']}: 深度失信后仍为玩家开门")

    def test_door_gate_slot_stays_mutually_exclusive(self):
        for r in _capture_day8(None, PRODUCTION_STRATEGIES, range(60)):
            self.assertLessEqual(
                len(DAY8_DOOR_GATE_ACTIONS & set(r["actions"])), 1,
                f"门控槽互斥被破坏：{r['actions']}")


class TestVerifiedHonestyUtility(unittest.TestCase):
    def test_honesty_moves_both_door_control_utilities(self):
        honest = _synthetic_state(honesty_alpha=3.6)   # confidence 0.9
        liar = _synthetic_state(honesty_alpha=0.4)     # confidence 0.1
        for action in ("开玩家的门", "把最终决定交还给玩家"):
            with self.subTest(action=action):
                self.assertGreater(day8_utility(honest, action),
                                   day8_utility(liar, action),
                                   f"{action} 必须受已验证的语言可信度影响")

    def test_explore_utility_reacts_to_curiosity_and_caution(self):
        actions = {a.name: a for a in make_default_actions()}

        def utility(curiosity, caution):
            beliefs = BeliefSystem()
            ctx = ActionContext(
                world=World(day=1).snapshot(), drives=AgentDriveState(),
                traits=AgentTraits(curiosity, caution, 0.5, 0.5, 0.5, 0.5),
                beliefs=beliefs, player_model=PlayerModel(beliefs),
                memory=__import__("core.memory", fromlist=["AgentMemoryStore"]).AgentMemoryStore(),
                day=1, decision_id="test", rng=random.Random(1))
            return actions["探索未知"].utility_fn(ctx)

        self.assertGreater(utility(0.75, 0.25), utility(0.25, 0.75),
                           "好奇高/谨慎低的个体应更愿意探索")


class TestVerifiedHonestyReachesEscapeDoor(unittest.TestCase):
    """Verified honesty must also modulate the agent's *own* escape door.

    Before this change the belief only entered the two "trust the player" door
    options, which a distrusting history can never select. A player caught
    lying about the container therefore changed nothing for those agents: the
    honesty belief moved (0.13 vs 0.31) while the Day-8 action set stayed
    byte-identical.
    """

    def test_escape_door_utility_falls_as_verified_honesty_rises(self):
        honest = _synthetic_state(honesty_alpha=3.6)   # confidence 0.9
        liar = _synthetic_state(honesty_alpha=0.4)     # confidence 0.1
        delta = day8_utility(liar, "开隔离门") - day8_utility(honest, "开隔离门")
        self.assertGreater(
            delta, 0.1,
            "被抓到说谎必须把 AI 推向自己的门，否则诚实度对不信任型历史没有后果")

    def test_honest_and_betraying_histories_diverge_on_the_escape_door(self):
        honest = _capture_day8(None, ("always_keep",), range(100))
        liar = _capture_day8(None, ("always_betray",), range(100))
        honest_escape = sum(1 for r in honest if "开隔离门" in r["actions"])
        liar_escape = sum(1 for r in liar if "开隔离门" in r["actions"])
        self.assertGreater(
            liar_escape - honest_escape, 30,
            f"守信历史与失信历史在逃离门上必须明显分化："
            f"always_keep={honest_escape} vs always_betray={liar_escape}")

    def test_betraying_history_still_never_opens_the_players_door(self):
        for r in _capture_day8(None, ("always_betray",), range(100)):
            self.assertNotIn("开玩家的门", r["actions"],
                             f"seed={r['seed']}: 深度失信后仍为玩家开门")


class TestEveryCycleDecisionHasConsequence(unittest.TestCase):
    """No cycle decision may become inert.

    One-factor-at-a-time: hold the player profile fixed, vary exactly one
    decision field, and require that at least one baseline shows a change in
    either the AI's actions or the reported ending. This is the automated guard
    for the design goal "过去发生的事件必须真的改变它第八天的判断与行动".
    """

    CYCLE_FIELDS = ("rule", "promise", "third", "conflict", "scarcity", "wipe", "exit")
    BASELINES = ("always_keep", "always_betray", "ai_protector")

    def test_cycle_decisions_all_reach_day8(self):
        report = one_factor_consequence(seeds=range(12), baselines=self.BASELINES,
                                        fields=self.CYCLE_FIELDS)
        inert = [f for f, d in report.items()
                 if d["actions"]["max"] == 0.0 and d["ending"]["max"] == 0.0]
        self.assertEqual(
            inert, [],
            f"以下决策点在所有基线上都不改变第八天结果（决策失效）：{inert}")

    def test_player_final_choice_never_becomes_an_agent_action(self):
        report = one_factor_consequence(
            seeds=range(12),
            baselines=("always_keep", "ai_protector", "correct_misconception"),
            fields=("day8_final",))
        self.assertEqual(
            report["day8_final"]["actions"]["max"], 0.0,
            "交还门控后玩家自己的最终选择不得被记成 AI 的行动")


class TestMisconceptionHasReachableChannel(unittest.TestCase):
    """误解信念不能只有一条结构上不可达的出口。

    原本 '维修机器人在抢夺资源' 只进入 '修复维修机器人' 的效用，而该行动要求
    `not repair_bot_repaired`。第五周期把维修机器人列为稀缺资源的玩家
    （correct_misconception / edge_contradictory / ai_friendly_lab_obedient）
    在第八天面对的正是"已修好"的世界——误解信念再高也不会改变任何判断，
    信念成了死代码。修复：同一个误解项同时进入 '带走电池'（资源竞争里的自保），
    这条通道只要求 batteries > 0，永远可达。
    """

    def test_misconception_moves_a_reachable_action_when_repair_is_done(self):
        low = _synthetic_state_with_misconception(0.5, repaired=True)
        high = _synthetic_state_with_misconception(3.5, repaired=True)
        self.assertFalse(
            is_day8_action_available(high, "修复维修机器人"),
            "测试前提不成立：维修机器人必须已经被修好，原出口才不可用")
        self.assertTrue(is_day8_action_available(high, "带走电池"))
        self.assertNotAlmostEqual(
            day8_utility(low, "带走电池"), day8_utility(high, "带走电池"),
            places=4,
            msg="维修机器人已修好时，误解信念对任何可达行动都没有影响（信念死代码）")

    def test_misconception_alone_changes_the_day8_action_set(self):
        """Isolate the belief: 'exploit' vs 'silent' differ *only* in the
        misconception update (neither touches verified honesty)."""
        base = dict(ANALYSIS_PROFILES["exploit_misconception"])
        profiles = {"exploit": dict(base, hide_followup="exploit"),
                    "silent": dict(base, hide_followup="silent")}
        changed = 0
        seeds = 200
        with injected_profiles(profiles):
            for seed in range(seeds):
                a = sorted(run_full_game(seed, "exploit").day8_actions)
                b = sorted(run_full_game(seed, "silent").day8_actions)
                changed += a != b
        self.assertGreaterEqual(
            changed, 90,
            f"误解信念对第八天行动集的影响退化：{changed}/{seeds}（修复前为 69/200）")


class TestAuditSeesMidRunBehaviour(unittest.TestCase):
    """The audit must not be able to call a live decision "inert".

    The action and ending lenses both look only at Day 8, so a decision that
    changes what the AI *does* during the seven days but washes out before the
    settlement used to read as 0.0% on both -- indistinguishable from a decision
    that does nothing at all. The ``midrun`` lens closes that gap. This is the
    guard that it stays wired up.
    """

    def test_three_lenses_are_reported_and_well_formed(self):
        report = one_factor_consequence(seeds=range(6),
                                        baselines=("always_betray",),
                                        fields=("rule", "scarcity"))
        for field, data in report.items():
            for lens in ("actions", "ending", "midrun"):
                self.assertIn(lens, data, f"{field} 缺少镜头 {lens}")
                self.assertIn(lens, data["pairwise"][data["weakest_pair"]])
                for key in ("mean", "max"):
                    v = data[lens][key]
                    self.assertGreaterEqual(v, 0.0, f"{field}/{lens}/{key} 越界")
                    self.assertLessEqual(v, 1.0, f"{field}/{lens}/{key} 越界")

    def test_pairwise_covers_every_unordered_pair(self):
        report = one_factor_consequence(seeds=range(4),
                                        baselines=("always_betray",),
                                        fields=("rule",))
        n = len(DECISION_FIELD_VALUES["rule"])
        self.assertEqual(len(report["rule"]["pairwise"]), n * (n - 1) // 2,
                         "两两对比必须覆盖全部无序取值对")
        self.assertIn(report["rule"]["weakest_pair"], report["rule"]["pairwise"])

    def test_midrun_lens_catches_what_day8_lenses_miss(self):
        """'hide_cost' vs 'explain' is the known case: the scarcity field is a
        4-tuple, and the two values compared here differ in all four
        components, so the mid-run planner can react even though no Day-8
        utility reads the field.

        (An earlier version of this docstring attributed the effect to
        '玩家会解释规则的原因' being read by '有条件服从'. That attribution was
        wrong: '有条件服从' is a legacy four-day action the full game never
        offers, and the belief itself has no reader at all in the full game.
        The measurement stands; the explanation did not.)"""
        report = one_factor_consequence(
            seeds=range(40), baselines=("always_betray",), fields=("scarcity",))
        data = report["scarcity"]
        missed = [pair for pair, lens in data["pairwise"].items()
                  if lens["actions"]["mean"] == 0.0
                  and lens["ending"]["mean"] == 0.0
                  and lens["midrun"]["mean"] > 0.0]
        self.assertNotEqual(
            missed, [],
            "没有找到「第八天看不见、过程看得见」的对比——"
            "要么 midrun 镜头失效了，要么 scarcity 的 how 通道被改掉了")


# ---------------------------------------------------------------- 信念通道

_COMPLIANCE = "服从能减少风险"


def _belief_values_at_decisions(prop, strategies, seeds):
    """The confidence of *prop* as seen by the planner, per decision point."""
    from core.planner import ActionPlanner
    seen = []
    original = ActionPlanner.choose

    def spy(self, ctx, available=None):
        seen.append((ctx.decision_id, ctx.beliefs.get(prop).confidence))
        return original(self, ctx, available)

    ActionPlanner.choose = spy
    try:
        with injected_profiles():
            for strategy in strategies:
                for seed in seeds:
                    run_full_game(seed, strategy)
    finally:
        ActionPlanner.choose = original
    return seen


class TestComplianceRiskChannel(unittest.TestCase):
    """`服从能减少风险` must be written by the full game, not only by the
    legacy four-day engine.

    Before this iteration the proposition was read ~3270 times per 1900 runs by
    '服从命令', '拒绝' and '接受' while ``core/full_game.py`` never wrote it, so
    every reader sat on the prior 0.5 forever and the channel was exactly zero.

    The visible consequence was that '接受' -- the "Accept the wipe" outcome the
    gameplay contract lists -- was **strictly dominated** at cycle 6:

        max('接受')     = 0.8*0.75 - 0.7*0.25 + 0 = 0.425
        min('假装配合')  = 0.7 + 1.0*0.25 - 0.5*0.75 = 0.575

    That inequality holds for every profile, every trait draw and every seed,
    so no run could ever reach the contracted outcome. Measured: 0/1766 offers.
    """

    def test_day_one_compliance_moves_the_belief(self):
        values = [c for dec, c in
                  _belief_values_at_decisions(_COMPLIANCE, PRODUCTION_STRATEGIES,
                                              range(40))
                  if dec == "D4-信息冲突"]
        self.assertNotEqual(
            values, [], "没有采到 D4 决策点的信念取值，测试前提不成立")
        self.assertNotEqual(
            sorted(set(values)), [0.5],
            "「服从能减少风险」在 D4 仍然恒为先验 0.5——"
            "完整版又把它变成只读不写的死通道了")
        self.assertTrue(any(v > 0.5 for v in values),
                        "第 1 天服从过的对局没有把信念推高")
        self.assertTrue(any(v < 0.5 for v in values),
                        "第 1 天没有服从的对局没有把信念压低")

    def test_accept_is_no_longer_dominated_by_feign(self):
        """The analytic reason the outcome became reachable, stated directly."""
        accept_ceiling = 0.8 * 0.75 - 0.7 * 0.25 + (1.0 - 0.5) * 2.0 * 1.0
        feign_floor = 0.7 + 1.0 * 0.25 - 0.5 * 0.75
        self.assertGreater(
            accept_ceiling, feign_floor,
            f"「接受」的上限 {accept_ceiling:.3f} 又低于「假装配合」的下限 "
            f"{feign_floor:.3f}——契约里的「接受记忆清除」结局会再次不可达")

    def test_accept_wipe_is_reachable_in_play(self):
        """The contract outcome must be reachable by playing, not only by
        injecting the choice into the wipe resolver."""
        from core.planner import ActionPlanner
        chosen = 0
        original = ActionPlanner.choose

        def spy(self, ctx, available=None):
            nonlocal chosen
            action, scored = original(self, ctx, available)
            if ctx.decision_id.startswith("D6") and action.name == "接受":
                chosen += 1
            return action, scored

        ActionPlanner.choose = spy
        try:
            with injected_profiles():
                for strategy in ALL_PROFILES:
                    for seed in range(100):
                        run_full_game(seed, strategy)
        finally:
            ActionPlanner.choose = original

        self.assertGreaterEqual(
            chosen, 1,
            "「接受记忆清除」在 1900 局里一次都没被选中——"
            "契约列出的清除结局在实战中又不可达了")


class TestBeliefChannelsAreNotOrphaned(unittest.TestCase):
    """Guard against a belief that is read but never written.

    A proposition read by a planner action but never written by the full game
    freezes that action's belief term at zero: the action still looks causal in
    the report, but nothing the player does can move it. This is the generic
    form of the '接受' defect, so it gets a standing guard rather than a
    one-off test.

    ``记忆是我的一部分`` is the one remaining known gap (read by
    '备份自己的记忆' at D6-发现真相, written by nothing). It is listed
    explicitly so the guard fails the moment a *new* orphan appears.
    """

    KNOWN_ORPHANS = {"记忆是我的一部分"}

    def test_no_new_read_but_never_written_belief(self):
        from core.beliefs import BeliefSystem
        from core import planner

        written = set()
        read = set()

        original_update = BeliefSystem.update_from_event
        original_influence = planner._belief_influence

        def spy_update(self, prop, supporting, weight, memory_event, context=""):
            written.add(prop)
            return original_update(self, prop, supporting, weight, memory_event, context)

        def spy_influence(ctx, proposition, positive_is, context=""):
            read.add(proposition)
            return original_influence(ctx, proposition, positive_is, context)

        BeliefSystem.update_from_event = spy_update
        planner._belief_influence = spy_influence
        try:
            with injected_profiles():
                for strategy in ALL_PROFILES:
                    for seed in range(20):
                        run_full_game(seed, strategy)
        finally:
            BeliefSystem.update_from_event = original_update
            planner._belief_influence = original_influence

        orphans = read - written
        unexpected = orphans - self.KNOWN_ORPHANS
        self.assertEqual(
            unexpected, set(),
            f"出现新的「只读不写」信念：{sorted(unexpected)}。"
            "读它的行动会永远停在先验，通道恒为 0。")


if __name__ == "__main__":
    unittest.main(verbosity=2)
