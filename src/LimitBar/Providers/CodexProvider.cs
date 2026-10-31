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

            var root = Json.Parse(body);
            var usage = new ServiceUsage { Id = Id, Name = Name };
            foreach (var key in new[] { "primary_window", "secondary_window" })
            {
                var window = Json.Get(root, "rate_limit", key);
                var used = Json.Num(Json.Get(window, "used_percent"));
                var seconds = Json.Num(Json.Get(window, "limit_window_seconds"));
                if (!used.HasValue || !seconds.HasValue || seconds.Value <= 0) continue;
                var length = TimeSpan.FromSeconds(seconds.Value);
                usage.Windows.Add(new UsageWindow
                {
                    Label = Text.WindowLabel(length),
                    Length = length,
                    UsedPercent = used.Value,
                    ResetsAt = Json.Time(Json.Get(window, "reset_at")),
                });
            }
            usage.Windows = usage.Windows.OrderBy(w => w.Length).ToList();
            return usage;
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
