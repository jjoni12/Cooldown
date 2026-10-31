using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar.Providers
{
    public sealed class AntigravityProvider : IUsageProvider
    {
        public string Id => "antigravity";
        public string Name => Settings.AntigravityClaudeGpt ? "Antigravity · Claude + GPT" : "Antigravity · Gemini";
        public bool IsSetUp => FindAgy() != null;

        static string FindAgy() => Cli.Find("agy", @"%LOCALAPPDATA%\agy\bin\agy.exe");

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var exe = FindAgy();
            if (exe == null) throw new UsageException(Text.AgyMissing);

            // A private working folder keeps agy from picking up a project's settings.
            var workDir = Path.Combine(Path.GetTempPath(), "LimitBar", "agy");
            Directory.CreateDirectory(workDir);
            var env = new Dictionary<string, string> { ["AGY_CLI_DISABLE_AUTO_UPDATE"] = "true" };

            var result = await Cli.RunAsync(exe, "-p /usage --output-format json", TimeSpan.FromSeconds(90), workDir, env, ct);
            if (result.TimedOut) throw new UsageException(Text.AgyTimeout);

            var json = Json.ExtractObject(result.Stdout);
            var root = json == null ? null : Json.Parse(json);
            if (root == null || Json.Str(Json.Get(root, "status")) != "SUCCESS") throw new UsageException(Text.AgySignIn);

            var claudeGpt = Settings.AntigravityClaudeGpt;
            var group = Json.Arr(Json.Get(root, "command", "data", "groups")).FirstOrDefault(g => claudeGpt ? IsClaudeGpt(g) : IsGemini(g));
            if (group == null) throw new UsageException(Text.AgySignIn);

            var usage = new ServiceUsage { Id = Id, Name = claudeGpt ? "Antigravity · Claude + GPT" : "Antigravity · Gemini" };
            foreach (var bucket in Json.Arr(Json.Get(group, "buckets")))
            {
                var length = WindowLength(Json.Str(Json.Get(bucket, "window")) ?? Json.Str(Json.Get(bucket, "id")));
                var remaining = Json.Num(Json.Get(bucket, "remaining_fraction"));
                if (!length.HasValue || !remaining.HasValue) continue;
                usage.Windows.Add(new UsageWindow
                {
                    Label = Text.WindowLabel(length.Value),
                    Length = length.Value,
                    UsedPercent = Math.Max(0, Math.Min(100, (1 - remaining.Value) * 100)),
                    ResetsAt = Json.Time(Json.Get(bucket, "reset_time")),
                });
            }
            usage.Windows = usage.Windows.OrderBy(w => w.Length).ToList();
            return usage;
        }

        static bool IsGemini(object group) => GroupMatches(group, "Gemini", "gemini-");

        static bool IsClaudeGpt(object group) => GroupMatches(group, "Claude", "3p-");

        static bool GroupMatches(object group, string nameWord, string bucketPrefix)
        {
            var name = Json.Str(Json.Get(group, "name")) ?? "";
            return name.IndexOf(nameWord, StringComparison.OrdinalIgnoreCase) >= 0
                || Json.Arr(Json.Get(group, "buckets")).Any(b => (Json.Str(Json.Get(b, "id")) ?? "").StartsWith(bucketPrefix));
        }

        static TimeSpan? WindowLength(string window)
        {
            if (window == null) return null;
            if (window.EndsWith("5h")) return TimeSpan.FromHours(5);
            if (window.EndsWith("weekly")) return TimeSpan.FromDays(7);
            return null;
        }
    }
}
