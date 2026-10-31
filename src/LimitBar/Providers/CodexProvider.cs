using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar.Providers
{
    public sealed class CodexProvider : IUsageProvider
    {
        const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";

        public string Id => "gpt";
        public string Name => "GPT";
        public bool IsSetUp => File.Exists(Path.Combine(CodexHome, "auth.json"));

        static string CodexHome
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable("CODEX_HOME");
                return string.IsNullOrWhiteSpace(custom)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
                    : custom;
            }
        }

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var auth = ReadAuth();
            if (auth == null) throw new UsageException(Text.CodexSignIn);

            var (status, body) = await Request(auth.Value, ct);
            if (status == 401)
            {
                var exe = Cli.Find("codex", @"%APPDATA%\npm\codex.cmd");
                if (exe != null) await Cli.RunAsync(exe, "login status", TimeSpan.FromSeconds(30), ct: ct);
                auth = ReadAuth();
                if (auth != null) (status, body) = await Request(auth.Value, ct);
            }
            if (status == 401 || status == 403) throw new UsageException(Text.CodexSignIn);
            if (status != 200) throw UsageException.Server(Name, status);

            return new ServiceUsage { Id = Id, Name = Name, Windows = ReadWindows(Json.Parse(body)) };
        }

        // Free: one 30-day window. Plus and Business: 5 hours and weekly. Model-specific limits
        // (GPT-5.3-Codex-Spark) come in additional_rate_limits, and a workspace's monthly credit
        // cap per member in individual_limit.
        static List<UsageWindow> ReadWindows(object root)
        {
            var windows = RateWindows(Json.Get(root, "rate_limit"), null).OrderBy(w => w.Length).ToList();

            var cap = Json.Get(root, "individual_limit") ?? Json.Get(root, "rate_limit", "individual_limit") ?? Json.Get(root, "spend_control", "individual_limit");
            var limit = Json.Num(Json.Get(cap, "limit"));
            if (limit > 0)
            {
                var used = Json.Num(Json.Get(cap, "used"));
                var left = Json.Num(Json.Get(cap, "remaining_percent")) ?? Json.Num(Json.Get(cap, "remainingPercent"));
                var resetsAt = Json.Time(Json.Get(cap, "reset_at") ?? Json.Get(cap, "resets_at") ?? Json.Get(cap, "resetsAt"));
                windows.Add(new UsageWindow
                {
                    Label = Text.CreditsLabel,
                    Length = UsageWindow.MonthEndingAt(resetsAt),
                    UsedPercent = Math.Max(0, Math.Min(100, used.HasValue ? used.Value / limit.Value * 100 : 100 - (left ?? 100))),
                    ResetsAt = resetsAt,
                    FixedReset = resetsAt.HasValue,
                });
            }

            foreach (var extra in Json.Arr(Json.Get(root, "additional_rate_limits")))
            {
                var name = (Json.Str(Json.Get(extra, "limit_name")) ?? Json.Str(Json.Get(extra, "metered_feature")))?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf("spark", StringComparison.OrdinalIgnoreCase) >= 0) name = "Spark";
                windows.AddRange(RateWindows(Json.Get(extra, "rate_limit"), name).OrderBy(w => w.Length));
            }

            if (windows.Count == 0)
            {
                var credits = Json.Get(root, "credits");
                var balance = Json.Num(Json.Get(credits, "balance"));
                if (Json.Get(credits, "unlimited") is true) throw new UsageException(Text.NoLimits);
                if (Json.Get(credits, "has_credits") is true && balance.HasValue) throw new UsageException(Text.CodexCreditsLeft(balance.Value));
            }
            return windows;
        }

        static IEnumerable<UsageWindow> RateWindows(object rateLimit, string scope)
        {
            foreach (var key in new[] { "primary_window", "secondary_window" })
            {
                var window = Json.Get(rateLimit, key);
                var used = Json.Num(Json.Get(window, "used_percent"));
                var seconds = Json.Num(Json.Get(window, "limit_window_seconds"));
                if (!used.HasValue || !seconds.HasValue || seconds.Value <= 0) continue;
                var length = TimeSpan.FromSeconds(seconds.Value);
                yield return new UsageWindow
                {
                    Label = scope == null ? Text.WindowLabel(length) : Text.WindowLabel(length) + "|" + scope,
                    Length = length,
                    UsedPercent = used.Value,
                    ResetsAt = Json.Time(Json.Get(window, "reset_at")),
                };
            }
        }

        static Task<(int Status, string Body)> Request((string Token, string AccountId) auth, CancellationToken ct)
        {
            var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + auth.Token };
            if (!string.IsNullOrEmpty(auth.AccountId)) headers["ChatGPT-Account-Id"] = auth.AccountId;
            return Http.GetAsync(UsageUrl, headers, ct);
        }

        static (string Token, string AccountId)? ReadAuth()
        {
            var file = Path.Combine(CodexHome, "auth.json");
            if (!File.Exists(file)) return null;
            var tokens = Json.Get(Json.Parse(File.ReadAllText(file)), "tokens");
            var token = Json.Str(Json.Get(tokens, "access_token"));
            if (string.IsNullOrEmpty(token)) return null;
            return (token, Json.Str(Json.Get(tokens, "account_id")));
        }
    }
}
