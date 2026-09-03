using System;

namespace Day8.Core {

    public enum Visibility { Direct, OutcomeOnly, Invisible }

    /// 感知滤过结果：要么完整目击，要么只留间接证据（误解之源）。
    public class Percept {
        public Visibility visibility;
        public string note = "";

        public static Percept Direct(string note = null) =>
            new Percept { visibility = Visibility.Direct, note = note ?? "" };
        public static Percept Outcome(string note) =>
            new Percept { visibility = Visibility.OutcomeOnly, note = note };
    }

    /// 感知系统：人工生命不应该知道所有事情。
    /// 控制台后的操作默认"仅结果可见"；警觉度随周期与好奇度增长。
    public class PerceptionSystem {
        /// 0..1，超过 0.65 时玩家在控制台后的操作可能被当场目击
        public float alertness = 0.5f;

        public void OnDay(int day, float curiosity) {
            // 前期藏匿几乎总成功；后期或高好奇才可能被抓到
            alertness = Math.Min(1f, 0.35f + day * 0.04f + curiosity * 0.2f);
        }

        public Percept Perceive(string actor, string action, string target, string spoken = null) {
            if (!string.IsNullOrEmpty(spoken))
                return Percept.Direct("听到了原话");
            bool consoleAction = action == "隐藏物品" || action == "私下操作终端"
                || action == "向实验室发送消息" || action == "查看协议详情";
            if (!consoleAction)
                return Percept.Direct();
            if (alertness > 0.65f)
                return Percept.Direct("（它当时正看着你的控制台）");
            return Percept.Outcome($"只注意到结果：{target}的状态变了。它没有看到是谁做的。");
        }
    }
}
