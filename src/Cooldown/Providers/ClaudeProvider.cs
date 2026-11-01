using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cooldown.Providers
{
    public sealed class ClaudeProvider : IUsageProvider
    {
        const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

        public string Id => "claude";
        public string Name => "Claude";
        public bool IsSetUp => ClaudeLogin.Exists || Cli.Find("claude", @"%USERPROFILE%\.local\bin\claude.exe", @"%APPDATA%\npm\claude.cmd") != null;

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var login = ClaudeLogin.Read() ?? throw new UsageException(Text.ClaudeSignIn);
            // An expired sign-in is never sent: the endpoint answers 401, and asked again, 429 for up to an hour.
            if (login.ExpiresSoon) login = await login.RenewAsync(ct) ?? throw new UsageException(Text.ClaudeSignIn);

            var (status, body) = await Request(login.AccessToken, ct);
            if (status == 401)
            {
                login = await login.RenewAsync(ct) ?? throw new UsageException(Text.ClaudeSignIn);
                (status, body) = await Request(login.AccessToken, ct);
            }
            if (status == 401) throw new UsageException(Text.ClaudeSignIn);
            if (status != 200) throw UsageException.Server(Name, status);

            var root = Json.Parse(body);
            var usage = new ServiceUsage { Id = Id, Name = Name };
            // "limits" also lists weekly limits for single models, which the older fields leave out.
            foreach (var limit in Json.Arr(Json.Get(root, "limits")))
            {
                var percent = Json.Num(Json.Get(limit, "percent"));
                if (!percent.HasValue) continue;
                string label;
                switch (Json.Str(Json.Get(limit, "kind")))
                {
                    case "session": label = "5h"; break;
                    case "weekly_all": label = "1w"; break;
                    case "weekly_scoped":
                        var model = Json.Str(Json.Get(limit, "scope", "model", "display_name"));
                        label = string.IsNullOrEmpty(model) ? "1w" : "1w|" + model;
                        break;
                    default: continue;
                }
                usage.Windows.Add(new UsageWindow
                {
                    Label = label,
                    Length = label == "5h" ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7),
                    UsedPercent = percent.Value,
                    ResetsAt = Json.Time(Json.Get(limit, "resets_at")),
                });
            }
            if (usage.Windows.Count == 0)
            {
                AddWindow(usage, root, "five_hour", TimeSpan.FromHours(5));
                AddWindow(usage, root, "seven_day", TimeSpan.FromDays(7));
            }
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
    }
}
