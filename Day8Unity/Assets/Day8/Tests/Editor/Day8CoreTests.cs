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
            st.perception.OnDay(3, 0.3f);        // 低警觉 → 藏匿成功
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
            Assert.IsTrue(st.memory.Query().Any(e => e.Has("异常") && e.actual.Contains("不见了")),
                "必须存在'电池不见了'的间接证据");
            Assert.IsFalse(st.memory.Query().Any(e => e.action == "隐藏电池" && e.Has("玩家")),
                "不应存在直接目击记录");
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
        }

        [Test]
        public void Scenario_Correction_LowersMisconception() {
            var (st, flow) = RunHidePath(42);
            float before = st.misconception;
            var correct = flow.next.options.First(o => o.label.Contains("纠正"));
            correct.apply(st);
            Assert.Less(st.misconception, before, "澄清必须降低误解");
            Assert.IsTrue(st.misconceptionCorrected);
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
