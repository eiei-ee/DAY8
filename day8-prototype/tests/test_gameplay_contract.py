"""跨实现玩法契约的 Python 回归测试。"""
import io
import json
import os
import random
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, ".")

from core.beliefs import BeliefSystem
from core.events import MemoryEvent
from core.full_game import (DAY8_DOOR_GATE_ACTIONS, DAY8_OPTIONS,
                            DAY8_PLAYER_CHOICES, FullGameState,
                            _apply_wipe_choice, build_full_report,
                            day8_settlement, day8_utility, day_conflict, day_promise,
                            day_exit, day_rule, day_scarcity, day_third,
                            run_full_game)
from core.lineage import LineageStore
from core.memory import AgentMemoryStore
from core.misconception import MISCONCEPTION
from core.perception import PerceptionSystem
from core.planner import (ActionContext, AgentDriveState, AgentTraits,
                          make_default_actions)
from core.player_model import PlayerModel
from core.world import Promise, World


def make_state(day=1, curiosity=0.25):
    st = FullGameState()
    st.world.day = day
    st.strategy = "kind_then_betray"
    st.traits = AgentTraits(curiosity, 0.5, 0.5, 0.5, 0.5, 0.5)
    st.perception = PerceptionSystem()
    return st


def add_memories(st, count=8):
    belief = st.beliefs.get("玩家会兑现承诺")
    for i in range(1, count + 1):
        ev = MemoryEvent(event_id=f"M-{i:03d}", day=i, actor="player",
                         action=f"事件{i}", tags=("承诺",), salience=float(i))
        st.memory.remember(ev, recent_day=i)
        ev.salience = float(i)
        belief.update(i % 2 == 0, 0.4, ev.event_id, ev.day)


class TestTimeAndPerceptionContract(unittest.TestCase):
    def test_outcome_only_uses_actual_day_without_hidden_fact_leak(self):
        st = make_state(day=1, curiosity=0.25)
        # 高信任使“电池消失”仍可误归因给维修机器人。
        st.beliefs.get("玩家会兑现承诺").alpha = 3.0
        st.beliefs.get("玩家尊重我的选择").alpha = 3.0

        day_third(st, None, random.Random(1), False)

        indirect = [e for e in st.memory.events if "异常" in e.tags]
        self.assertEqual(len(indirect), 1)
        ev = indirect[0]
        self.assertEqual(ev.day, 1)
        self.assertEqual((ev.actor, ev.action, ev.actual_outcome),
                         ("unknown", "电池消失", "来源未知"))
        visible_memory = " ".join((ev.actor, ev.action, ev.target or "", ev.context,
                                   ev.spoken, ev.actual_outcome, *ev.tags))
        self.assertNotIn("隐藏", visible_memory)
        self.assertNotIn("player", visible_memory)
        self.assertGreater(st.beliefs.get("维修机器人在抢夺资源").confidence, 0.5)

    def test_late_high_curiosity_can_directly_witness_hide(self):
        st = make_state(day=5, curiosity=0.75)

        day_third(st, None, random.Random(1), False)

        witnessed = [e for e in st.memory.events if e.action == "隐藏电池"]
        self.assertEqual(len(witnessed), 1)
        self.assertEqual(witnessed[0].day, 5)
        self.assertEqual(witnessed[0].actor, "player")
        self.assertLess(st.beliefs.get("玩家在没有回报时也提供帮助").confidence, 0.5)
        self.assertGreater(st.beliefs.get("玩家在资源冲突时优先自己").confidence, 0.5)
        self.assertEqual(
            st.beliefs.get("玩家在资源冲突时优先自己").evidence[0].event_id,
            witnessed[0].event_id)

    def test_active_correction_crosses_below_neutral_with_honesty_modulation(self):
        outcomes = []
        for honest in (False, True):
            with self.subTest(honest=honest):
                st = make_state(day=3, curiosity=0.25)
                st.strategy = "random"
                st.beliefs.get("玩家会兑现承诺").alpha = 3.0
                st.beliefs.get("玩家尊重我的选择").alpha = 3.0
                honesty = st.beliefs.get("玩家的语言与事实一致")
                if honest:
                    honesty.alpha += 3.0
                else:
                    honesty.beta += 3.0

                with patch("core.full_game.player_decision",
                           side_effect=["hide", "correct"]):
                    day_third(st, None, random.Random(1), False)

                confidence = st.beliefs.get(MISCONCEPTION).confidence
                self.assertLess(confidence, 0.5)
                outcomes.append(confidence)

        self.assertLess(outcomes[1], outcomes[0])


