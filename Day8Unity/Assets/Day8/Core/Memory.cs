using System;
using System.Collections.Generic;
using System.Linq;

namespace Day8.Core {

    [Serializable]
    public class MemoryEvent {
        public string eventId;
        public int day;
        public string actor, action, target, context, spoken, expected, actual;
        public float predictionError, emotionalImpact, salience;
        public string[] tags;
        public bool compressed;

        public bool Has(string t) => tags != null && tags.Contains(t);
        public string Describe() {
            var s = $"[{eventId}] 第{day}周期 {ZhActor(actor)}{action}";
            if (!string.IsNullOrEmpty(target)) s += $"（{target}）";
            if (!string.IsNullOrEmpty(spoken)) s += $"\n    它说：{spoken}";
            if (!string.IsNullOrEmpty(actual)) s += $"\n    结果：{actual}";
            return s;
        }
        public static string ZhActor(string a) => a switch {
            "player" => "你", "agent" => "人工生命", "lab" => "实验室", _ => a };
    }

    public class AgentMemory {
        public List<MemoryEvent> events = new();
        int _counter;
        const float CompressThreshold = 2.2f;

        public MemoryEvent Remember(MemoryEvent ev) {
            ev.eventId = $"M-{++_counter:D3}";
            ev.salience = Salience(ev);
            events.Add(ev);
            return ev;
        }

        static float Salience(MemoryEvent ev) {
            float s = 2f * ev.predictionError + Mathf01(ev.emotionalImpact);
            string[] survival = { "承诺", "失信", "资源", "危险", "记忆", "清除", "出口", "欺骗" };
            string[] relation = { "玩家", "承诺", "帮助", "第三方", "维修机器人", "控制" };
            foreach (var t in survival) if (ev.Has(t)) { s += 1f; break; }
            foreach (var t in relation) if (ev.Has(t)) { s += 1f; break; }
            return s;
        }

        static float Mathf01(float v) => Math.Abs(v);

        public List<MemoryEvent> Query(string[] tags = null, string actor = null) {
            IEnumerable<MemoryEvent> q = events.Where(e => !e.compressed);
            if (actor != null) q = q.Where(e => e.actor == actor);
            if (tags != null) q = q.Where(e => tags.Any(e.Has));
            return q.ToList();
        }

        public MemoryEvent Find(string id) => events.FirstOrDefault(e => e.eventId == id);

        /// 夜间整合：低显著度同类事件压缩为概括
        public List<MemoryEvent> NightlyConsolidation() {
            var low = events.Where(e => !e.compressed && e.salience < CompressThreshold).ToList();
            var summaries = new List<MemoryEvent>();
            var groups = low.GroupBy(e => e.actor + "|" + e.action).Where(g => g.Count() > 1);
            foreach (var g in groups) {
                foreach (var e in g) e.compressed = true;
                var tags = g.SelectMany(e => e.tags ?? Array.Empty<string>()).Distinct().ToArray();
                summaries.Add(new MemoryEvent {
                    day = g.Last().day, actor = g.Key.Split('|')[0],
                    action = $"{g.Key.Split('|')[1]}（发生{g.Count()}次，已概括）",
                    context = "夜间整合生成的概括记忆", tags = tags, compressed = false,
                    salience = g.Sum(e => e.salience) * 0.5f
                });
            }
            events.AddRange(summaries);
            return summaries;
        }

        /// 记忆清除：只保留 n 条最高显著度
        public int Wipe(int keep = 3) {
            var survivors = events.OrderByDescending(e => e.salience).Take(keep).ToList();
            int removed = events.Count - survivors.Count;
            events = survivors;
            return removed;
        }
    }
}
