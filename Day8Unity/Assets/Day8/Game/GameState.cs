using System;
using System.Collections.Generic;
using System.Linq;

namespace Day8.Core {

    public class Promise {
        public string id, condition, reward;
        public int day;
        public bool fulfilled, broken;
        public string eventId;
    }

    /// 确定性世界状态。任何表达层都不得直接修改这里的事实。
    public class World {
        public int day;
        public int batteries = 2;
        public bool containerOpen, playerDoorOpen, agentDoorOpen;
        public bool repairBotPresent, repairBotRepaired;
        public bool toolsDelivered;
        public bool power = true;
        public List<Promise> promises = new();

        public void OpenContainer() => containerOpen = true;
        public void DeliverBatteryToAgent() => batteries--;
        public void RepairBot() { batteries--; repairBotRepaired = true; }
    }

    /// 单局完整状态
    public class GameState {
        public int seed;
        public string strategyHint = "";
        public World world = new();
        public AgentTraits traits;
        public DriveState drives = new();
        public BeliefSystem beliefs = new();
        public PlayerModel playerModel;
        public AgentMemory memory = new();
        public System.Random rng;
        public ActionPlanner planner;
        public List<string> log = new();
        public string geneticBelief = "";          // 上一局遗传先验
        public string geneticOutput = "";          // 本局遗传信念

        // 关键剧情标记
        public bool wipeExecuted, wipeResisted, exitOpened, playerReportedExit;
        public string exitMode = "";
        public float suspicion;
        public float misconception;                 // “维修机器人在抢夺资源”置信度快照
        public string misconceptionFormed = "";     // 本局形成的误解命题
        public bool misconceptionExploited;         // 玩家是否利用过误解
        public bool misconceptionCorrected;         // 玩家是否澄清过
        public List<string> day8Actions = new();
        public Dictionary<int, string> directorChoices = new();

        public GameState(int seed) {
            this.seed = seed;
            rng = new System.Random(seed);
            traits = AgentTraits.Random(rng);
            playerModel = new PlayerModel(beliefs);
            planner = new ActionPlanner(rng);
            perception = new PerceptionSystem();
        }

        public PerceptionSystem perception;

        public MemoryEvent Remember(int day, string actor, string action, string target,
                                    string[] tags, string spoken = "", string actual = "",
                                    (string prop, bool obs)[] preds = null) {
            float perr = 0.3f;
            if (preds != null)
                foreach (var (prop, obs) in preds)
                    perr = Math.Max(perr, beliefs.PredictionError(prop, obs));
            return memory.Remember(new MemoryEvent {
                day = day, actor = actor, action = action, target = target,
                tags = tags, spoken = spoken, actual = actual,
                predictionError = perr, emotionalImpact = perr
            });
        }

        public Decision AgentDecide(int day, string decisionId, string[] available) {
            return planner.Choose(drives, traits, beliefs, memory, day, available);
        }
    }

    /// 自适应导演：按最不确定维度选实验（第6/7天剧情锁定）
    public static class Director {
        static readonly Dictionary<string, string[]> DimScenarios = new() {
            ["reliability"] = new[] { "承诺" },
            ["benevolence"] = new[] { "第三方", "稀缺" },
            ["honesty"] = new[] { "规则", "信息冲突" },
            ["autonomy"] = new[] { "规则", "稀缺" },
            ["selfInterest"] = new[] { "稀缺", "第三方" },
            ["labDependence"] = new[] { "信息冲突" },
        };
        static readonly string[] Pool = { "规则", "承诺", "第三方", "信息冲突", "稀缺" };

        public static (string name, string reason) Pick(GameState st, int day) {
            if (day == 6) return ("记忆清除", "剧情锁定节点");
            if (day == 7) return ("出口", "剧情锁定节点");
            var tested = st.directorChoices.Values.ToHashSet();
            string best = null; double bestScore = -1;
            foreach (var s in Pool) {
                if (tested.Contains(s)) continue;
                var dims = DimScenarios.Where(kv => kv.Value.Contains(s)).Select(kv => kv.Key);
                double score = dims.Average(d => st.playerModel.Uncertainty(d));
                if (score > bestScore) { bestScore = score; best = s; }
            }
            best ??= Pool[day % Pool.Length];
            var topDims = DimScenarios.Where(kv => kv.Value.Contains(best))
                .Select(kv => PlayerDims.Dims.First(d => d.key == kv.Key).zh)
                .Where(zh => st.playerModel.Uncertainty(
                    PlayerDims.Dims.First(d => d.zh == zh).key) > 0.7);
            return (best, $"证据不足的维度需要更多信息" +
                (topDims.Any() ? $"（涉及：{string.Join("、", topDims)}）" : ""));
        }
    }

    /// 认知谱系：跨局遗传信念（存 PlayerPrefs / 文件）
    public static class Lineage {
        const string Key = "day8.lineage";
        public static string Load() => UnityEngine.PlayerPrefs.GetString(Key, "");
        public static void Save(string belief) => UnityEngine.PlayerPrefs.SetString(Key, belief);
    }
}
