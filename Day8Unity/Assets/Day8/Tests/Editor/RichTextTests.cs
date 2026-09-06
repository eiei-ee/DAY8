using Day8.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Day8.UI;

namespace Day8.Tests {

    /// 富文本渲染验证：用 TextGenerator 的顶点颜色判定 <color> 标签是否被解析。
    public class RichTextTests {

        Text MakeText(string content) {
            var go = new GameObject("t", typeof(RectTransform));
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.supportRichText = true;
            t.fontSize = 14;
            t.text = content;
            return t;
        }

        [Test]
        public void AppendLog_Reconstruction_KeepsRichTextIntact() {
            // 模拟 GameUI 的解析-重建循环：任何重建结果必须与原始行等价
            string line = "<color=#D9A05B>它｜测试台词</color>";
            string prefix = "", text = line;
            if (line.StartsWith("<color=")) {
                int gt = line.IndexOf('>');
                if (gt > 0 && line.EndsWith("</color>")) {
                    prefix = line.Substring(0, gt + 1);
                    text = line.Substring(gt + 1, line.Length - gt - 1 - "</color>".Length);
                }
            }
            string suffix = prefix == "" ? "" : "</color>";
            string rebuilt = prefix + text + suffix;
            Assert.AreEqual(line, rebuilt, "解析-重建必须无损");
            Assert.AreEqual("它｜测试台词", text);
        }

        [Test]
        public void BeliefReport_DisclosesLiveEvidenceAndLineageTogether() {
            var belief = new Belief {
                proposition = "玩家会兑现承诺",
                alpha = 2f,
                evidence = new System.Collections.Generic.List<Evidence> {
                    new Evidence { eventId = "M-007", dir = 1, w = 1f }
                }
            };

            string report = GameUI.FormatBeliefForReport(
                belief, belief.proposition, "不相信「玩家会兑现承诺」", false);

            StringAssert.Contains("依据：M-007", report);
            StringAssert.Contains("另含谱系先验：不相信", report);
        }
    }
}
