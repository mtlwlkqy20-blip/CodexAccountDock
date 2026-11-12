using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexAccountDock
{
    internal static class ModelEvidence
    {
        internal static string Status(string requested, string resolved)
        {
            if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(resolved)) return "无法核对（缺少实际响应标识或所选标识）";
            if (requested == "auto" || requested == "router") return "自动路由（不判定降级）";
            if (string.Equals(requested, resolved, StringComparison.OrdinalIgnoreCase)) return "标识一致";
            string a = Regex.Replace(requested, @"-\d{4}-\d{2}-\d{2}$", "");
            string b = Regex.Replace(resolved, @"-\d{4}-\d{2}-\d{2}$", "");
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return "同系列／快照名称不同（不能判断能力变化）";
            return "标识不同（需核对，不能单凭名称判断降智）";
        }

        private static string Model(object value)
        {
            string text = value as string;
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length > 128 || !Regex.IsMatch(text, @"^[A-Za-z0-9][A-Za-z0-9._:/-]*$")) throw new FormatException();
            return text;
        }

        internal static string ReadReport(string path)
        {
            if (new FileInfo(path).Length > 65536) throw new FormatException();
            var root = JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(File.ReadAllText(path, new UTF8Encoding(false, true))));
            if (root == null || JsonUtil.Text(root, "format") != "codex-account-dock-models-v1" || JsonUtil.Text(root, "source") != "chatgpt-web") throw new FormatException();
            if (root.Keys.Any(k => k != "format" && k != "source" && k != "observations")) throw new FormatException();
            var rows = JsonUtil.Value(root, "observations") as object[];
            if (rows == null || rows.Length > 100) throw new FormatException();
            var lines = new List<string> { "ChatGPT 网页模型报告 · 未绑定助手账号", "这是浏览器观察到的标识，不是能力测评，也不是登录记录。", "报告文件可被修改；它不是服务器签名证明。", "" };
            foreach (object value in rows.Reverse()) {
                var row = JsonUtil.Dict(value);
                if (row == null || row.Keys.Any(k => k != "requested" && k != "resolved" && k != "at")) throw new FormatException();
                string requested = Model(JsonUtil.Value(row, "requested")), resolved = Model(JsonUtil.Value(row, "resolved"));
                string at = AccountOnlineClient.ReadDate(JsonUtil.Value(row, "at"));
                if (at.Length == 0) throw new FormatException();
                lines.Add(DateTimeOffset.Parse(at).ToLocalTime().ToString("MM-dd HH:mm:ss") + "  " + Status(requested, resolved));
                lines.Add("所选：" + (requested.Length == 0 ? "未取得" : requested) + "   返回：" + (resolved.Length == 0 ? "未取得" : resolved));
                lines.Add("");
            }
            if (rows.Length == 0) lines.Add("尚无模型观察记录；需要扩展启用后正常进行一次网页聊天。");
            return string.Join("\r\n", lines);
        }

        internal static string ReadRecentCodex()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            string root = Path.Combine(home, "sessions");
            if (!Directory.Exists(root)) return "未找到本机 Codex 会话记录；无法核对。";
            string[] paths = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).Take(8).ToArray();
            return DescribeCodexFiles(paths);
        }

        internal static string DescribeCodexFiles(string[] paths)
        {
            var observations = new List<CodexObservation>();
            int failed = 0;
            int partial = 0;
            foreach (string path in paths) {
                try {
                    CodexObservation observation = ReadCodexObservation(path);
                    if (observation.Truncated) partial++;
                    observations.Add(observation);
                } catch { failed++; }
            }

            var conversations = observations.Where(o => o.Requested.Length > 0 && o.Requested != "codex-auto-review").ToList();
            var comparable = conversations.Where(o => o.Resolved.Length > 0).ToList();
            var text = new List<string>();
            if (comparable.Count == 0) {
                if (conversations.Count > 0) text.Add("最近会话所选模型：" + conversations[0].Requested);
                text.Add("当前无法核对实际模型");
                text.Add("");
                text.Add("本次读取的记录缺少可配对的实际响应模型字段，因此无法比较所选模型与实际模型。");
                text.Add("");
                
            } else {
                text.Add("已找到可核对的模型记录");
                foreach (CodexObservation observation in comparable.Take(4)) {
                    text.Add("");
                    text.Add("所选模型：" + observation.Requested);
                    text.Add("实际模型：" + observation.Resolved);
                    text.Add(Status(observation.Requested, observation.Resolved));
                }
            }
            var requestedModels = conversations.Select(o => o.Requested).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
            if (comparable.Count == 0 && requestedModels.Length > 0) {
                text.Add("");
                text.Add("本机记录中的模型选择");
                text.Add(string.Join("、", requestedModels));
            }
            text.Add("");
            text.Add("已检查 " + paths.Length + " 份本机记录。此结果未与所选账号绑定。");
            if (partial > 0) text.Add(partial + " 份大文件仅检查末尾部分。");
            if (failed > 0) text.Add(failed + " 份记录未能读取。");
            return string.Join("\r\n", text);
        }

        private sealed class CodexObservation
        {
            internal string Requested;
            internal string Resolved;
            internal int ModelTurns;
            internal bool Truncated;
        }

        internal static string[] ReadCodexFile(string path, out bool truncated)
        {
            CodexObservation observation = ReadCodexObservation(path);
            truncated = observation.Truncated;
            return new [] { "记录：" + Path.GetFileName(path), "最近上下文所选：" + (observation.Requested.Length == 0 ? "未取得" : observation.Requested), "实际响应标识：" + (observation.Resolved.Length == 0 ? "未取得" : observation.Resolved), Status(observation.Requested, observation.Resolved) + " · 检查到 " + observation.ModelTurns + " 个模型上下文" };
        }

        private static CodexObservation ReadCodexObservation(string path)
        {
            string requested = "", resolved = "", turnId = ""; int modelTurns = 0;
            bool truncated;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                truncated = stream.Length > 2 * 1024 * 1024;
                // Scan the whole session: a long turn can put its context far before the final response.
                using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                    truncated = false;
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        if (line.IndexOf("\"turn_context\"", StringComparison.Ordinal) < 0 && line.IndexOf("\"response_item\"", StringComparison.Ordinal) < 0) continue;
                        if (line.Length > 131072) continue;
                        Dictionary<string, object> row;
                        try { row = JsonUtil.Dict(JsonUtil.Serializer.DeserializeObject(line)); } catch { continue; }
                        var payload = JsonUtil.Dict(JsonUtil.Value(row, "payload"));
                        string type = JsonUtil.Text(row, "type");
                        if (type == "turn_context") {
                            requested = Model(JsonUtil.Value(payload, "model")); resolved = "";
                            turnId = JsonUtil.Text(payload, "turn_id"); modelTurns++;
                        } else if (type == "response_item" && requested.Length > 0) {
                            string responseTurn = JsonUtil.Text(payload, "turn_id");
                            if (responseTurn.Length > 0 && turnId.Length > 0 && responseTurn != turnId) continue;
                            string itemType = JsonUtil.Text(payload, "type");
                            if (itemType != "response" && !(itemType == "message" && JsonUtil.Text(payload, "role") == "assistant")) continue;
                            var response = JsonUtil.Dict(JsonUtil.Value(payload, "response"));
                            var metadata = JsonUtil.Dict(JsonUtil.Value(payload, "metadata"));
                            string value = JsonUtil.Text(response, "model");
                            if (value.Length == 0) value = JsonUtil.Text(metadata, "resolved_model_slug");
                            if (value.Length == 0) value = JsonUtil.Text(metadata, "model_slug");
                            if (value.Length > 0) resolved = Model(value);
                        }
                    }
                }
            }
            return new CodexObservation { Requested = requested, Resolved = resolved, ModelTurns = modelTurns, Truncated = truncated };
        }
    }
}
