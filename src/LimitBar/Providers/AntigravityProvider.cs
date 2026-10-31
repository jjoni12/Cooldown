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
            var windows = ReadWindows(root, claudeGpt);
            if (windows == null) throw new UsageException(Text.AgySignIn);
            return new ServiceUsage { Id = Id, Name = claudeGpt ? "Antigravity · Claude + GPT" : "Antigravity · Gemini", Windows = windows };
        }

        // Free (Starter) accounts get only the weekly limit; buckets without a remaining fraction are skipped.
        static List<UsageWindow> ReadWindows(object root, bool claudeGpt)
        {
            var groups = Json.Arr(Json.Get(root, "command", "data", "groups")).ToList();
            var prefix = claudeGpt ? "3p-" : "gemini-";
            // The ordinary pool (gemini-5h / gemini-weekly) before versioned or model-specific groups.
            var group = groups.FirstOrDefault(g => HasBucket(g, id => id == prefix + "5h" || id == prefix + "weekly"))
                ?? groups.FirstOrDefault(g => (Json.Str(Json.Get(g, "name")) ?? "").IndexOf(claudeGpt ? "Claude" : "Gemini", StringComparison.OrdinalIgnoreCase) >= 0
                    || HasBucket(g, id => id.StartsWith(prefix)));
            if (group == null) return null;

            var windows = new List<UsageWindow>();
            foreach (var bucket in Json.Arr(Json.Get(group, "buckets")))
            {
                var length = WindowLength(Json.Str(Json.Get(bucket, "window"))) ?? WindowLength(Json.Str(Json.Get(bucket, "id")));
                var remaining = Json.Num(Json.Get(bucket, "remaining_fraction"));
                if (!length.HasValue || !remaining.HasValue) continue;
                windows.Add(new UsageWindow
                {
                    Label = Text.WindowLabel(length.Value),
                    Length = length.Value,
                    UsedPercent = Math.Max(0, Math.Min(100, (1 - remaining.Value) * 100)),
                    ResetsAt = Json.Time(Json.Get(bucket, "reset_time")),
                });
            }
            return windows.OrderBy(w => w.Length).ToList();
        }

        static bool HasBucket(object group, Func<string, bool> id) =>
            Json.Arr(Json.Get(group, "buckets")).Any(b => id(Json.Str(Json.Get(b, "id")) ?? ""));

        static TimeSpan? WindowLength(string window)
        {
            if (string.IsNullOrEmpty(window)) return null;
            var w = window.ToLowerInvariant();
            if (w.EndsWith("5h") || w.Contains("five") || w.Contains("session")) return TimeSpan.FromHours(5);
            if (w.EndsWith("weekly") || w.EndsWith("week") || w.EndsWith("7d")) return TimeSpan.FromDays(7);
            return null;
        }
    }
}
