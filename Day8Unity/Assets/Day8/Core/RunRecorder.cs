using System;
using System.IO;
using System.Text;

namespace Day8.Core {

    /// 运行记录器：每局完整落盘（文档 §18 RunRecorder / §22 可复现原则）。
    /// 写入 persistentDataPath/runs/：JSON（机器可读）+ TXT（人类可读）。
    public static class RunRecorder {

        public static string RunsDir =>
            Path.Combine(UnityEngine.Application.persistentDataPath, "runs");

        /// 保存一局。transcript 为界面完整对白；返回写入的 TXT 路径。
        public static string Save(GameState st, string transcript, string[] reportLines) {
            Directory.CreateDirectory(RunsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string baseName = $"run-{stamp}-seed{st.seed}";

            var json = new StringBuilder();
            json.Append("{\n");
            json.Append($"  \"savedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\n");
            json.Append($"  \"seed\": {st.seed},\n");
            json.Append($"  \"traits\": \"{Escape(st.traits.ToString())}\",\n");
            json.Append($"  \"geneticBeliefInherited\": \"{Escape(st.geneticBelief)}\",\n");
            json.Append($"  \"geneticBeliefPassedOn\": \"{Escape(st.geneticOutput)}\",\n");
            json.Append($"  \"wipeExecuted\": {st.wipeExecuted.ToString().ToLower()},\n");
            json.Append($"  \"wipeResisted\": {st.wipeResisted.ToString().ToLower()},\n");
            json.Append($"  \"exitMode\": \"{Escape(st.exitMode)}\",\n");
            json.Append($"  \"misconception\": {st.misconception.ToString("F2")},\n");
            json.Append($"  \"misconceptionExploited\": {st.misconceptionExploited.ToString().ToLower()},\n");
            json.Append($"  \"misconceptionCorrected\": {st.misconceptionCorrected.ToString().ToLower()},\n");
            json.Append($"  \"day8Actions\": [{string.Join(", ", st.day8Actions.ConvertAll(a => "\"" + Escape(a) + "\""))}],\n");
            json.Append($"  \"world\": {UnityEngine.JsonUtility.ToJson(st.world)},\n");
            json.Append("  \"beliefs\": [\n");
            var all = st.beliefs.All;
            for (int i = 0; i < all.Count; i++) {
                var b = all[i];
                json.Append($"    {{\"prop\": \"{Escape(b.proposition)}\", \"conf\": {b.Confidence:F2}, " +
                            $"\"certainty\": {b.Certainty:F2}}}{(i < all.Count - 1 ? "," : "")}\n");
            }
            json.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(RunsDir, baseName + ".json"), json.ToString());

            var txt = new StringBuilder();
            txt.AppendLine($"《第八天》运行记录  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  seed={st.seed}");
            txt.AppendLine($"初始特质：{st.traits}");
            if (!string.IsNullOrEmpty(st.geneticBelief))
                txt.AppendLine($"继承先验：{st.geneticBelief}");
            txt.AppendLine(new string('-', 60));
            txt.AppendLine(transcript);
            txt.AppendLine(new string('-', 60));
            txt.AppendLine("信念终态：");
            txt.AppendLine(st.beliefs.Dump());
            txt.AppendLine("结局报告：");
            foreach (var r in reportLines) txt.AppendLine("  " + r);
            string txtPath = Path.Combine(RunsDir, baseName + ".txt");
            File.WriteAllText(txtPath, txt.ToString());
            return txtPath;
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
