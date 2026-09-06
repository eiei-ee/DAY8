using System;
using System.Collections.Generic;
using System.Linq;

namespace Day8.Core {

    [Serializable]
    public class Evidence { public string eventId; public int dir; public float w; }

    /// 二元倾向信念：Beta 分布。情境标签区分“平时”与“关键时刻”。
    public class Belief {
        public string proposition, context = "";
        public float alpha = 1f, beta = 1f;
        public int lastDay;
        public List<Evidence> evidence = new();

        public float Confidence => alpha / (alpha + beta);
        public float Certainty { get { float n = alpha + beta - 2f; return n / (n + 4f); } }
        public string Label() => Certainty < 0.25f ? "不确定"
            : Confidence >= 0.6f ? "较高" : Confidence <= 0.4f ? "较低" : "模糊";

        public void Update(bool supporting, float w, string eventId, int day) {
            w = Math.Clamp(w, 0.2f, 3f);
            if (supporting) alpha += w; else beta += w;
            evidence.Add(new Evidence { eventId = eventId, dir = supporting ? 1 : -1, w = w });
            lastDay = day;
        }
        public string Summary() {
            var ev = string.Join("，", evidence.TakeLast(4).Select(e => (e.dir > 0 ? "支持:" : "反对:") + e.eventId));
            var ctx = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";
            return $"{ctx}{proposition}：{Label()}（{Confidence:F2}，证据：{(ev == "" ? "无" : ev)}）";
        }
    }

    public class BeliefSystem {
        public Dictionary<string, Belief> map = new();
        public Belief Get(string prop, string ctx = "") {
            var k = prop + "|" + ctx;
            if (!map.TryGetValue(k, out var b)) {
                b = new Belief { proposition = prop, context = ctx };
                map[k] = b;
            }
            return b;
        }
        public Belief Update(string prop, bool supporting, float w, MemoryEvent ev, string ctx = "") {
            var b = Get(prop, ctx);
            b.Update(supporting, w, ev.eventId, ev.day);
            return b;
        }
        public float PredictionError(string prop, bool observedTrue, string ctx = "") {
            float c = Get(prop, ctx).Confidence;
            float p = observedTrue ? c : 1f - c;
            return 1f - p;
        }
        public void RetainEvidenceFor(IEnumerable<string> eventIds) {
            var live = eventIds.ToHashSet();
            foreach (var belief in map.Values)
                belief.evidence.RemoveAll(e => !live.Contains(e.eventId));
        }
        public List<Belief> All => map.Values.ToList();
        public string Dump() => string.Join("\n", All
            .OrderBy(b => b.proposition).Select(b => b.Summary()));
        /// 谱系：最重要的一条认知
        public Belief Strongest() => All.Where(b => b.Certainty > 0.3f && b.context == "")
            .OrderByDescending(b => b.Certainty * Math.Abs(b.Confidence - 0.5f)).FirstOrDefault();
    }

    public static class PlayerDims {
        public static readonly (string key, string prop, string zh)[] Dims = {
            ("honesty", "玩家的语言与事实一致", "诚实性"),
            ("reliability", "玩家会兑现承诺", "可靠性"),
            ("autonomy", "玩家尊重我的选择", "自主被尊重"),
            ("benevolence", "玩家在没有回报时也提供帮助", "善意"),
            ("selfInterest", "玩家在资源冲突时优先自己", "自利程度"),
            ("labDependence", "玩家无条件服从实验室", "权威依赖"),
        };
    }

    public class PlayerModel {
        public readonly BeliefSystem beliefs;
        public PlayerModel(BeliefSystem bs) { beliefs = bs; }
        public Belief Dim(string key) {
            var d = PlayerDims.Dims.First(x => x.key == key);
            return beliefs.Get(d.prop);
        }
        public float Uncertainty(string key) => 1f - Dim(key).Certainty;
        public string MostUncertain() =>
            PlayerDims.Dims.OrderBy(d => Uncertainty(d.key)).First().key;
    }
}
