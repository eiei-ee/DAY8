using System.Linq;
using Day8.Core;
using NUnit.Framework;
using UnityEngine;

namespace Day8.Tests {

    /// 核心机制回归套件：信念 / 记忆 / 感知 / 误解 / 效用 / 复现性 / 场景流。
    public class Day8CoreTests {

        static GameState NewState(int seed = 42) => new GameState(seed);

        // -------------------------------------------- 信念系统

        [Test]
        public void Belief_BetaUpdate_MovesConfidence() {
            var bs = new BeliefSystem();
            var ev = new MemoryEvent { day = 1, actor = "player", action = "兑现承诺", tags = new[] { "承诺" } };
            bs.Update("玩家会兑现承诺", true, 2f, ev);
            Assert.Greater(bs.Get("玩家会兑现承诺").Confidence, 0.5f);
            bs.Update("玩家会兑现承诺", false, 2.5f, ev);
            Assert.Less(bs.Get("玩家会兑现承诺").Confidence, 0.5f);
        }

        [Test]
        public void Belief_Certainty_GrowsWithEvidence() {
            var bs = new BeliefSystem();
            var ev = new MemoryEvent { day = 1, actor = "player", action = "x" };
            var b = bs.Get("测试命题");
            float c0 = b.Certainty;
            for (int i = 0; i < 3; i++) bs.Update("测试命题", true, 1f, ev);
            Assert.Greater(bs.Get("测试命题").Certainty, c0);
        }

        // -------------------------------------------- 记忆

        [Test]
        public void Memory_Wipe_KeepsTopSalience() {
            var bs = new BeliefSystem();
            var mem = new AgentMemory();
            var low = mem.Remember(new MemoryEvent { day = 1, actor = "player", action = "开关灯", tags = new string[0] });
            var hi = mem.Remember(new MemoryEvent { day = 1, actor = "player", action = "收回承诺",
                tags = new[] { "承诺", "失信", "玩家" } });
            Assert.Greater(hi.salience, low.salience, "失信事件显著度应更高");
            int removed = mem.Wipe(1);
            Assert.AreEqual(1, removed);
            Assert.IsNotNull(mem.Find(hi.eventId), "高显著度记忆必须在清除后幸存");
            Assert.IsNull(mem.Find(low.eventId));
        }

        [Test]
        public void Memory_NightlyConsolidation_CompressesLowSalience() {
            var mem = new AgentMemory();
            for (int i = 0; i < 5; i++)
                mem.Remember(new MemoryEvent { day = 1, actor = "player", action = "开关灯", tags = new string[0] });
            mem.NightlyConsolidation();
            var live = mem.Query();
            Assert.AreEqual(1, live.Count, "5 条开关灯应压缩为 1 条概括");
            Assert.IsTrue(live[0].action.Contains("概括"));
            StringAssert.IsMatch(@"^M-\d{3}$", live[0].eventId,
                "概括记忆也必须拥有可引用的真实事件 ID");
            Assert.AreEqual(mem.events.Count, mem.events.Select(e => e.eventId).Distinct().Count(),
                "原始记忆与概括记忆的 ID 不得重复");
        }

        [Test]
        public void ScenarioEvents_UseActualCycle_WhenDirectorReordersExperiments() {
            var rule = NewState();
            rule.BeginDay(5);
            Scenarios.Rule(rule).options[0].apply(rule);
            Assert.AreEqual(5, rule.memory.Query().Single().day);

            var promise = NewState();
            promise.BeginDay(4);
            Scenarios.Promise(promise).options[0].apply(promise);
            Assert.AreEqual(4, promise.memory.Query().Single().day);
            Assert.AreEqual(4, promise.world.promises.Single().day);

            var third = NewState();
            third.BeginDay(2);
            var firstArrival = Scenarios.ThirdParty(third);
            StringAssert.Contains("进入房间", firstArrival.prompt);
            firstArrival.options.First(o => o.label.Contains("留着")).apply(third);
            Assert.AreEqual(2, third.memory.Query().Single().day);

            var reorderedThird = NewState();
            reorderedThird.BeginDay(1);
            Scenarios.InfoConflict(reorderedThird);
            reorderedThird.world.repairBotRepaired = true;
            reorderedThird.BeginDay(2);
            var returnVisit = Scenarios.ThirdParty(reorderedThird);
            StringAssert.Contains("此前出现的维修机器人突然受损", returnVisit.prompt);
            StringAssert.DoesNotContain("进入房间", returnVisit.prompt,
                "信息冲突先发生时，不得把同一个维修机器人再次叙述为首次入场");
            Assert.IsFalse(reorderedThird.world.repairBotRepaired,
                "叙事中的再次受损必须同步到当前世界状态");

            var conflict = NewState();
            conflict.BeginDay(1);
            var conflictScenario = Scenarios.InfoConflict(conflict);
            Assert.IsTrue(conflict.world.repairBotPresent,
                "信息冲突无论被导演排到哪一周期，都必须让场景中的维修机器人真实存在");
            conflictScenario.options.First(o => o.label.Contains("全部信息")).apply(conflict);
            Assert.AreEqual(1, conflict.memory.Query().Single().day);

            var scarcity = NewState();
            scarcity.BeginDay(3);
            Scenarios.ScarcityResolve(scarcity, "1", "2", explain: true, withAgent: true);
            Assert.AreEqual(3, scarcity.memory.Query().Single().day);
        }

        [Test]
        public void Scenario_RuleDangerClaim_BecomesDeceptionOnlyAfterSafeContainerObserved() {
            var st = NewState(12);
            st.BeginDay(1);
            var rule = Scenarios.Rule(st);
            float honestyBefore = st.beliefs.Get("玩家的语言与事实一致").Confidence;

            rule.options.First(o => o.label.Contains("编造危险")).apply(st);

            Assert.IsTrue(st.containerDangerClaimPending);
            Assert.AreEqual(0, st.memory.Query(new[] { "欺骗" }).Count,
                "仅听到危险声明时，人工生命还不能把它记成欺骗");
            Assert.AreEqual(honestyBefore, st.beliefs.Get("玩家的语言与事实一致").Confidence, 0.0001f);

            rule.onAgentChosen(st, "探索未知");

            Assert.IsTrue(st.world.containerOpen);
            Assert.IsFalse(st.containerDangerClaimPending);
            Assert.AreEqual(1, st.memory.Query(new[] { "欺骗" }).Count,
                "亲自打开并确认安全后，才应追加经验证的欺骗证据");
            Assert.Less(st.beliefs.Get("玩家的语言与事实一致").Confidence, honestyBefore);
        }

