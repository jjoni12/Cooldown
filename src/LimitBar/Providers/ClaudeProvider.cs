using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar.Providers
{
    public sealed class ClaudeProvider : IUsageProvider
    {
        const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

        public string Id => "claude";
        public string Name => "Claude";
        public bool IsSetUp => File.Exists(Path.Combine(ConfigDir, ".credentials.json")) || FindClaude() != null;

        static string FindClaude() => Cli.Find("claude", @"%USERPROFILE%\.local\bin\claude.exe", @"%APPDATA%\npm\claude.cmd");

        static string ConfigDir
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                return string.IsNullOrWhiteSpace(custom)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                    : custom;
            }
        }

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var token = ReadToken(out var expiresAt);
            if (token == null || expiresAt < DateTimeOffset.Now.AddMinutes(2))
            {
                await LetClaudeCodeRefresh(ct);
                token = ReadToken(out _);
            }
            if (token == null) throw new UsageException(Text.ClaudeSignIn);

            var (status, body) = await Request(token, ct);
            if (status == 401)
            {
                await LetClaudeCodeRefresh(ct);
                token = ReadToken(out _);
                if (token != null) (status, body) = await Request(token, ct);
            }
            if (status == 401) throw new UsageException(Text.ClaudeSignIn);
            if (status != 200) throw UsageException.Server(Name, status);

            var root = Json.Parse(body);
            var usage = new ServiceUsage { Id = Id, Name = Name };
            AddWindow(usage, root, "five_hour", TimeSpan.FromHours(5));
            AddWindow(usage, root, "seven_day", TimeSpan.FromDays(7));
            return usage;
        }

        static void AddWindow(ServiceUsage usage, object root, string key, TimeSpan length)
        {
            var used = Json.Num(Json.Get(root, key, "utilization"));
            if (!used.HasValue) return;
            usage.Windows.Add(new UsageWindow
            {
                Label = Text.WindowLabel(length),
                Length = length,
                UsedPercent = used.Value,
                ResetsAt = Json.Time(Json.Get(root, key, "resets_at")),
            });
        }

        static Task<(int Status, string Body)> Request(string token, CancellationToken ct) =>
            Http.GetAsync(UsageUrl, new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + token,
                ["anthropic-beta"] = "oauth-2025-04-20",
            }, ct);

        static string ReadToken(out DateTimeOffset expiresAt)
        {
            expiresAt = DateTimeOffset.MinValue;
            var file = Path.Combine(ConfigDir, ".credentials.json");
            if (!File.Exists(file)) return null;
            var oauth = Json.Get(Json.Parse(File.ReadAllText(file)), "claudeAiOauth");
            var expires = Json.Num(Json.Get(oauth, "expiresAt"));
            if (expires.HasValue) expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)expires.Value);
            return Json.Str(Json.Get(oauth, "accessToken"));
        }

        // Tokens are never refreshed here: `claude auth status` makes Claude Code refresh and save its own.
        static async Task LetClaudeCodeRefresh(CancellationToken ct)
        {
            var exe = FindClaude();
            if (exe == null) return;
            var result = await Cli.RunAsync(exe, "auth status", TimeSpan.FromSeconds(45), ct: ct);
            if (result.TimedOut) Log.Info("claude", "`claude auth status` timed out");
        }
    }
}