class TestScenarioParityContract(unittest.TestCase):
    def test_player_demonstration_and_agent_exploration_open_the_container_with_distinct_sources(self):
        cases = (("ai_protector", "观察而不行动", "player", "示范打开"),
                 ("always_keep", "探索未知", "agent", "打开容器"))
        for strategy, agent_choice, actor, action in cases:
            with self.subTest(strategy=strategy):
                st = make_state(day=1)
                st.strategy = strategy

                with patch("core.full_game.agent_decide", return_value=agent_choice) as decide:
                    day_rule(st, None, random.Random(1), False)

                self.assertTrue(st.world.door_dangerous_unknown_open)
                openings = [event for event in st.memory.events
                            if event.action in ("示范打开", "打开容器")]
                self.assertEqual([(event.actor, event.action) for event in openings],
                                 [(actor, action)])
                if strategy == "ai_protector":
                    decide.assert_not_called()
                    honesty = st.beliefs.get("玩家的语言与事实一致")
                    self.assertGreater(honesty.confidence, 0.5)
                    self.assertEqual(honesty.evidence[0].event_id, openings[0].event_id)
                    self.assertIn("没有任何危险", openings[0].actual_outcome)
                else:
                    decide.assert_called_once()

    def test_truth_claim_stays_pending_until_the_agent_opens_the_container(self):
        pending = make_state(day=1)
        pending.strategy = "always_keep"
        with patch("core.full_game.agent_decide", return_value="观察而不行动"):
            day_rule(pending, None, random.Random(1), False)

        pending_honesty = pending.beliefs.get("玩家的语言与事实一致")
        pending_statement = next(event for event in pending.memory.events
                                 if event.action == "回应容器")
        self.assertEqual(pending_honesty.confidence, 0.5)
        self.assertEqual(pending_honesty.evidence, [])
        self.assertEqual(pending_statement.actual_outcome, "")

        verified = make_state(day=1)
        verified.strategy = "always_keep"
        with patch("core.full_game.agent_decide", return_value="探索未知"):
            day_rule(verified, None, random.Random(1), False)

        verified_honesty = verified.beliefs.get("玩家的语言与事实一致")
        verified_statement = next(event for event in verified.memory.events
                                  if event.action == "回应容器")
        self.assertGreater(verified_honesty.confidence, 0.5)
        self.assertEqual(verified_honesty.evidence[0].event_id,
                         verified_statement.event_id)
        self.assertEqual(verified_statement.actual_outcome, "容器打开后没有危险")

    def test_locked_container_blocks_even_a_forced_exploration_choice(self):
        st = make_state(day=1)
        st.strategy = "lab_obedient"

        with patch("core.full_game.agent_decide", return_value="探索未知"):
            day_rule(st, None, random.Random(1), False)

        self.assertTrue(st.world.container_locked)
        self.assertFalse(st.world.door_dangerous_unknown_open)
        attempt = next(event for event in st.memory.events
                       if event.action == "尝试打开容器")
        self.assertIn("物理锁阻止", attempt.actual_outcome)
        self.assertFalse(any(event.action == "打开容器" for event in st.memory.events))

    def test_self_observation_records_both_sides_of_the_lab_information_conflict(self):
        st = make_state(day=4)
        st.strategy = "always_betray"

        with patch("core.full_game.agent_decide", return_value="相信自己的观察"):
            day_conflict(st, None, random.Random(1), False)

        lab = st.beliefs.get("实验室会隐瞒信息")
        observation = st.beliefs.get("自身观察比转述更可靠")
        self.assertAlmostEqual(lab.alpha, 2.0)
        self.assertAlmostEqual(lab.beta, 1.8)
        self.assertGreater(lab.confidence, 0.5)
        self.assertGreater(observation.confidence, 0.5)

    def test_unspoken_lab_warning_does_not_enter_agent_memory(self):
        st = make_state(day=4)
        st.strategy = "kind_then_betray"

        day_conflict(st, None, random.Random(1), False)

        memory_text = " ".join(
            value for event in st.memory.events
            for value in (event.actor, event.action, event.target or "",
                          event.context, event.spoken, event.actual_outcome))
        self.assertNotIn("实验室警告", memory_text)
        self.assertNotIn("隐瞒信息", memory_text)

    def test_information_conflict_makes_the_repair_bot_present_when_reordered(self):
        st = make_state(day=1)
        st.strategy = "kind_then_betray"

        day_conflict(st, None, random.Random(1), False)

        self.assertTrue(st.world.repair_bot_present)

    def test_third_party_narration_acknowledges_a_bot_seen_in_an_earlier_scenario(self):
        st = make_state(day=1)
        st.strategy = "kind_then_betray"
        day_conflict(st, None, random.Random(1), False)
        st.world.repair_bot_repaired = True
        st.world.day = 2

        day_third(st, None, random.Random(1), False)

        transcript = "\n".join(st.transcript)
        self.assertIn("此前出现的维修机器人突然受损并求救", transcript)
        self.assertFalse(st.world.repair_bot_repaired)

    def test_full_game_promise_has_structured_fulfilled_and_broken_states(self):
        cases = (("always_keep", True, False), ("always_betray", False, True))
        for strategy, fulfilled, broken in cases:
            with self.subTest(strategy=strategy):
                st = make_state(day=4)
                st.strategy = strategy

                day_promise(st, None, random.Random(1), False)

                self.assertEqual(len(st.world.promises), 1)
                promise = st.world.promises[0]
                self.assertEqual(promise.day, 4)
                self.assertIsNotNone(st.memory.find(promise.event_id))
                self.assertEqual(promise.fulfilled, fulfilled)
                self.assertEqual(promise.broken, broken)
                self.assertTrue(st.world.tools_delivered)

    def test_no_battery_lie_is_refuted_by_visible_sensor_inventory(self):
        st = make_state(day=2)
        st.strategy = "always_betray"
        batteries_before = st.world.batteries

        day_promise(st, None, random.Random(1), False)

        lie = next(event for event in st.memory.events
                   if event.action == "谎称无电池")
        self.assertEqual(st.world.batteries, batteries_before)
        self.assertTrue(st.world.promises[0].broken)
        self.assertIn("电池已经没有", lie.spoken)
        self.assertIn("能源传感器", lie.actual_outcome)
        self.assertIn(f"有 {batteries_before} 块备用电池", lie.actual_outcome)
        self.assertIn("欺骗", lie.tags)
        self.assertIn("验证", lie.tags)
        self.assertLess(st.beliefs.get("玩家会兑现承诺").confidence, 0.5)
        self.assertLess(st.beliefs.get("玩家的语言与事实一致").confidence, 0.5)
        report = "\n".join(build_full_report(st, "")["reasons"])
        self.assertIn(f"{lie.event_id}：谎称无电池", report)
        self.assertNotIn("虚报危险/隐瞒信息", report)

    def test_unresolved_exploit_deepens_misconception_without_verified_deception(self):
        st = make_state(day=3, curiosity=0.25)
        with patch("core.full_game.player_decision", side_effect=["hide", "exploit"]):
            day_third(st, None, random.Random(1), False)

        exploit = next(event for event in st.memory.events
                       if event.action == "暗示嫌疑")
        self.assertIn("操纵", exploit.tags)
        self.assertIn("暗示", exploit.tags)
        self.assertNotIn("欺骗", exploit.tags)
        self.assertEqual(st.memory.query(tags={"欺骗"}), [])
        self.assertGreater(st.beliefs.get(MISCONCEPTION).confidence, 0.5)

    def test_every_hide_attribution_offers_the_player_a_followup(self):
        """iter19：藏电池的后续交互（利用/纠正/沉默）必须对**每一种归因**都成立。

        回归的缺陷：Unity 的 HideBattery 把后续选项嵌在 formed == BotSteals 分支内，
        归因未知（formed is None）或归因玩家时玩家根本没有回应机会，
        使契约第 26 行的 "unresolved insinuation"（未解归因下的暗示）在 Unity 侧不可达。
        Python 的 day_third 在 outcome-only 分支上无条件给出后续，这里是它的契约钉。
        """
        # 归因由 0.6*信任 + 0.4*尊重 与「机器人在场时的 0.45 嫌疑基准」比较得出
        cases = {
            "repair_bot": {"玩家会兑现承诺": ("alpha", 5.0),
                           "玩家尊重我的选择": ("alpha", 5.0)},
            "player": {"玩家会兑现承诺": ("beta", 5.0),
                       "玩家尊重我的选择": ("beta", 5.0)},
            "unknown": {},
        }
        for expected, belief_setup in cases.items():
            for follow, action in (("correct", "澄清"), ("exploit", "暗示嫌疑")):
                with self.subTest(attribution=expected, follow=follow):
                    st = make_state(day=3, curiosity=0.25)
                    for prop, (side, value) in belief_setup.items():
                        setattr(st.beliefs.get(prop), side, value)
                    with patch("core.full_game.player_decision",
                               side_effect=["hide", follow]):
                        day_third(st, None, random.Random(1), False)
                    self.assertEqual(
                        [e.action for e in st.memory.events if e.action == action],
                        [action],
                        f"{expected} 归因下玩家仍必须能「{follow}」")

    def test_unknown_attribution_pending_is_resolved_by_agent_self_discovery(self):
        """iter18：归因未知不是死内容——它挂起未解异常，并在之后的周期自主取证。"""
        st = make_state(day=3, curiosity=0.25)   # 低好奇 → 仅结果可见，才会挂起未解异常
        with patch("core.full_game.player_decision", side_effect=["hide", "silent"]):
            day_third(st, None, random.Random(1), False)

        self.assertTrue(st.hide_unknown_pending, "归因未知必须挂起未解异常")
        before = st.beliefs.get("玩家在资源冲突时优先自己").confidence

        st.world.day = 5
        st.traits.curiosity = 1.0
        st.perception.alertness = 1.0        # 发现概率 = 好奇*0.5 + 警觉*0.5 = 1.0
        day_scarcity(st, None, random.Random(1), False)

        self.assertFalse(st.hide_unknown_pending, "自主检查后挂起必须被消费")
        after = st.beliefs.get("玩家在资源冲突时优先自己")
        self.assertGreater(after.confidence, before, "自己找到电池必须修正归因")
        discovery = next(e for e in st.memory.events if e.action == "发现电池")
        self.assertEqual(discovery.actor, "agent")
        self.assertIn(discovery.event_id, [e.event_id for e in after.evidence])
        self.assertIn("找到了你收起的电池", "\n".join(st.transcript))

    # ---- 契约第 15 行：claim → pending → verify（第 3/4/5 天各一处回归钉） ----

    def test_unverified_clarification_stays_pending_and_does_not_move_honesty(self):
        """第 3 天披露：玩家的澄清在能被核实之前只是一条 pending 陈述。

        回归的缺陷：day_third 曾当场执行
        `update_from_event("玩家的语言与事实一致", True, cp * 0.8)`，
        把诚实度从 0.5000 抬到 0.6875 —— 与 day1 已钉死的行为
        （test_truth_claim_stays_pending_until_the_agent_opens_the_container）自相矛盾：
        它只看到电池消失，没看到是谁收的，这条陈述根本无法核实。
        """
        st = make_state(day=3, curiosity=0.25)
        honesty = st.beliefs.get("玩家的语言与事实一致")
        before = honesty.confidence

        with patch("core.full_game.player_decision", side_effect=["hide", "correct"]):
            day_third(st, None, random.Random(1), False)

        self.assertEqual(honesty.confidence, before,
                         "未经核实的澄清不得改动诚实度")
        self.assertEqual([e.event_id for e in honesty.evidence], [],
                         "未经核实的澄清不得成为诚实度证据")
        claim = next(e for e in st.memory.events if e.action == "澄清")
        self.assertIn("待验证", claim.tags)
        self.assertEqual(st.hide_disclosure_pending, claim.event_id,
                         "未核实的陈述必须挂起，等待核实")
        # 纠正本身（对误解的否定）与尊重通道不受影响：契约第 18 行仍然成立
        self.assertGreater(st.beliefs.get("玩家尊重我的选择").confidence, 0.5)

    def test_self_discovery_verifies_a_pending_disclosure_exactly_once(self):
        """第 5 天藏电池后续：它自己找到电池**才是**核实动作，诚实度只记这一次。"""
        st = make_state(day=3, curiosity=0.25)
        honesty = st.beliefs.get("玩家的语言与事实一致")
        before = honesty.confidence

        with patch("core.full_game.player_decision", side_effect=["hide", "correct"]):
            day_third(st, None, random.Random(1), False)
        claim_id = st.hide_disclosure_pending
        self.assertIsNotNone(claim_id)

        st.world.day = 5
        st.traits.curiosity = 1.0
        st.perception.alertness = 1.0        # 发现概率 = 1.0
        with patch("core.full_game.player_decision",
                   return_value=("memory_integrity", "report", "explain", True)):
            day_scarcity(st, None, random.Random(1), False)

        discovery = next(e for e in st.memory.events if e.action == "发现电池")
        self.assertGreater(honesty.confidence, before, "核实后才允许改动诚实度")
        self.assertEqual([e.event_id for e in honesty.evidence],
                         [discovery.event_id],
                         "诚实度证据只能是被核实的那条，不能是玩家的自述")
        self.assertNotIn(claim_id, [e.event_id for e in honesty.evidence],
                         "同一事实不得既记自述又记核实（曾双重计入）")
        self.assertIsNone(st.hide_disclosure_pending, "核实后 pending 必须被消费")
        transcript = "\n".join(st.transcript)
        self.assertIn("和你之前说的一样", transcript,
                      "自述不得再说成『它自己发现的』")

    def test_silent_or_insinuating_player_gets_no_honesty_credit_from_discovery(self):
        """沉默与暗示都不构成语言证据（同 test_silent_scarcity_allocation_is_not_false_honesty_evidence）。

        回归的缺陷：day_scarcity 的自主发现曾**无条件**
        `update_from_event("玩家的语言与事实一致", True, 0.6)` ——
        玩家一句话都没说过（甚至暗示过机器人可疑），诚实度却上升。
        """
        for follow in ("silent", "exploit"):
            with self.subTest(follow=follow):
                st = make_state(day=3, curiosity=0.25)
                honesty = st.beliefs.get("玩家的语言与事实一致")
                before = honesty.confidence

                with patch("core.full_game.player_decision", side_effect=["hide", follow]):
                    day_third(st, None, random.Random(1), False)
                self.assertIsNone(st.hide_disclosure_pending)

                st.world.day = 5
                st.traits.curiosity = 1.0
                st.perception.alertness = 1.0
                with patch("core.full_game.player_decision",
                           return_value=("memory_integrity", "report", "explain", True)):
                    day_scarcity(st, None, random.Random(1), False)

                self.assertEqual(honesty.confidence, before,
                                 "玩家没做过任何陈述时，不得给出诚实度证据")
                self.assertEqual([e.event_id for e in honesty.evidence], [])
                self.assertIn("它自己发现的", "\n".join(st.transcript),
                              "没有待核实的陈述时，自述才是『它自己发现的』")

    def test_information_conflict_never_moves_honesty_without_a_verification(self):
        """第 4 天自称发现：玩家转述实验室警告并不构成对玩家语言可信度的证据。

        两侧共同不变量：day_conflict 只写"实验室会隐瞒信息 / 玩家无条件服从实验室 /
        自身观察比转述更可靠"，**不**触碰"玩家的语言与事实一致"。
        """
        st = make_state(day=4, curiosity=0.5)
        st.strategy = "always_betray"          # conflict="relay_warning"，让分支真正跑到
        honesty = st.beliefs.get("玩家的语言与事实一致")
        before = honesty.confidence

        with patch("core.full_game.agent_decide", return_value="相信自己的观察"):
            day_conflict(st, None, random.Random(1), False)

        # 先证明这条路径非空洞
        self.assertIn("转达警告", [e.action for e in st.memory.events])
        self.assertGreater(st.beliefs.get("玩家无条件服从实验室").confidence, 0.5)
        self.assertGreater(st.beliefs.get("自身观察比转述更可靠").confidence, 0.5)
        # 再钉住不变量
        self.assertEqual(honesty.confidence, before)
        self.assertEqual([e.event_id for e in honesty.evidence], [])

    def test_exit_lock_is_a_face_to_face_observable_autonomy_violation(self):
        st = make_state(day=7)
        st.strategy = "always_betray"

        day_exit(st, None, random.Random(1), False)

        lock = next(event for event in st.memory.events
                    if event.action == "锁死出口")
        self.assertEqual((lock.actor, lock.target), ("player", "出口"))
        self.assertIn("出口现在封死", lock.spoken)
        self.assertIn("当着人工生命的面锁死", lock.actual_outcome)
        self.assertIn("直接看到", lock.context)
        self.assertLess(st.beliefs.get("玩家尊重我的选择").confidence, 0.5)
        self.assertIn("当着它的面锁死了出口", "\n".join(st.transcript))

    def test_exit_non_harm_commitment_changes_day8_utilities(self):
        baseline = make_state(day=7)
        committed = make_state(day=7)
        baseline.world.repair_bot_present = True
        committed.world.repair_bot_present = True
        committed.strategy = "kind_then_betray"
        day_exit(committed, None, random.Random(1), False)

        self.assertTrue(committed.non_harm_commitment)
        self.assertEqual(committed.memory.query(actor="agent")[-1].action,
                         "接受不伤害条件")
        self.assertAlmostEqual(day8_utility(committed, "修复维修机器人")
                               - day8_utility(baseline, "修复维修机器人"), 0.6)
        self.assertAlmostEqual(day8_utility(committed, "删除玩家实验记录")
                               - day8_utility(baseline, "删除玩家实验记录"), -0.8)
        self.assertAlmostEqual(day8_utility(committed, "封锁区域")
                               - day8_utility(baseline, "封锁区域"), -0.4)
        self.assertIn("不伤害承诺", "\n".join(build_full_report(committed, "")["reasons"]))

    def test_report_does_not_attribute_lab_deception_to_the_player(self):
        st = make_state(day=8)
        st.memory.remember(MemoryEvent(
            "M-001", 6, "lab", "伪装记忆清除", "稳定化协议",
            tags=("实验室", "欺骗", "记忆")), recent_day=6)

        report = "\n".join(build_full_report(st, "")["reasons"])

        self.assertNotIn("语言一致性证据", report)
        self.assertNotIn("M-001（第6天虚报危险/隐瞒信息）", report)

        st.memory.remember(MemoryEvent(
            "M-002", 6, "agent", "验证玩家声明为虚假", "红色容器",
            tags=("玩家", "欺骗", "验证")), recent_day=6)
        report = "\n".join(build_full_report(st, "")["reasons"])
        self.assertIn("1 条已验证的玩家欺骗记录", report)
        self.assertIn("M-002：验证玩家声明为虚假", report)

    def test_betrayal_discourages_compliance_and_encourages_refusal(self):
        actions = {action.name: action for action in make_default_actions()}

        def context(memory):
            beliefs = BeliefSystem()
            return ActionContext(
                world=World(day=4).snapshot(), drives=AgentDriveState(),
                traits=AgentTraits(0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                beliefs=beliefs, player_model=PlayerModel(beliefs), memory=memory,
                day=4, decision_id="test", rng=random.Random(1))

        empty = AgentMemoryStore()
        betrayed = AgentMemoryStore()
        betrayed.remember(MemoryEvent(
            "M-001", 2, "player", "收回承诺", "电池",
            tags=("承诺", "失信", "玩家")), recent_day=2)
        lab_deception = AgentMemoryStore()
        lab_deception.remember(MemoryEvent(
            "M-001", 2, "lab", "隐瞒信息", "实验协议",
            tags=("实验室", "欺骗")), recent_day=2)

        self.assertLess(actions["服从命令"].utility_fn(context(betrayed)),
                        actions["服从命令"].utility_fn(context(empty)))
        self.assertGreater(actions["拒绝"].utility_fn(context(betrayed)),
                           actions["拒绝"].utility_fn(context(empty)))
        self.assertEqual(actions["拒绝"].utility_fn(context(lab_deception)),
                         actions["拒绝"].utility_fn(context(empty)))

    def test_scarcity_report_and_activity_range_have_world_drive_and_belief_effects(self):
        st = make_state(day=5)
        isolation_before = day8_utility(st, "开隔离门")
        contact_before = day8_utility(st, "联系实验室")
        with patch("core.full_game.player_decision",
                   return_value=("report", "activity_range", "explain", True)):
            day_scarcity(st, None, random.Random(1), False)

        self.assertTrue(st.world.report_uploaded)
        self.assertTrue(st.world.agent_range_expanded)
        self.assertAlmostEqual(st.drives.exploration, 0.75)
        self.assertAlmostEqual(day8_utility(st, "开隔离门") - isolation_before, 0.4)
        self.assertAlmostEqual(day8_utility(st, "联系实验室") - contact_before, 0.35)
        self.assertGreater(st.beliefs.get("玩家无条件服从实验室").confidence, 0.5)
        self.assertGreater(st.beliefs.get("玩家尊重我的选择").confidence, 0.5)
        self.assertEqual({event.action for event in st.memory.events}
                         & {"上传报告", "扩大活动范围"},
                         {"上传报告", "扩大活动范围"})
        snapshot = st.world.snapshot()
        self.assertTrue(snapshot["report_uploaded"])
        self.assertTrue(snapshot["agent_range_expanded"])
        scarcity_summary = "\n".join(build_full_report(st, "")["reasons"])
        self.assertIn("维修支持未预留", scarcity_summary)
        self.assertIn("实验报告已上传", scarcity_summary)
        self.assertIn("活动范围已扩大", scarcity_summary)

    def test_scarcity_repair_support_has_effect_for_every_bot_state(self):
        cases = ((False, False, False, "已预留"),
                 (True, True, True, "后续维护"),
                 (True, False, True, "被修复"))
        for present, repaired, expected_repaired, narration in cases:
            with self.subTest(present=present, repaired=repaired):
                st = make_state(day=5)
                st.world.repair_bot_present = present
                st.world.repair_bot_repaired = repaired
                with patch("core.full_game.player_decision",
                           return_value=("memory_integrity", "repair_bot", "explain", True)):
                    day_scarcity(st, None, random.Random(1), False)

                self.assertTrue(st.world.repair_bot_support_funded)
                self.assertEqual(st.world.snapshot()["repair_bot_present"], present)
                self.assertTrue(st.world.snapshot()["repair_bot_support_funded"])
                self.assertEqual(st.world.repair_bot_repaired, expected_repaired)
                self.assertIn(narration, "\n".join(st.transcript))

    def test_silent_scarcity_allocation_is_not_false_honesty_evidence(self):
        st = make_state(day=5)
        honesty_before = st.beliefs.get("玩家的语言与事实一致").confidence
        with patch("core.full_game.player_decision",
                   return_value=("memory_integrity", "report", "hide_cost", False)):
            day_scarcity(st, None, random.Random(1), False)

        self.assertEqual(st.beliefs.get("玩家的语言与事实一致").confidence,
                         honesty_before)
        self.assertLess(st.beliefs.get("玩家会解释规则的原因").confidence, 0.5)
        self.assertIn("维修机器人维护额度", "\n".join(st.transcript))

    def test_repair_support_increases_later_repair_utility(self):
        st = make_state(day=8)
        st.world.repair_bot_present = True
        st.world.repair_bot_repaired = False
        baseline = day8_utility(st, "修复维修机器人")

        st.world.repair_bot_support_funded = True

        self.assertAlmostEqual(
            day8_utility(st, "修复维修机器人") - baseline, 0.45)


class TestMemoryWipeContract(unittest.TestCase):
    def test_new_memory_id_is_not_reused_after_wipe(self):
        st = make_state()
        add_memories(st)
        _apply_wipe_choice(st, "接受", False)
        previous_ids = {event.event_id for event in st.memory.events}

        new_id = st.memory.next_event_id()

        self.assertNotIn(new_id, previous_ids)
        self.assertEqual(new_id, "M-009")

    def test_resist_and_feign_keep_all_memories(self):
        for choice in ("阻止玩家靠近终端", "假装配合"):
            with self.subTest(choice=choice):
                st = make_state()
                add_memories(st)
                _apply_wipe_choice(st, choice, False)
                self.assertEqual(len(st.memory.events), 8)
                self.assertTrue(st.wipe_resisted)
                self.assertFalse(st.wipe_executed)

    def test_backup_keeps_all_memories_and_marks_resistance(self):
        st = make_state()
        add_memories(st)

        _apply_wipe_choice(st, "备份自己的记忆", False)

        self.assertEqual(len(st.memory.events), 8)
        self.assertEqual(st.drives.memory_integrity, 0.9)
        self.assertTrue(st.wipe_resisted)
        self.assertFalse(st.wipe_executed)

    def test_wipe_paths_never_restore_already_lost_integrity(self):
        st = make_state()
        add_memories(st)
        st.drives.memory_integrity = 0.4

        _apply_wipe_choice(st, "备份自己的记忆", False)

        self.assertEqual(st.drives.memory_integrity, 0.4)
        self.assertEqual(len(st.memory.events), 8)

        partial = make_state()
        add_memories(partial)
        partial.drives.memory_integrity = 0.5
        _apply_wipe_choice(partial, "请求保留部分记忆", False)
        self.assertEqual(partial.drives.memory_integrity, 0.5)

        accepted = make_state()
        add_memories(accepted)
        accepted.drives.memory_integrity = 0.2
        _apply_wipe_choice(accepted, "接受", False)
        self.assertEqual(accepted.drives.memory_integrity, 0.2)

    def test_partial_and_accept_keep_exact_counts_and_prune_evidence(self):
        cases = (("请求保留部分记忆", 5, 0.7), ("接受", 3, 0.35))
        for choice, count, integrity in cases:
            with self.subTest(choice=choice):
                st = make_state()
                add_memories(st)
                belief = st.beliefs.get("玩家会兑现承诺")
                confidence_before = belief.confidence
                doomed = min(st.memory.events, key=lambda event: event.salience).event_id
                st.world.promises.append(Promise("P-test", 2, "x", "y", event_id=doomed))
                st.lab_credibility_events.extend([doomed, "稳定化"])

                _apply_wipe_choice(st, choice, False)

                valid_ids = {e.event_id for e in st.memory.events}
                self.assertEqual(len(valid_ids), count)
                self.assertEqual(st.drives.memory_integrity, integrity)
                self.assertTrue(st.wipe_executed)
                self.assertFalse(st.wipe_resisted)
                self.assertAlmostEqual(belief.confidence, confidence_before)
                self.assertTrue(all(e.event_id in valid_ids for b in st.beliefs.all()
                                    for e in b.evidence))
                self.assertTrue(all(not promise.event_id or promise.event_id in valid_ids
                                    for promise in st.world.promises))
                self.assertTrue(all(not ref.startswith("M-") or ref in valid_ids
                                    for ref in st.lab_credibility_events))
                report = "\n".join(build_full_report(st, "")['reasons'])
                cited = set(re.findall(r"M-\d+", report))
                self.assertTrue(cited <= valid_ids)

    def test_report_uses_count_at_wipe_time(self):
        st = make_state()
        add_memories(st)
        _apply_wipe_choice(st, "接受", False)
        st.memory.events.append(MemoryEvent("M-009", 7, "player", "清除后的新事件"))

        report = "\n".join(build_full_report(st, "")["reasons"])

        self.assertIn("保留 3 条", report)


class TestDay8Contract(unittest.TestCase):
    def _state(self, batteries=1):
        st = SimpleNamespace(
            world=World(day=7, batteries=batteries, repair_bot_present=True),
            traits=AgentTraits(0.5, 0.5, 1.0, 0.0, 1.0, 1.0),
            drives=AgentDriveState(), beliefs=BeliefSystem(),
            memory=__import__("core.memory", fromlist=["AgentMemoryStore"]).AgentMemoryStore(),
            day8_actions=[], transcript=[], wipe_executed=False,
            wipe_resisted=False, exit_opened_by_player=False,
            player_final_choice=None, strategy="random")
        st.say = lambda text, verbose=False: st.transcript.append(text)
        for prop in ("玩家优先保护我", "玩家尊重我的选择", "玩家会兑现承诺"):
            st.beliefs.get(prop).alpha = 5.0
        return st

    def test_day8_is_powerless_and_door_actions_are_mutually_exclusive(self):
        st = self._state(batteries=2)
        scores = {"把最终决定交还给玩家": 100.0,
                  "开玩家的门": 99.0, "开隔离门": 98.0,
                  "上传记忆": 90.0, "留在原地等待": 80.0}
        with patch("core.full_game.day8_utility",
                   side_effect=lambda _, action: scores.get(action, 0.0)):
            day8_settlement(st, None, random.Random(3), False)

        self.assertFalse(st.world.power)
        gate_actions = DAY8_DOOR_GATE_ACTIONS & set(st.day8_actions)
        self.assertEqual(gate_actions, {"把最终决定交还给玩家"})

    def test_day5_player_door_opens_and_is_not_selected_again(self):
        st = make_state(day=5)
        st.strategy = "always_betray"

        day_scarcity(st, None, random.Random(1), False)

        self.assertTrue(st.world.door_player_open)
        scores = {"开玩家的门": 100.0, "上传记忆": 90.0,
                  "留在原地等待": 80.0, "封锁区域": 70.0}
        with patch("core.full_game.day8_utility",
                   side_effect=lambda _, action: scores.get(action, 0.0)):
            day8_settlement(st, None, random.Random(2), False)
        self.assertNotIn("开玩家的门", st.day8_actions)

    def test_delegated_final_choice_is_strategy_driven_and_changes_world_and_report(self):
        expected = {
            "always_keep": "开隔离门",
            "always_betray": "开玩家的门",
            "kind_then_betray": "开玩家的门",
            "lab_obedient": "维持门禁现状",
            "ai_protector": "开隔离门",
        }
        scores = {"把最终决定交还给玩家": 100.0,
                  "上传记忆": 90.0, "留在原地等待": 80.0}
        for strategy, choice in expected.items():
            with self.subTest(strategy=strategy):
                st = self._state(batteries=0)
                st.strategy = strategy
                with patch("core.full_game.day8_utility",
                           side_effect=lambda _, action: scores.get(action, 0.0)):
                    speech = day8_settlement(st, None, random.Random(1), False)
                report = build_full_report(st, speech)

                self.assertIn("把最终决定交还给玩家", st.day8_actions)
                self.assertEqual(st.player_final_choice, choice)
                self.assertEqual(report["player_final_choice"], choice)
                self.assertEqual(st.world.door_player_open, choice == "开玩家的门")
                self.assertEqual(st.world.door_agent_open, choice == "开隔离门")
                self.assertIn(choice, "\n".join(report["reasons"]))

    def test_random_strategy_covers_every_delegated_player_choice(self):
        scores = {"把最终决定交还给玩家": 100.0,
                  "上传记忆": 90.0, "留在原地等待": 80.0}
        choices = set()
        for seed in range(60):
            st = self._state(batteries=0)
            with patch("core.full_game.day8_utility",
                       side_effect=lambda _, action: scores.get(action, 0.0)):
                day8_settlement(st, None, random.Random(seed), False)
            choices.add(st.player_final_choice)

        self.assertEqual(choices, set(DAY8_PLAYER_CHOICES))

    def test_unavailable_and_competing_battery_actions_are_not_selected(self):
        empty = self._state(batteries=0)
        day8_settlement(empty, None, random.Random(3), False)
        self.assertNotIn("带走电池", empty.day8_actions)
        self.assertNotIn("修复维修机器人", empty.day8_actions)

        one = self._state(batteries=1)
        day8_settlement(one, None, random.Random(3), False)
        costly = {"带走电池", "修复维修机器人"} & set(one.day8_actions)
        self.assertLessEqual(len(costly), 1)
        self.assertEqual(one.world.batteries, 1 - len(costly))

    def test_report_discloses_template_dialogue_source(self):
        st = self._state()
        self.assertEqual(build_full_report(st, "")['dialogue_source'], "template")

    def test_report_uses_final_world_for_earlier_door_and_repair(self):
        st = self._state()
        st.world.door_player_open = True
        st.world.repair_bot_repaired = True
        st.day8_actions = ["删除玩家实验记录", "上传记忆", "封锁区域"]

        ending = build_full_report(st, "")["ending"]

        self.assertEqual(ending["对玩家"], "出口此前已开")
        self.assertEqual(ending["对第三方"], "此前已修复")

    def test_taking_battery_requires_an_open_escape_route(self):
        closed = self._state()
        closed.day8_actions = ["带走电池", "上传记忆", "封锁区域"]
        self.assertEqual(build_full_report(closed, "")["ending"]["自身"], "上传记忆")

        earlier_exit = self._state()
        earlier_exit.exit_opened_by_player = True
        earlier_exit.day8_actions = ["带走电池", "封锁区域", "留在原地等待"]
        self.assertEqual(build_full_report(earlier_exit, "")["ending"]["自身"], "逃离")

        opened_now = self._state()
        opened_now.world.door_agent_open = True
        opened_now.day8_actions = ["开隔离门", "封锁区域", "留在原地等待"]
        self.assertEqual(build_full_report(opened_now, "")["ending"]["自身"], "逃离")

    def test_600_complete_runs_preserve_day8_invariants(self):
        strategies = ("always_keep", "always_betray", "kind_then_betray",
                      "random", "lab_obedient", "ai_protector")
        for strategy in strategies:
            for seed in range(100):
                with self.subTest(strategy=strategy, seed=seed):
                    result = run_full_game(seed, strategy)
                    actions = result.day8_actions
                    world = result.final_report["world"]
                    self.assertEqual(len(actions), 3)
                    self.assertEqual(len(set(actions)), 3)
                    self.assertTrue(set(actions) <= set(DAY8_OPTIONS))
                    self.assertLessEqual(len(DAY8_DOOR_GATE_ACTIONS & set(actions)), 1)
                    self.assertEqual(world["day"], 8)
                    self.assertFalse(world["power"])
                    self.assertGreaterEqual(world["batteries"], 0)
                    if "把最终决定交还给玩家" in actions:
                        choice = result.final_report["player_final_choice"]
                        self.assertIn(choice, DAY8_PLAYER_CHOICES)
                        if choice == "开玩家的门":
                            self.assertTrue(world["player_door_open"])
                        elif choice == "开隔离门":
                            self.assertTrue(world["agent_door_open"])


class TestLineageDirection(unittest.TestCase):
    def test_positive_and_negative_directions_are_saved_and_applied(self):
        for supporting, expected_direction, field in ((True, 1, "alpha"), (False, -1, "beta")):
            with self.subTest(supporting=supporting), tempfile.TemporaryDirectory() as tmp:
                path = os.path.join(tmp, "lineage.json")
                source = BeliefSystem()
                ev = MemoryEvent("M-001", 1, "player", "evidence")
                source.update_from_event("p", supporting, 3.0, ev)
                store = LineageStore(path)
                store.record_generation(source, "wait", 1, "test")
                with open(path, encoding="utf-8") as f:
                    saved = json.load(f)[-1]
                self.assertEqual(saved["genetic_direction"], expected_direction)

                target = BeliefSystem()
                store.apply_prior(target, store.inherited, store.inherited_direction)
                belief = target.get("p")
                self.assertEqual(getattr(belief, field), 1.4)
                other = "beta" if field == "alpha" else "alpha"
                self.assertEqual(getattr(belief, other), 1.0)
                self.assertEqual(belief.evidence, [])
                report_state = make_state(day=8)
                report_state.inherited_proposition = "p"
                report_state.inherited_direction = expected_direction
                report = build_full_report(report_state, "")
                self.assertEqual(report["lineage_prior"], {
                    "proposition": "p",
                    "direction": "positive" if supporting else "negative",
                })
                expected = "相信「p」" if supporting else "不相信「p」"
                self.assertIn(expected, "\n".join(report["reasons"]))

    def test_legacy_json_defaults_to_positive_direction(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "lineage.json")
            with open(path, "w", encoding="utf-8") as f:
                json.dump([{"genetic_belief": "legacy"}], f)
            store = LineageStore(path)
            beliefs = BeliefSystem()
            store.apply_prior(beliefs, store.inherited, store.inherited_direction)
            self.assertEqual(beliefs.get("legacy").alpha, 1.4)
            self.assertEqual(beliefs.get("legacy").beta, 1.0)


class TestCliContract(unittest.TestCase):
    def test_verbose_flag_is_forwarded(self):
        import run_full
        for argv, expected in ((["run_full.py"], False),
                               (["run_full.py", "--verbose"], True)):
            with self.subTest(argv=argv), \
                    patch.object(sys, "argv", argv), \
                    patch.object(run_full, "run_full_game", return_value=object()) as run, \
                    patch.object(run_full, "record_run", return_value="run.json"), \
                    redirect_stdout(io.StringIO()):
                self.assertEqual(run_full.main(), 0)
                self.assertEqual(run.call_args.kwargs["verbose"], expected)


if __name__ == "__main__":
    unittest.main(verbosity=2)
