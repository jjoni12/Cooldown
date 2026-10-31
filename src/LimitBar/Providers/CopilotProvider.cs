using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar.Providers
{
    public sealed class CopilotProvider : IUsageProvider
    {
        static readonly (string Key, string Label)[] Quotas =
        {
            ("premium_interactions", Text.PremiumLabel),
            ("chat", Text.ChatLabel),
            ("completions", Text.CompletionsLabel),
        };

        public string Id => "copilot";
        public string Name => "Copilot";
        public bool IsSetUp => FindGh() != null;

        static string FindGh() => Cli.Find("gh", @"%ProgramFiles%\GitHub CLI\gh.exe");

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var exe = FindGh();
            if (exe == null) throw new UsageException(Text.GhMissing);

            var env = new Dictionary<string, string> { ["GH_NO_UPDATE_NOTIFIER"] = "1", ["GH_PROMPT_DISABLED"] = "1" };
            var result = await Cli.RunAsync(exe, "api /copilot_internal/user", TimeSpan.FromSeconds(30), env: env, ct: ct);
            if (result.TimedOut) throw new UsageException(Text.CommandTimedOut(Name));
            if (result.ExitCode == 4) throw new UsageException(Text.GhSignIn); // gh's "not signed in" exit code

            var json = Json.ExtractObject(result.Stdout);
            var root = json == null ? null : Json.Parse(json);
            if (result.ExitCode != 0 || root == null)
            {
                var message = Json.Str(Json.Get(root, "message")) ?? "";
                if (message.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) >= 0) throw new UsageException(Text.CopilotNone);
                if (message.IndexOf("credentials", StringComparison.OrdinalIgnoreCase) >= 0) throw new UsageException(Text.GhSignIn);
                throw new UsageException(Text.CommandBadOutput(Name));
            }

            var resetsAt = Json.Time(Json.Get(root, "quota_reset_date_utc")) ?? Json.Time(Json.Get(root, "quota_reset_date"));
            var usage = new ServiceUsage { Id = Id, Name = Name };
            foreach (var (key, label) in Quotas)
            {
                var quota = Json.Get(root, "quota_snapshots", key);
                var entitlement = Json.Num(Json.Get(quota, "entitlement")) ?? 0;
                if (quota == null || Json.Get(quota, "unlimited") is true || entitlement <= 0) continue;

                var remaining = Json.Num(Json.Get(quota, "percent_remaining"));
                if (!remaining.HasValue)
                {
                    var left = Json.Num(Json.Get(quota, "remaining")) ?? Json.Num(Json.Get(quota, "quota_remaining"));
                    if (left.HasValue) remaining = left.Value / entitlement * 100;
                }
                if (!remaining.HasValue) continue;

                usage.Windows.Add(new UsageWindow
                {
                    Label = label,
                    Length = UsageWindow.MonthEndingAt(resetsAt),
                    UsedPercent = Math.Max(0, Math.Min(100, 100 - remaining.Value)),
                    ResetsAt = resetsAt,
                    FixedReset = true,
                });
            }
            if (usage.Windows.Count == 0) throw new UsageException(Text.NoLimits);
            return usage;
        }
    }
}
