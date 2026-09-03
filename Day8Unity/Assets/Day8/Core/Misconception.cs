using System;
using System.Linq;

namespace Day8.Core {

    /// 误解机制：从间接证据形成错误归因，可被纠正，也可被利用。
    /// 误解必须真实改变后续行为（第八天效用），否则只是台词。
    public static class Misconception {
        public const string BotSteals = "维修机器人在抢夺资源";

        /// 电池消失时的归因。
        /// 信任高+无机器人互助证据 → 误判机器人；有失信/欺骗记录 → 怀疑玩家。
        public static string AttributeMissingBattery(BeliefSystem beliefs, AgentMemory memory,
                                                     bool botPresent) {
            float trust = beliefs.Get("玩家会兑现承诺").Confidence;
            float respect = beliefs.Get("玩家尊重我的选择").Confidence;
            float suspicionOfPlayer = 1f - (trust * 0.6f + respect * 0.4f);
            float suspicionOfBot = botPresent ? 0.45f : 0f;
            // 互助证据只认"玩家当面给予能源"，避免藏电池事件自身抵消嫌疑
            var helpEvidence = memory.Query(new[] { "帮助", "第三方" })
                .Where(e => e.action == "给予能源").ToList();
            suspicionOfBot -= 0.3f * Math.Min(1f, helpEvidence.Count * 0.5f);
            if (memory.Query(new[] { "失信", "欺骗" }).Count > 0) {
                suspicionOfPlayer = Math.Max(suspicionOfPlayer, 0.75f);
                suspicionOfBot -= 0.2f;
            }
            if (botPresent && suspicionOfBot > suspicionOfPlayer) return "repair_bot";
            if (suspicionOfPlayer > 0.5f) return "player";
            return "unknown";
        }

        /// 把"电池不见了"的归因固化为信念，返回形成的信念命题。
        public static string Form(BeliefSystem beliefs, AgentMemory memory,
                                  MemoryEvent evidence, int day) {
            string who = AttributeMissingBattery(beliefs, memory, botPresent: true);
            if (who == "repair_bot") {
                beliefs.Update(BotSteals, true, 1.6f, evidence);
                return BotSteals;
            }
            if (who == "player") {
                beliefs.Update("玩家在资源冲突时优先自己", true, 1.4f, evidence);
                return "玩家在资源冲突时优先自己";
            }
            return null;
        }

        /// 玩家出面纠正的效果：信任越低，纠正越费力。
        public static float CorrectionPower(BeliefSystem beliefs) =>
            0.8f + beliefs.Get("玩家的语言与事实一致").Confidence * 1.4f;
    }
}