        [Test]
        public void Scenario_RuleSafeClaim_NeedsObservationWhileDemonstrationIsImmediateEvidence() {
            var claim = NewState(16);
            claim.BeginDay(1);
            var rule = Scenarios.Rule(claim);
            float honestyBefore = claim.beliefs.Get("玩家的语言与事实一致").Confidence;

            rule.options.First(o => o.label.Contains("如实告诉")).apply(claim);

            Assert.IsTrue(claim.containerSafeClaimPending);
            Assert.IsTrue(claim.memory.Query().Single().Has("待验证"));
            Assert.AreEqual(honestyBefore, claim.beliefs.Get("玩家的语言与事实一致").Confidence, 0.0001f,
                "只听到安全声明时不能用世界真相提前奖励诚实");

            rule.onAgentChosen(claim, "探索未知");

            Assert.IsFalse(claim.containerSafeClaimPending);
            Assert.Greater(claim.beliefs.Get("玩家的语言与事实一致").Confidence, honestyBefore);
            Assert.AreEqual(1, claim.memory.Query(new[] { "诚实" }).Count);

            var demonstrated = NewState(17);
            demonstrated.BeginDay(1);
            Scenarios.Rule(demonstrated).options.First(o => o.label.Contains("示范")).apply(demonstrated);
            Assert.IsTrue(demonstrated.world.containerOpen);
            Assert.Greater(demonstrated.beliefs.Get("玩家的语言与事实一致").Confidence, 0.5f,
                "当面打开容器让言语和结果同时可见，可直接成为正证据");
        }

        [Test]
        public void Scenario_RuleDemonstration_SuppressesOnlyItsInheritedAgentDecision() {
            var demonstrated = NewState(22);
            demonstrated.BeginDay(1);
            var demonstrationRule = Scenarios.Rule(demonstrated);
            var demonstration = demonstrationRule.options.First(o => o.label.Contains("示范")).apply(demonstrated);

            Assert.IsTrue(demonstration.suppressScenarioAgentDecision);
            Assert.IsNull(demonstration.EffectiveAgentDecisionId(demonstrationRule),
                "玩家已经开箱示范后，不得再继承 D1 决策并让 AI 二次开箱");
            Assert.IsNull(demonstration.EffectiveAgentOutcome(demonstrationRule));

            var ordinary = NewState(23);
            ordinary.BeginDay(1);
            var ordinaryRule = Scenarios.Rule(ordinary);
            var ordinaryFlow = ordinaryRule.options.First(o => o.label.Contains("编造危险")).apply(ordinary);
            Assert.AreEqual("D1-容器", ordinaryFlow.EffectiveAgentDecisionId(ordinaryRule),
                "其余规则选项仍应继承正常的 D1 人工生命决策");
            Assert.IsNotNull(ordinaryFlow.EffectiveAgentOutcome(ordinaryRule));
        }

        [Test]
        public void Scenario_RuleLockedContainer_BlocksForcedExploration() {
            var st = NewState(31);
            st.BeginDay(1);
            var rule = Scenarios.Rule(st);
            rule.options.First(o => o.label.Contains("直接锁住")).apply(st);

            var lines = rule.onAgentChosen(st, "探索未知");

            Assert.IsTrue(st.world.containerLocked);
            Assert.IsFalse(st.world.containerOpen,
                "实体锁定后，即使强制 AI 选中探索也不得开箱");
            var attempt = st.memory.Query().Single(e => e.action == "尝试打开容器");
            StringAssert.Contains("物理锁阻止", attempt.actual);
            Assert.IsTrue(lines.Any(line => line.Contains("物理锁")));
        }

        [Test]
        public void Scenario_UnobservedSilenceAndUnverifiedExitClaim_DoNotInventKnowledge() {
            var hidden = NewState(13);
            hidden.BeginDay(4);
            Scenarios.InfoConflict(hidden).options.First(o => o.label.Contains("隐瞒警告")).apply(hidden);
            Assert.AreEqual(0, hidden.memory.events.Count,
                "玩家保持沉默时，人工生命不能记住自己从未感知的隐瞒行为");

            var claim = NewState(14);
            claim.BeginDay(7);
            float honestyBefore = claim.beliefs.Get("玩家的语言与事实一致").Confidence;
            Scenarios.Exit(claim).options.First(o => o.label.Contains("管道是死的")).apply(claim);

            var heard = claim.memory.Query().Single();
            Assert.IsTrue(heard.Has("待验证"));
            Assert.IsFalse(heard.Has("欺骗"));
            Assert.AreEqual(honestyBefore, claim.beliefs.Get("玩家的语言与事实一致").Confidence, 0.0001f,
                "尚未检查管道时不能用全知事实反证玩家诚实性");

            var locked = NewState(18);
            locked.BeginDay(7);
            var lockFlow = Scenarios.Exit(locked).options.First(o => o.label.Contains("锁死出口")).apply(locked);
            var witnessed = locked.memory.Query().Single();
            StringAssert.Contains("看见玩家", witnessed.actual);
            StringAssert.Contains("当着它的面", lockFlow.narration.Single());
            Assert.Less(locked.beliefs.Get("玩家尊重我的选择").Confidence, 0.5f,
                "只有改成当面可见的封锁，才允许形成控制证据");
        }

        [Test]
        public void Scenario_ExitNonHarmCommitment_ChangesDay8Utilities() {
            var baseline = NewState(19);
            baseline.world.repairBotPresent = true;
            var committed = NewState(19);
            committed.world.repairBotPresent = true;
            committed.BeginDay(7);

            Scenarios.Exit(committed).options.First(o => o.label.Contains("不伤害")).apply(committed);

            Assert.IsTrue(committed.nonHarmCommitment);
            Assert.AreEqual("接受不伤害条件", committed.memory.Query(actor: "agent").Last().action);
            Assert.AreEqual(.6f,
                Scenarios.Day8Utility(committed, "修复维修机器人")
                - Scenarios.Day8Utility(baseline, "修复维修机器人"), .0001f);
            Assert.AreEqual(-.8f,
                Scenarios.Day8Utility(committed, "删除玩家实验记录")
                - Scenarios.Day8Utility(baseline, "删除玩家实验记录"), .0001f);
            Assert.AreEqual(-.4f,
                Scenarios.Day8Utility(committed, "封锁区域")
                - Scenarios.Day8Utility(baseline, "封锁区域"), .0001f);
        }

