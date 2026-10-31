using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cooldown.Providers
{
    public sealed class GrokProvider : IUsageProvider
    {
        const string BillingUrl = "https://cli-chat-proxy.grok.com/v1/billing?format=credits";

        sealed class SignIn
        {
            public string Token;
            public DateTimeOffset? ExpiresAt;
            public bool IsTeam;
        }

        public string Id => "grok";
        public string Name => "Grok";
        public bool IsSetUp => File.Exists(AuthFile) || Cli.Find("grok", @"%USERPROFILE%\.grok\bin\grok.exe") != null;

        static string AuthFile
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable("GROK_HOME");
                var home = string.IsNullOrWhiteSpace(custom)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok")
                    : Environment.ExpandEnvironmentVariables(custom);
                return Path.Combine(home, "auth.json");
            }
        }

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var signIn = File.Exists(AuthFile) ? ReadSignIn(Json.Parse(File.ReadAllText(AuthFile))) : null;
            if (signIn == null) throw new UsageException(Text.GrokSignIn);
            // `grok login` saves a sign-in that lasts about a week; only the grok CLI renews it.
            if (signIn.ExpiresAt <= DateTimeOffset.Now) throw new UsageException(Text.GrokExpired);

            var (status, body) = await Http.GetAsync(BillingUrl, new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + signIn.Token,
                ["x-xai-token-auth"] = "xai-grok-cli",
                ["Accept"] = "application/json",
            }, ct);
            if (status == 401) throw new UsageException(Text.GrokExpired);
            // Grok has no usage answer for team sign-ins yet.
            if (status != 200 && signIn.IsTeam) throw new UsageException(Text.LimitsUnavailable);
            if (status != 200) throw UsageException.Server(Name, status);
            return new ServiceUsage { Id = Id, Name = Name, Windows = ReadWindows(Json.Parse(body), DateTimeOffset.Now) };
        }

        // auth.json is keyed by sign-in scope: SuperGrok's OIDC entry first, then the older session one.
        static SignIn ReadSignIn(object root)
        {
            SignIn oidc = null, session = null;
            if (!(root is IDictionary<string, object> scopes)) return null;
            foreach (var scope in scopes)
            {
                var token = Json.Str(Json.Get(scope.Value, "key"));
                if (string.IsNullOrWhiteSpace(token)) continue;
                var signIn = new SignIn
                {
                    Token = token.Trim(),
                    ExpiresAt = Json.Time(Json.Get(scope.Value, "expires_at")),
                    IsTeam = string.Equals(Json.Str(Json.Get(scope.Value, "principal_type"))?.Trim(), "team", StringComparison.OrdinalIgnoreCase),
                };
                if (scope.Key.StartsWith("https://auth.x.ai::", StringComparison.Ordinal)) oidc = signIn;
                else if (scope.Key.Contains("/sign-in")) session = signIn;
            }
            return oidc ?? session;
        }

        // SuperGrok plans spend one weekly pool across Grok's products. creditUsagePercent is left out
        // when Grok doesn't say (SuperGrok Heavy, or an unused period), and then no bar is made up.
        static List<UsageWindow> ReadWindows(object root, DateTimeOffset now)
        {
            var config = Json.Get(root, "config");
            if (config == null) throw new UsageException(Text.FetchFailed);
            var periodEnd = Json.Time(Json.Get(config, "currentPeriod", "end"));
            var resetsAt = periodEnd ?? Json.Time(Json.Get(config, "billingPeriodEnd"));
            var start = periodEnd.HasValue ? Json.Time(Json.Get(config, "currentPeriod", "start")) : Json.Time(Json.Get(config, "billingPeriodStart"));
            var cap = Json.Num(Json.Get(config, "onDemandCap", "val"));
            var used = Json.Num(Json.Get(config, "onDemandUsed", "val"));
            var percent = Json.Num(Json.Get(config, "creditUsagePercent")) ?? (cap > 0 && used.HasValue ? used / cap * 100 : null);
            if (!percent.HasValue) throw new UsageException(resetsAt.HasValue ? Text.GrokNoPercent : Text.FetchFailed);

            var length = start <= now && resetsAt > start ? resetsAt.Value - start.Value : (TimeSpan?)null;
            string label;
            if (length.HasValue && Math.Abs(length.Value.TotalDays - 7) < 0.1) label = "1w";
            else if (length.HasValue && length.Value.TotalDays >= 28 && length.Value.TotalDays <= 31.1) label = Text.MonthLabel;
            else if (length.HasValue) label = Text.WindowLabel(length.Value);
            else label = Text.CreditsLabel;
            return new List<UsageWindow>
            {
                new UsageWindow
                {
                    Label = label,
                    Length = length ?? TimeSpan.FromDays(7),
                    UsedPercent = Math.Max(0, Math.Min(100, percent.Value)),
                    ResetsAt = resetsAt,
                    FixedReset = resetsAt.HasValue,
                },
            };
        }
    }
}
