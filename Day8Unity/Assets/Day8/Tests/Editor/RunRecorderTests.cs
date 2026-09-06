using System.IO;
using Day8.Core;
using NUnit.Framework;
using UnityEngine;

namespace Day8.Tests {

    /// 运行记录器：每局必须落盘且人类可读（文档 §18 / §22 可复现原则）。
    public class RunRecorderTests {

        [Test]
        public void Save_WritesReadableFilesWithSeedAndBeliefs() {
            var st = new GameState(2026);
            var ev = st.Remember(1, "player", "兑现承诺", "电池", new[] { "承诺", "兑现" });
            st.beliefs.Update("玩家会兑现承诺", true, 2f, ev);
            st.world.repairBotPresent = true;
            st.day8Actions.Add("开隔离门");
            st.geneticOutput = "玩家会兑现承诺";

            string path = RunRecorder.Save(st, "第1周期｜测试对白\n第8天｜最终选择", new[] { "理由1" });

            Assert.IsTrue(File.Exists(path), "TXT 必须落盘");
            string content = File.ReadAllText(path);
            StringAssert.Contains("seed=2026", content);
            StringAssert.Contains("测试对白", content);
            StringAssert.Contains("玩家会兑现承诺", content);
            StringAssert.Contains("理由1", content);

            string jsonPath = path.Replace(".txt", ".json");
            Assert.IsTrue(File.Exists(jsonPath), "JSON 必须落盘");
            string json = File.ReadAllText(jsonPath);
            StringAssert.Contains("\"seed\": 2026", json);
            StringAssert.Contains("开隔离门", json);
            StringAssert.Contains("玩家会兑现承诺", json);
        }
    }
}