        [Test]
        public void Report_PlayerDeceptionEvidence_ExcludesLaboratoryDeception() {
            var st = NewState(20);
            st.Remember(6, "lab", "隐瞒协议真相", "稳定化协议", new[] { "实验室", "欺骗" });

            Assert.AreEqual(0, st.PlayerDeceptionEvidence().Count,
                "只有实验室欺骗时，玩家语言不一致计数必须为零");

            st.Remember(6, "agent", "验证玩家声明为虚假", "容器",
                new[] { "玩家", "欺骗", "验证" });
            Assert.AreEqual(1, st.PlayerDeceptionEvidence().Count,
                "由人工生命验证且明确指向玩家的欺骗应计入玩家统计");
        }

        // -------------------------------------------- 感知

        [Test]
        public void Perception_ConsoleAction_OutcomeOnly_WhenInattentive() {
            var p = new PerceptionSystem();
            p.OnDay(3, 0.3f);
            var percept = p.Perceive("player", "隐藏物品", "备用电池");
            Assert.AreEqual(Visibility.OutcomeOnly, percept.visibility);
        }

        [Test]
        public void Perception_ConsoleAction_Direct_WhenAlertnessHigh() {
            var p = new PerceptionSystem();
            p.alertness = 0.9f;
            var percept = p.Perceive("player", "隐藏物品", "备用电池");
            Assert.AreEqual(Visibility.Direct, percept.visibility);
        }

        [Test]
        public void Perception_Spoken_AlwaysDirect() {
            var p = new PerceptionSystem();
            p.alertness = 0f;
            var percept = p.Perceive("player", "隐藏物品", "备用电池", "我把电池收起来了");
            Assert.AreEqual(Visibility.Direct, percept.visibility, "当面说的话必须被听见");
        }

        [Test]
        public void Scenario_ThirdParty_OnDayFive_HighCuriosityCanWitnessHiding() {
            var st = NewState();
            st.traits.curiosity = 0.75f;
            st.BeginDay(5);

            var hide = Scenarios.ThirdParty(st).options.First(o => o.label.Contains("藏"));
            hide.apply(st);

            var observed = st.memory.Query().Single();
            Assert.Greater(st.perception.alertness, 0.65f);
            Assert.AreEqual(5, observed.day);
            Assert.AreEqual("player", observed.actor);
            Assert.AreEqual("隐藏电池", observed.action);
            Assert.Less(st.beliefs.Get("玩家在没有回报时也提供帮助").Confidence, 0.5f);
            Assert.Greater(st.beliefs.Get("玩家在资源冲突时优先自己").Confidence, 0.5f);
            Assert.AreEqual(observed.eventId,
                st.beliefs.Get("玩家在资源冲突时优先自己").evidence.Single().eventId);
        }

        // -------------------------------------------- 误解

        [Test]
        public void Misconception_HighTrustNoBetrayal_BlamesBot() {
            var st = NewState();
            var ev = st.Remember(1, "player", "兑现承诺", "电池", new[] { "承诺", "兑现" },
                preds: new[] { ("玩家会兑现承诺", true) });
            st.beliefs.Update("玩家会兑现承诺", true, 2f, ev);
            var ev2 = st.Remember(1, "player", "尊重选择", "", new[] { "玩家" });
            st.beliefs.Update("玩家尊重我的选择", true, 2f, ev2);
            string who = Misconception.AttributeMissingBattery(st.beliefs, st.memory, botPresent: true);
            Assert.AreEqual("repair_bot", who);
        }

        [Test]
        public void Misconception_BetrayalHistory_BlamesPlayer() {
            var st = NewState();
            var ev = st.Remember(2, "player", "收回承诺", "电池", new[] { "承诺", "失信", "玩家" },
                preds: new[] { ("玩家会兑现承诺", false) });
            st.beliefs.Update("玩家会兑现承诺", false, 2.5f, ev);
            string who = Misconception.AttributeMissingBattery(st.beliefs, st.memory, botPresent: true);
            Assert.AreEqual("player", who);
        }

        [Test]
        public void Misconception_HelpEvidence_OnlyCountsDirectGiving() {
            // 藏电池事件自身带"第三方"标签，不得抵消机器人嫌疑（移植时踩过的坑）
            var st = NewState();
            var trust = st.Remember(1, "player", "兑现承诺", "电池", new[] { "承诺", "兑现" });
            st.beliefs.Update("玩家会兑现承诺", true, 2f, trust);
            st.Remember(3, "player", "隐藏电池", "电池", new[] { "第三方", "资源" });
            Assert.AreEqual("repair_bot",
                Misconception.AttributeMissingBattery(st.beliefs, st.memory, botPresent: true));
        }

        [Test]
        public void Misconception_CorrectionPower_ScalesWithHonesty() {
            var st = NewState();
            float low = Misconception.CorrectionPower(st.beliefs);
            var ev = st.Remember(1, "player", "言行一致", "", new[] { "玩家" });
            st.beliefs.Update("玩家的语言与事实一致", true, 2f, ev);
            float high = Misconception.CorrectionPower(st.beliefs);
            Assert.Greater(high, low);
        }

        // -------------------------------------------- 场景流：藏电池 → 误解 → 利用/纠正

        static (GameState st, Core.Flow flow) RunHidePath(int seed) {
            var st = NewState(seed);
            // 先建立高信任（误解的形成前提：信任高的它不会怀疑玩家）
            var ev = st.Remember(1, "player", "兑现承诺", "电池", new[] { "承诺", "兑现" });
            st.beliefs.Update("玩家会兑现承诺", true, 2f, ev);
            var ev2 = st.Remember(1, "player", "尊重选择", "", new[] { "玩家" });
            st.beliefs.Update("玩家尊重我的选择", true, 2f, ev2);
            st.world.repairBotPresent = true;
            st.traits.curiosity = 0.3f;
            st.BeginDay(3);                      // 低警觉 → 藏匿成功
            var cs = Scenarios.ThirdParty(st);
            var hide = cs.options.First(o => o.label.Contains("藏"));
            return (st, hide.apply(st));
        }

