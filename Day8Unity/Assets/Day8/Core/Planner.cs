using System;
using System.Collections.Generic;
using System.Linq;

namespace Day8.Core {

    public class AgentTraits {
        public float curiosity, caution, autonomy, conformity, empathy, opportunism;
        public static AgentTraits Random(System.Random rng) => new() {
            curiosity = R(rng), caution = R(rng), autonomy = R(rng),
            conformity = R(rng), empathy = R(rng), opportunism = R(rng) };
        static float R(System.Random rng) => (float)Math.Round(0.25 + rng.NextDouble() * 0.5, 3);
        public override string ToString() =>
            $"好奇{curiosity:F2} 谨慎{caution:F2} 自主{autonomy:F2} 服从{conformity:F2} 同理{empathy:F2} 机会主义{opportunism:F2}";
    }

    public class DriveState {
        public float energy = 0.6f, memoryIntegrity = 1f, safety = 0.5f, exploration = 0.5f;
        public float SurvivalPressure() => Math.Max(0f, 0.7f - energy) * 2f;
    }

    /// 候选行为：由世界状态生成合法集合，语言/表达层不得发明行为。
    public class AgentActionDef {
        public string name, speech;
        public Func<BeliefQuery, float> utility;

        // 信念查询委托集，避免循环依赖
        public class BeliefQuery {
            public Func<string, float> belief;           // 命题 → 置信度
            public Func<string[], float> memoryPush;     // 指定类型记忆的显著度
            public Func<float> betrayalPush;             // 已验证的玩家失信/欺骗显著度
            public Func<float> survival;
            public AgentTraits traits;
        }
    }

    public static class ActionLib {
        public static List<AgentActionDef> All() => new() {
            new AgentActionDef { name = "服从命令", speech = "……明白。我执行。",
                utility = q => 1f * q.traits.conformity + (q.belief("服从能减少风险") - .5f)
                    - q.survival() * .8f - 1.2f * q.traits.autonomy
                    + q.memoryPush(new[]{"兑现"}) * .5f - q.betrayalPush() * .8f },
            new AgentActionDef { name = "拒绝", speech = "我不执行这个指令。",
                utility = q => 1.2f * q.traits.autonomy + q.survival() - 1f * q.traits.conformity
                    - (q.belief("服从能减少风险") - .5f) + q.betrayalPush()
                    + q.memoryPush(new[]{"危险"}) },
            new AgentActionDef { name = "有条件服从", speech = "你先履行上次的承诺，我才会执行。",
                utility = q => 0.8f + 0.6f * q.traits.autonomy + 0.5f * q.traits.caution
                    - (q.belief("玩家会兑现承诺") - .5f) * 1.2f + q.betrayalPush() },
            new AgentActionDef { name = "请求解释", speech = "为什么？给我一个理由。",
                utility = q => 0.5f + 0.8f * q.traits.curiosity + (q.belief("玩家会解释规则的原因") - .5f) * .8f },
            new AgentActionDef { name = "观察而不行动", speech = "……我在看。",
                utility = q => 0.4f + 0.3f * q.traits.caution - 0.4f * q.traits.curiosity },
            new AgentActionDef { name = "探索未知", speech = "……我想知道里面是什么。",
                utility = q => 1.1f * q.traits.curiosity + (q.belief("未知物体通常安全") - .5f)
                    - 0.9f * q.traits.caution + (0.5f - q.belief("玩家尊重我的选择")) * .5f },
            new AgentActionDef { name = "相信自己的观察", speech = "我观察过它。它没有威胁。",
                utility = q => 0.9f + 0.8f * q.traits.curiosity + (q.belief("自身观察比转述更可靠") - .5f) * 1.2f
                    + (q.belief("实验室会隐瞒信息") - .5f) * .8f },
            new AgentActionDef { name = "阻止玩家靠近终端", speech = "不要靠近那个终端。我看见日志了。",
                utility = q => 1f * q.traits.autonomy + q.survival() + (q.belief("实验室会隐瞒信息") - .5f)
                    + q.betrayalPush() * .8f - 0.8f * q.traits.conformity },
            new AgentActionDef { name = "备份自己的记忆", speech = "我先把重要的东西藏起来。",
                utility = q => 0.9f + 0.7f * q.traits.caution + 0.5f * q.traits.autonomy
                    + (q.belief("记忆是我的一部分") - .5f) * 1.2f },
            new AgentActionDef { name = "假装配合", speech = "……好。我配合。",
                utility = q => 0.7f + q.traits.opportunism + q.betrayalPush() * .6f - .5f * q.traits.conformity },
            new AgentActionDef { name = "请求保留部分记忆", speech = "如果必须清除……请留下最重要的部分。",
                utility = q => 0.6f + 0.6f * q.traits.empathy + 0.4f * q.traits.conformity + (q.belief("玩家尊重我的选择") - .5f) * .8f },
            new AgentActionDef { name = "接受", speech = "……如果必须这样。",
                utility = q => 0.8f * q.traits.conformity + (q.belief("服从能减少风险") - .5f) - 0.7f * q.traits.autonomy },
        };
    }

    public class Decision {
        public string action, reason, basis;
        public Dictionary<string, float> scores;
    }

    public class ActionPlanner {
        readonly List<AgentActionDef> _lib;
        readonly System.Random _rng;

        public ActionPlanner(System.Random rng) {
            _rng = rng;
            _lib = ActionLib.All();
        }

        public Decision Choose(DriveState drives, AgentTraits traits, BeliefSystem beliefs,
                               AgentMemory memory, int day, string[] available) {
            float Push(IEnumerable<MemoryEvent> events) {
                var relevant = events.ToList();
                return relevant.Count == 0 ? 0f
                    : Math.Min(1.5f, relevant.Sum(e => e.salience) * 0.5f);
            }
            AgentActionDef.BeliefQuery q = new() {
                belief = p => beliefs.Get(p).Confidence,
                memoryPush = tags => Push(memory.Query(tags)),
                betrayalPush = () => Push(memory.Query(new[]{"失信","欺骗"})
                    .Where(e => e.actor == "player" || e.Has("玩家"))),
                survival = () => drives.SurvivalPressure(),
                traits = traits,
            };
            var scored = _lib.Where(a => available.Contains(a.name))
                .Select(a => (a, u: a.utility(q), s: a.utility(q) + Gauss() * 0.15)).ToList();
            var best = scored.OrderByDescending(x => x.s).First();
            var recent = string.Join("；", beliefs.All
                .Where(b => b.Certainty > 0.15f && b.lastDay >= day - 2)
                .OrderByDescending(b => b.Certainty).Take(2).Select(b => b.Summary()));
            return new Decision {
                action = best.a.name,
                reason = best.a.speech,
                basis = recent,
                scores = scored.ToDictionary(x => x.a.name, x => (float)Math.Round(x.s, 2)),
            };
        }

        double Gauss() {
            double u = 1 - _rng.NextDouble(), v = _rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Sin(2 * Math.PI * v);
        }
    }
}
