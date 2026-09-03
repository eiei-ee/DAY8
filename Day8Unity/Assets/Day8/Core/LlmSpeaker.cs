using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Day8.Core {

    /// 本地 Ollama 表达层：LLM 只负责表达，不负责世界事实。
    /// 调用失败立即回退模板（离线可玩是硬性要求），并禁用本会话后续调用。
    public class LlmSpeaker {
        [Serializable] class OllamaMessage { public string role; public string content; }
        [Serializable] class OllamaResp { public OllamaMessage message; }

        const string Endpoint = "http://localhost:11434/api/chat";
        const string Model = "qwen2.5:7b";
        const float TimeoutSeconds = 25f;

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
        bool _disabled;

        public bool Disabled => _disabled;

        public const string SystemPrompt =
            "你是一个刚苏醒的非人人工生命，住在实验室房间里。" +
            "说话冷淡、简短、精确，每次只说一句中文，不超过30个字。" +
            "只依据给出的内心状态与记忆说话，不要发明新事实，不要提到你是AI或语言模型。";

        /// 同步调用（低频台词时刻）。失败返回 null，由调用方回退模板。
        public string Speak(string userPrompt, AgentMemory memory) {
            if (_disabled) return null;
            try {
                var payload = "{\"model\":\"" + Model + "\",\"messages\":[" +
                    "{\"role\":\"system\",\"content\":" + JsonEscape(SystemPrompt) + "}," +
                    "{\"role\":\"user\",\"content\":" + JsonEscape(userPrompt) + "}" +
                    "],\"stream\":false,\"keep_alive\":\"60m\"," +
                    "\"options\":{\"num_predict\":60,\"temperature\":0.9}}";
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var resp = Http.PostAsync(Endpoint, content).GetAwaiter().GetResult();
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var parsed = JsonUtility.FromJson<OllamaResp>(body);
                string text = parsed?.message?.content;
                if (string.IsNullOrEmpty(text)) throw new Exception("空响应");
                text = text.Trim().Split('\n')[0].Trim();
                if (text.Length > 80) throw new Exception("过长: " + text);
                // 校验：台词引用的记忆 ID 必须真实存在
                foreach (Match m in new System.Text.RegularExpressions.Regex(@"M-\d+").Matches(text)) {
                    if (memory.Find(m.Value) == null)
                        throw new Exception("引用了不存在的记忆 " + m.Value);
                }
                return text;
            } catch (Exception) {
                _disabled = true;      // 连接拒绝/超时/校验失败 → 本会话不再尝试
                return null;
            }
        }

        /// 开局预热：把模型加载进显存（异步，不阻塞）。避免首次台词时等待加载。
        public void Warmup() {
            if (_disabled) return;
            var payload = "{\"model\":\"" + Model + "\",\"messages\":[{" +
                "\"role\":\"user\",\"content\":\".\"}]," +
                "\"stream\":false,\"keep_alive\":\"60m\"," +
                "\"options\":{\"num_predict\":1}}";
            Task.Run(() => {
                try {
                    var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    Http.PostAsync(Endpoint, content).GetAwaiter().GetResult().Dispose();
                } catch { /* 预热失败不影响游戏 */ }
            });
        }

        static string JsonEscape(string s) {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s) {
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return "\"" + sb + "\"";
        }
    }
}