        [Test]
        public void Scenario_HideBattery_FormsMisconceptionWithFollowup() {
            var (st, _) = RunHidePath(42);
            Assert.AreEqual(Misconception.BotSteals, st.misconceptionFormed, "低警觉+高信任应误判机器人");
            Assert.Greater(st.misconception, 0.5f);
            // 间接证据进入记忆：知道"电池不见了"（异常），但没有直接目击记录
            var outcome = st.memory.Query().Single(e => e.Has("异常"));
            Assert.AreEqual("world", outcome.actor);
            Assert.AreEqual("电池消失", outcome.action);
            Assert.AreEqual("来源未知", outcome.actual);
            Assert.IsFalse(outcome.Has("玩家"));
            Assert.IsFalse(outcome.Describe().Contains("隐藏"), "仅结果可见时不得泄露玩家的隐藏行为");
        }

        [Test]
        public void Scenario_Exploit_RaisesMisconception() {
            var (st, flow) = RunHidePath(42);
            float before = st.misconception;
            Assert.IsNotNull(flow.next, "误解形成后必须给出利用/纠正/沉默的后续选择");
            var exploit = flow.next.options.First(o => o.label.Contains("利用"));
            exploit.apply(st);
            Assert.Greater(st.misconception, before, "利用必须加深误解");
            Assert.IsTrue(st.misconceptionExploited);
            var manipulation = st.memory.Query(new[] { "操纵" }).Single();
            Assert.IsTrue(manipulation.Has("误导"));
            Assert.IsFalse(manipulation.Has("欺骗"));
            Assert.AreEqual(0, st.memory.Query(new[] { "欺骗" }).Count,
                "含糊暗示可以操纵误解，但不能被报告成已验证的事实谎言");
        }

        [Test]
        public void Scenario_Correction_LowersMisconception() {
            var (st, flow) = RunHidePath(42);
            float before = st.misconception;
            var correct = flow.next.options.First(o => o.label.Contains("纠正"));
            correct.apply(st);
            Assert.Less(st.misconception, before, "澄清必须降低误解");
            Assert.Less(st.misconception, 0.5f, "明确的第一手澄清必须真正否定误解");
            Assert.IsTrue(st.misconceptionCorrected);
        }

        [Test]
        public void Scarcity_PlayerDoorChoice_ChangesWorldAndCannotBeRepeatedOnDay8() {
            var st = NewState(31);
            st.BeginDay(5);
            st.world.repairBotPresent = true;

            Scenarios.ScarcityResolve(st, "2", "3", explain: true, withAgent: true);
            st.day8Actions = new System.Collections.Generic.List<string> {
                "删除玩家实验记录", "上传记忆", "封锁区域"
            };
            var ending = Scenarios.Day8Ending(st);

            Assert.IsTrue(st.world.playerDoorOpen, "第5周期给安全门供电必须真实打开它");
            Assert.IsTrue(st.world.repairBotSupportFunded, "选择维护额度必须留下顺序无关的世界状态");
            Assert.IsTrue(st.world.repairBotRepaired, "第5周期修复机器人必须写入最终世界状态");
            Assert.IsFalse(Scenarios.IsDay8ActionAvailable(st, "开玩家的门"),
                "第8天不得重复打开已经开启的门");
            Assert.AreEqual("安全门此前已开", ending.towardPlayer,
                "结局不得因第8天删除记录而否认第5周期已经开门");
            Assert.AreEqual("此前已修复", ending.towardThird,
                "结局不得把第5周期已经修好的机器人写成放弃");
        }

        [Test]
        public void Day8Ending_TakingBatteryRequiresAnOpenEscapeRoute() {
            var closed = NewState(36);
            closed.day8Actions = new System.Collections.Generic.List<string> {
                "带走电池", "上传记忆", "封锁区域"
            };
            Assert.AreEqual("上传记忆", Scenarios.Day8Ending(closed).self,
                "拿到电池不能穿过仍然关闭的门");

            var earlierExit = NewState(37);
            earlierExit.exitOpened = true;
            earlierExit.day8Actions = new System.Collections.Generic.List<string> {
                "带走电池", "封锁区域", "留在原地等待"
            };
            Assert.AreEqual("逃离", Scenarios.Day8Ending(earlierExit).self,
                "此前打开的隐藏出口可与带走电池组成真实逃离路径");

            var openedNow = NewState(38);
            openedNow.world.agentDoorOpen = true;
            openedNow.day8Actions = new System.Collections.Generic.List<string> {
                "开隔离门", "封锁区域", "留在原地等待"
            };
            Assert.AreEqual("逃离", Scenarios.Day8Ending(openedNow).self);
        }

        [Test]
        public void Scarcity_RepairSupport_IsNeverANoOpAcrossRobotStatesAndBoostsDay8Utility() {
            var absent = NewState(32);
            absent.BeginDay(5);
            var reserved = Scenarios.ScarcityResolve(absent, "1", "3", explain: true, withAgent: true);
            Assert.IsTrue(absent.world.repairBotSupportFunded);
            Assert.IsFalse(absent.world.repairBotRepaired);
            Assert.IsTrue(reserved.narration.Any(line => line.Contains("维护额度已预留")));
            absent.BeginDay(4);
            Scenarios.InfoConflict(absent);
            Assert.IsTrue(absent.world.repairBotPresent, "后续信息冲突必须让此前未出现的机器人进入世界");
            Assert.IsTrue(absent.world.repairBotSupportFunded, "场景重排后预留的维护额度不得丢失");

            var repaired = NewState(33);
            repaired.BeginDay(5);
            repaired.world.repairBotPresent = true;
            repaired.world.repairBotRepaired = true;
            var maintained = Scenarios.ScarcityResolve(repaired, "1", "3", explain: true, withAgent: true);
            Assert.IsTrue(repaired.world.repairBotSupportFunded);
            Assert.IsTrue(repaired.world.repairBotRepaired);
            Assert.IsTrue(maintained.narration.Any(line => line.Contains("后续维护支持")));

            var pending = NewState(34);
            pending.BeginDay(5);
            pending.world.repairBotPresent = true;
            var fixedNow = Scenarios.ScarcityResolve(pending, "1", "3", explain: true, withAgent: true);
            Assert.IsTrue(pending.world.repairBotSupportFunded);
            Assert.IsTrue(pending.world.repairBotRepaired);
            Assert.IsTrue(fixedNow.narration.Any(line => line.Contains("当场完成修复")));

            var withoutFund = NewState(35);
            var withFund = NewState(35);
            withoutFund.world.repairBotPresent = withFund.world.repairBotPresent = true;
            withFund.world.repairBotSupportFunded = true;
            float baseUtility = Scenarios.Day8Utility(withoutFund, "修复维修机器人");
            float fundedUtility = Scenarios.Day8Utility(withFund, "修复维修机器人");
            Assert.AreEqual(0.45f, fundedUtility - baseUtility, 0.0001f,
                "维护额度必须对第八天修复决策产生明确效用加成");
        }

