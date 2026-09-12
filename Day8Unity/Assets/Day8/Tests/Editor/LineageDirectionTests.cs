using Day8.Core;
using NUnit.Framework;
using UnityEngine;

namespace Day8.Tests {

    /// 契约（Lineage and language）：谱系保存命题与方向；
    /// 正向先验加 alpha，负向加 beta；旧存档视为正向。
    public class LineageDirectionTests {

        [Test]
        public void Encode_PreservesDirection() {
            var beliefs = new BeliefSystem();
            var pos = beliefs.Get("玩家尊重我的选择");
            pos.alpha += 2f;                       // conf > 0.5 → 正向
            Assert.AreEqual("+|玩家尊重我的选择", Lineage.Encode(pos));
            var neg = beliefs.Get("玩家的语言与事实一致");
            neg.beta += 2f;                        // conf < 0.5 → 负向
            Assert.AreEqual("-|玩家的语言与事实一致", Lineage.Encode(neg));
        }

        [Test]
        public void Load_DescribesDirectionInChinese() {
            PlayerPrefs.SetString("day8.lineage", "+|玩家尊重我的选择");
            Assert.IsTrue(Lineage.Load().Contains("相信"));
            PlayerPrefs.SetString("day8.lineage", "-|玩家的语言与事实一致");
            Assert.IsTrue(Lineage.Load().Contains("不相信"));
            PlayerPrefs.DeleteKey("day8.lineage");
        }

        [Test]
        public void ApplyPrior_RaisesCorrectSide() {
            var st = new GameState(42);
            Lineage.ApplyPrior(st, "+|p");          // 正向先验 → alpha 增加
            Assert.Greater(st.beliefs.Get("p").Confidence, 0.5f);
            var st2 = new GameState(43);
            Lineage.ApplyPrior(st2, "-|q");         // 负向先验 → beta 增加
            Assert.Less(st2.beliefs.Get("q").Confidence, 0.5f);
        }

        [Test]
        public void LegacyPositiveOnly_ArchivesTreatedAsPositive() {
            PlayerPrefs.SetString("day8.lineage", "旧格式命题");
            Assert.IsTrue(Lineage.Load().Contains("相信"),
                "旧格式（仅命题）必须按正向先验解读");
            PlayerPrefs.DeleteKey("day8.lineage");
        }
    }
}