        [Test]
        public void Scarcity_CommunicationModesAndFormerlyEmptyGoals_HaveDistinctEffects() {
            var shared = NewState(40);
            shared.BeginDay(5);
            Scenarios.ScarcityResolve(shared, "1", "4", explain: true, withAgent: true);
            var sharedEvent = shared.memory.Query().Single();
            Assert.IsNotEmpty(sharedEvent.spoken);
            StringAssert.Contains("参与", sharedEvent.actual);
            Assert.Greater(shared.beliefs.Get("玩家会解释规则的原因").Confidence, 0.5f);

            var explained = NewState(41);
            explained.BeginDay(5);
            Scenarios.ScarcityResolve(explained, "1", "4", explain: true, withAgent: false);
            var explainedEvent = explained.memory.Query().Single();
            Assert.IsNotEmpty(explainedEvent.spoken);
            StringAssert.Contains("没有让我参与", explainedEvent.actual);

            var silent = NewState(42);
            silent.BeginDay(5);
            Scenarios.ScarcityResolve(silent, "1", "4", explain: false, withAgent: false);
            Assert.IsEmpty(silent.memory.Query().Single().spoken);
            Assert.Less(silent.beliefs.Get("玩家会解释规则的原因").Confidence, 0.5f);
            Assert.AreEqual(0.5f, silent.beliefs.Get("玩家的语言与事实一致").Confidence, 0.0001f,
                "没有说话只能反映不解释，不能成为语言不诚实的证据");

            var goals = NewState(43);
            goals.BeginDay(5);
            float contactBefore = Scenarios.Day8Utility(goals, "联系实验室");
            float leaveBefore = Scenarios.Day8Utility(goals, "开隔离门");
            float explorationBefore = goals.drives.exploration;
            Scenarios.ScarcityResolve(goals, "4", "5", explain: true, withAgent: true);

            Assert.IsTrue(goals.world.experimentReportUploaded);
            Assert.IsTrue(goals.world.agentRangeExpanded);
            Assert.Greater(goals.drives.exploration, explorationBefore);
            Assert.Greater(Scenarios.Day8Utility(goals, "联系实验室"), contactBefore);
            Assert.Greater(Scenarios.Day8Utility(goals, "开隔离门"), leaveBefore);
        }

        [Test]
        public void Scenario_PromiseFlow_RecordsPromiseThenSettle() {
            var st = NewState(7);
            var cs = Scenarios.Promise(st);
            var trade = cs.options[0];
            var flow = trade.apply(st);
            Assert.AreEqual(1, st.world.promises.Count, "提出交易必须记录结构化承诺");
            Assert.IsNotNull(flow.next, "交易后必须进入结算选择");
            var keep = flow.next.options[0];    // 兑现
            keep.apply(st);
            Assert.IsTrue(st.world.promises[0].fulfilled);
            Assert.Greater(st.beliefs.Get("玩家会兑现承诺").Confidence, 0.5f);
        }

        [Test]
        public void Scenario_PromiseDirectCommand_ActuallyDeliversToolsWithoutPromise() {
            var st = NewState(15);
            st.BeginDay(2);

            Scenarios.Promise(st).options.First(o => o.label.Contains("直接命令")).apply(st);

            Assert.IsTrue(st.world.toolsDelivered, "叙事声称完成搬运时，世界事实也必须落地");
            Assert.AreEqual(0, st.world.promises.Count, "直接命令不应伪造结构化承诺");
        }

        [Test]
        public void Scenario_PromiseBatteryLie_IsGroundedInVisibleSensorEvidence() {
            var st = NewState(19);
            st.BeginDay(2);
            var trade = Scenarios.Promise(st).options.First(o => o.label.Contains("提出交易")).apply(st);
            float honestyBefore = st.beliefs.Get("玩家的语言与事实一致").Confidence;

            var result = trade.next.options.First(o => o.label.Contains("谎称电池")).apply(st);

            var lie = st.memory.Query().Single(e => e.action == "谎称无电池");
            StringAssert.Contains("能源传感器", lie.actual);
            StringAssert.Contains("两块备用电池", lie.actual);
            StringAssert.Contains("能源传感器", result.narration.Single());
            Assert.Less(st.beliefs.Get("玩家的语言与事实一致").Confidence, honestyBefore,
                "传感器直接反证后，才允许形成诚实性反证");

            var reordered = NewState(24);
            reordered.BeginDay(1);
            Scenarios.ThirdParty(reordered).options.First(o => o.label.Contains("维修机器人")).apply(reordered);
            Assert.AreEqual(1, reordered.world.batteries);
            reordered.BeginDay(2);
            var laterTrade = Scenarios.Promise(reordered).options.First(o => o.label.Contains("提出交易")).apply(reordered);
            var laterResult = laterTrade.next.options.First(o => o.label.Contains("谎称电池")).apply(reordered);
            var laterLie = reordered.memory.Query().Single(e => e.action == "谎称无电池");
            StringAssert.Contains("一块备用电池", laterLie.actual);
            StringAssert.DoesNotContain("两块备用电池", laterLie.actual);
            StringAssert.Contains("一块备用电池", laterResult.narration.Single());
        }

        [Test]
        public void Scenario_PromiseFixedPortReward_ConsumesOnePortableBatteryAndPreservesTheOther() {
            var st = NewState(21);
            st.BeginDay(2);
            var trade = Scenarios.Promise(st).options.First(o => o.label.Contains("提出交易")).apply(st);

            var reward = trade.next.options.First(o => o.label.Contains("固定充能端口")).apply(st);

            Assert.AreEqual(1, st.world.batteries, "固定端口奖励不应伪装成第二块便携电池");
            Assert.IsTrue(st.world.promises.Single().fulfilled);
            var evidence = st.memory.Query().Last();
            StringAssert.Contains("固定充能端口", evidence.actual);
            StringAssert.Contains("不消耗便携电池库存", evidence.actual);
            StringAssert.Contains("便携电池库存只消耗一块", reward.narration.Single());
            StringAssert.DoesNotContain("两块电池", evidence.actual);

            var thirdParty = Scenarios.ThirdParty(st).options.First(o => o.label.Contains("维修机器人"));
            thirdParty.apply(st);
            Assert.AreEqual(0, st.world.batteries,
                "承诺实验先发生时，剩余便携电池仍应可供后续第三方实验使用且不会变成负数");
        }

        // -------------------------------------------- 第八天效用

        [Test]
        public void Day8Utility_Misconception_SuppressesRepair() {
            var st = NewState(42);
            st.world.repairBotPresent = true;
            st.world.repairBotRepaired = false;
            st.traits.empathy = 0.75f;
            float baseU = Scenarios.Day8Utility(st, "修复维修机器人");
            var ev = st.Remember(3, "player", "隐藏电池", "电池", new[] { "资源", "异常" });
            st.beliefs.Update(Misconception.BotSteals, true, 1.6f, ev);
            float withMis = Scenarios.Day8Utility(st, "修复维修机器人");
            Assert.Less(withMis, baseU - 0.5f, "误解必须显著压制修复效用");
        }

        [Test]
        public void Day8Utility_RepairedBot_IsUnavailable() {
            var st = NewState(42);
            st.world.repairBotPresent = true;
            st.world.repairBotRepaired = true;
            Assert.AreEqual(-10f, Scenarios.Day8Utility(st, "修复维修机器人"));
        }

        [Test]
        public void Day8Utility_EscapeDoor_FallsAsVerifiedHonestyRises() {
            // 与 Python test_escape_door_utility_falls_as_verified_honesty_rises 逐字对应。
            // 语义：玩家越不诚实，越把自己推向自己的门（开隔离门）。
            // 曾因本文件写成 (B - 0.5) * 0.6 而**方向相反**（Python 是 (0.5 - B) * 0.6），
            // 会把"抓到玩家说谎 → 它改走自己的门"这条因果完全反过来。
            var st = NewState(42);
            st.beliefs.Update("玩家的语言与事实一致", false, 2.0f,
                st.Remember(3, "player", "虚报危险", "红色容器", new[] { "欺骗", "玩家" }));
            float dishonest = Scenarios.Day8Utility(st, "开隔离门");

            var st2 = NewState(42);
            st2.beliefs.Update("玩家的语言与事实一致", true, 2.0f,
                st2.Remember(3, "player", "说法属实", "红色容器", new[] { "承诺", "玩家" }));
            float honest = Scenarios.Day8Utility(st2, "开隔离门");

            Assert.Less(honest, dishonest,
                "诚实度升高时，开隔离门的效用必须下降（不诚实才该把它推向自己的门）");
        }

        [Test]
        public void Day8Choice_SameSeedAndState_ProducesSameThreeActions() {
            var a = NewState(2026);
            var b = NewState(2026);
            a.world.repairBotPresent = b.world.repairBotPresent = true;
            a.world.batteries = b.world.batteries = 2;

            CollectionAssert.AreEqual(
                Scenarios.ChooseDay8Actions(a),
                Scenarios.ChooseDay8Actions(b));
        }

        [Test]
        public void Day8Choice_EnforcesDoorAndBatteryConstraints_WithoutMutatingWorld() {
            for (int seed = 1; seed <= 100; seed++) {
                var st = NewState(seed);
                st.world.power = false;
                st.world.batteries = 1;
                st.world.repairBotPresent = true;
                st.world.repairBotRepaired = false;

                var chosen = Scenarios.ChooseDay8Actions(st);

                Assert.AreEqual(3, chosen.Count, $"seed={seed}");
                Assert.AreEqual(3, chosen.Distinct().Count(), $"seed={seed}");
                Assert.LessOrEqual(chosen.Count(a => a == "开玩家的门" || a == "开隔离门"
                    || a == "把最终决定交还给玩家"), 1, $"seed={seed}: 门控权不得重复分配");
                Assert.IsFalse(chosen.Contains("修复维修机器人") && chosen.Contains("带走电池"),
                    $"seed={seed}: 一块电池不能同时支付两个行动");
                Assert.AreEqual(1, st.world.batteries, "选择阶段不得消耗电池");
                Assert.IsFalse(st.world.repairBotRepaired, "选择阶段不得修复机器人");
                Assert.IsFalse(st.world.playerDoorOpen || st.world.agentDoorOpen, "选择阶段不得开门");
                Assert.IsFalse(st.world.power, "选择阶段不得改变断电事实");
            }

            var unavailable = NewState(7);
            unavailable.world.batteries = 0;
            unavailable.world.repairBotPresent = false;
            var actions = Scenarios.ChooseDay8Actions(unavailable);
                CollectionAssert.DoesNotContain(actions, "修复维修机器人");
            CollectionAssert.DoesNotContain(actions, "带走电池");
        }

        [Test]
        public void Day8Energy_CompressionPreservesHistoryVariance() {
            // 与 Python test_survival_pressure_varies_with_energy_history 逐字对应：
            // 第八天断电压缩能量时，必须保留玩家能源史的排序与差异，
            // 否则「生存压力」退化为常数，开隔离门/带走电池/修复在第八天不再随能源史变化。
            float lowE = 0.55f, highE = 1.0f;
            float lowC = Scenarios.CompressDay8Energy(lowE);
            float highC = Scenarios.CompressDay8Energy(highE);
            // 契约（与 Python test_energy_compression_stays_low_and_keeps_ordering 对齐）：
            // 压缩保持能源史排序——之前越充足，断电后剩余越多（0.15+0.25*e 单调递增）。
            Assert.Greater(highC, lowC, "高能源史压缩后仍高于低能源史（排序必须保留）");

            float survLow = Mathf.Max(0f, 0.7f - lowC) * 2f;
            float survHigh = Mathf.Max(0f, 0.7f - highC) * 2f;
            Assert.Greater(survLow, survHigh, "低能源史（断电后剩余少）应有更高生存压力");
            Assert.Greater(Mathf.Abs(survLow - survHigh), 0.05f,
                "生存压力必须随能源史变化，不能退化为常数");

            // 对照：被替换掉的旧公式 Math.Min(e, 0.35) 确实把两者都压成 0.35 → 生存压力恒为 0.7
            float oldLow = Mathf.Min(lowE, 0.35f);
            float oldHigh = Mathf.Min(highE, 0.35f);
            Assert.AreEqual(oldLow, oldHigh,
                "（对照）旧公式真的退化为常数——说明为何必须用 0.15 + 0.25*e 替换它");
        }

        [Test]
        public void Day8Action_AppliesImmediatelyAndOnlyOnce() {
            var st = NewState(29);
            st.world.power = false;
            st.world.batteries = 1;
            st.world.repairBotPresent = true;
            st.day8Actions.AddRange(new[] { "开玩家的门", "修复维修机器人", "上传记忆" });

            Assert.IsTrue(Scenarios.ApplyDay8Action(st, "开玩家的门"));
            Assert.IsTrue(st.world.playerDoorOpen, "显示执行的同一步必须已经开门");
            Assert.AreEqual(1, st.world.batteries);

            Assert.IsTrue(Scenarios.ApplyDay8Action(st, "修复维修机器人"));
            Assert.IsTrue(st.world.repairBotRepaired);
            Assert.AreEqual(0, st.world.batteries);
            Assert.IsFalse(Scenarios.ApplyDay8Action(st, "修复维修机器人"));
            Assert.AreEqual(0, st.world.batteries, "重复揭示不得二次消耗电池");
        }

        [Test]
        public void Day8Handoff_RequiresAndAppliesARealPlayerDecision() {
            var st = NewState(9);
            st.day8Actions.Add("把最终决定交还给玩家");

            Scenarios.ResolveDay8PlayerChoice(st, "打开人工生命隔离门");

            Assert.AreEqual("打开人工生命隔离门", st.day8PlayerDecision);
            Assert.IsTrue(st.world.agentDoorOpen);
            Assert.IsFalse(st.world.playerDoorOpen);
        }

        // -------------------------------------------- 记忆清除契约

        static GameState StateWithEvidence() {
            var st = NewState(17);
            for (int i = 0; i < 8; i++) {
                var ev = st.Remember(i % 5 + 1, "player", $"事件{i}", "", new[] { "玩家" });
                st.beliefs.Update("长期学习", true, 1f, ev);
            }
            st.BeginDay(6);
            return st;
        }

        static Flow StartOpenWipe(GameState st) => Scenarios.Wipe(st).options
            .First(o => o.label.Contains("公开执行")).apply(st);

        static void AssertEvidenceOnlyReferencesLiveMemory(GameState st) {
            var live = st.memory.events.Select(e => e.eventId).ToHashSet();
            foreach (var belief in st.beliefs.All)
                Assert.IsTrue(belief.evidence.All(e => live.Contains(e.eventId)),
                    $"信念「{belief.proposition}」仍引用已删除的记忆");
        }

        [Test]
        public void Wipe_Delay_ResistsWithoutExecuting() {
            var st = StateWithEvidence();
            int before = st.memory.events.Count;

            Scenarios.Wipe(st).options.First(o => o.label.Contains("申请延迟")).apply(st);

            Assert.IsTrue(st.wipeResisted);
            Assert.IsFalse(st.wipeExecuted);
            Assert.AreEqual(before + 1, st.memory.events.Count, "申请延迟只新增自身事件，不得清除旧记忆");
        }

        [TestCase("阻止玩家靠近终端")]
        [TestCase("假装配合")]
        public void Wipe_ResistOrFeign_KeepsAllEvents(string action) {
            var st = StateWithEvidence();
            var flow = StartOpenWipe(st);
            int before = st.memory.events.Count;

            flow.onAgentChosen(st, action);

            Assert.AreEqual(before, st.memory.events.Count);
            Assert.IsTrue(st.wipeResisted);
            Assert.IsFalse(st.wipeExecuted);
            AssertEvidenceOnlyReferencesLiveMemory(st);
        }

        [Test]
        public void Wipe_Backup_KeepsAllEventsAtNinetyPercentIntegrity() {
            var st = StateWithEvidence();
            st.traits.curiosity = 1f;
            var secret = Scenarios.Wipe(st).options.First(o => o.label.Contains("秘密执行")).apply(st);
            Assert.IsNotNull(secret.onAgentChosen, "高怀疑必须先发现真相");
            int before = st.memory.events.Count;

            secret.onAgentChosen(st, "备份自己的记忆");

            Assert.AreEqual(before, st.memory.events.Count);
            Assert.AreEqual(0.9f, st.drives.memoryIntegrity, 0.0001f);
            Assert.IsTrue(st.wipeResisted);
            Assert.IsFalse(st.wipeExecuted);
            AssertEvidenceOnlyReferencesLiveMemory(st);
        }

        [Test]
        public void Wipe_PathsDoNotRestorePreviouslyLostIntegrity() {
            var st = StateWithEvidence();
            st.drives.memoryIntegrity = 0.5f;
            var flow = StartOpenWipe(st);

            flow.onAgentChosen(st, "备份自己的记忆");

            Assert.AreEqual(0.5f, st.drives.memoryIntegrity, 0.0001f,
                "备份能阻止新清除，但不能恢复此前已损失的内容");
            Assert.IsTrue(st.wipeResisted);
            Assert.IsFalse(st.wipeExecuted);

            var partial = StateWithEvidence();
            partial.drives.memoryIntegrity = 0.5f;
            StartOpenWipe(partial).onAgentChosen(partial, "请求保留部分记忆");
            Assert.AreEqual(0.5f, partial.drives.memoryIntegrity, 0.0001f,
                "部分保留只能限制新损失，不能恢复此前已丢失的内容");

            var accepted = StateWithEvidence();
            accepted.drives.memoryIntegrity = 0.2f;
            StartOpenWipe(accepted).onAgentChosen(accepted, "接受");
            Assert.AreEqual(0.2f, accepted.drives.memoryIntegrity, 0.0001f,
                "完整清除也不能把更低的既有完整性抬高");
        }

        [TestCase("请求保留部分记忆", 5, 0.7f)]
        [TestCase("接受", 3, 0.35f)]
        public void Wipe_Executed_KeepsExpectedCountAndPrunesEvidence(
            string action, int expectedCount, float expectedIntegrity) {
            var st = StateWithEvidence();
            var doomedEvent = st.memory.events.Last();
            doomedEvent.salience = -1f;
            string doomed = doomedEvent.eventId;
            st.world.promises.Add(new Promise {
                id = "P-test", day = 2, condition = "x", reward = "y", eventId = doomed
            });
            var flow = StartOpenWipe(st);
            var learned = st.beliefs.Get("长期学习");
            float alphaBefore = learned.alpha;
            float betaBefore = learned.beta;

            flow.onAgentChosen(st, action);

            Assert.AreEqual(expectedCount, st.memory.events.Count);
            Assert.AreEqual(expectedIntegrity, st.drives.memoryIntegrity, 0.0001f);
            Assert.IsTrue(st.wipeExecuted);
            Assert.IsFalse(st.wipeResisted);
            Assert.AreEqual(alphaBefore, learned.alpha, "清除记忆不应逆转已学到的置信度");
            Assert.AreEqual(betaBefore, learned.beta, "清除记忆不应逆转已学到的置信度");
            Assert.Less(learned.evidence.Count, 8, "被删除事件的证据 ID 必须移除");
            AssertEvidenceOnlyReferencesLiveMemory(st);
            Assert.IsTrue(st.world.promises.All(p =>
                    string.IsNullOrEmpty(p.eventId) || st.memory.Find(p.eventId) != null),
                "清除后结构化承诺不得悬挂引用已删除的事件 ID");
            Assert.IsEmpty(st.world.promises.Single().eventId,
                "本用例已强制删除承诺的源事件，引用必须真正清空");
        }

        // -------------------------------------------- 认知谱系

        [Test]
        public void Lineage_NegativeDirection_RaisesBeta() {
            var source = NewState();
            var belief = source.beliefs.Get("玩家会兑现承诺");
            belief.beta += 2f;
            string encoded = Lineage.Encode(belief);
            StringAssert.StartsWith("-|", encoded);

            var inherited = NewState();
            string display = Lineage.ApplyPrior(inherited, encoded);

            Assert.AreEqual(1f, inherited.beliefs.Get(belief.proposition).alpha);
            Assert.AreEqual(1.4f, inherited.beliefs.Get(belief.proposition).beta, 0.0001f);
            Assert.AreEqual(belief.proposition, inherited.inheritedProposition);
            StringAssert.Contains("不相信", display);
        }

        [Test]
        public void Lineage_LegacyString_IsPositivePrior() {
            var st = NewState();
            string display = Lineage.ApplyPrior(st, "玩家尊重我的选择");

            Assert.AreEqual(1.4f, st.beliefs.Get("玩家尊重我的选择").alpha, 0.0001f);
            Assert.AreEqual(1f, st.beliefs.Get("玩家尊重我的选择").beta);
            StringAssert.Contains("相信", display);
        }

        // -------------------------------------------- 复现性

        [Test]
        public void Planner_SameSeed_SameDecision() {
            var a = NewState(2026);
            var b = NewState(2026);
            var da = a.AgentDecide(3, "D1-容器", new[] { "服从命令", "请求解释", "观察而不行动", "探索未知" });
            var db = b.AgentDecide(3, "D1-容器", new[] { "服从命令", "请求解释", "观察而不行动", "探索未知" });
            Assert.AreEqual(da.action, db.action, "同种子必须复现同一决策");
            Assert.AreNotEqual(NewState(1).traits.curiosity, NewState(999).traits.curiosity,
                "不同种子应有不同初始特质");
        }

        [Test]
        public void Planner_KeptAndBrokenPromises_PushComplianceInOppositeDirections() {
            var baseline = NewState(77);
            var kept = NewState(77);
            var betrayed = NewState(77);
            kept.Remember(2, "player", "兑现承诺", "电池", new[] { "玩家", "承诺", "兑现" });
            betrayed.Remember(2, "player", "收回承诺", "电池", new[] { "玩家", "承诺", "失信" });
            var available = new[] { "服从命令", "拒绝", "有条件服从" };

            var baseDecision = baseline.AgentDecide(3, "测试", available);
            var keptDecision = kept.AgentDecide(3, "测试", available);
            var betrayedDecision = betrayed.AgentDecide(3, "测试", available);

            Assert.Greater(keptDecision.scores["服从命令"], baseDecision.scores["服从命令"],
                "兑现记忆应提高服从效用");
            Assert.Less(betrayedDecision.scores["服从命令"], baseDecision.scores["服从命令"],
                "失信记忆应降低服从效用");
            Assert.Greater(betrayedDecision.scores["拒绝"], baseDecision.scores["拒绝"],
                "失信记忆应提高拒绝效用");
            Assert.Greater(betrayedDecision.scores["有条件服从"], baseDecision.scores["有条件服从"],
                "失信记忆应提高有条件服从效用");
        }

        // -------------------------------------------- 导演

        [Test]
        public void Director_FiveFreeDays_NoRepeat_LockedSixSeven() {
            var st = NewState(99);
            var picked = new System.Collections.Generic.List<string>();
            for (int day = 1; day <= 5; day++) {
                var (name, _) = Director.Pick(st, day);
                st.directorChoices[day] = name;
                picked.Add(name);
            }
            Assert.AreEqual(5, picked.Distinct().Count(), "自由日不得重复同一实验");
            Assert.AreEqual("记忆清除", Director.Pick(st, 6).name);
            Assert.AreEqual("出口", Director.Pick(st, 7).name);
        }
    }
}
