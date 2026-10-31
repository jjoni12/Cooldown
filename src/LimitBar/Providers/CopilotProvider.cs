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
                if (result.Stderr.IndexOf("error connecting", StringComparison.OrdinalIgnoreCase) >= 0) throw new UsageException(Text.CantConnect(Name), transient: true);
                throw new UsageException(Text.CommandBadOutput(Name));
            }
            return new ServiceUsage { Id = Id, Name = Name, Windows = ReadWindows(root) };
        }

        static List<UsageWindow> ReadWindows(object root)
        {
            if (Json.Str(Json.Get(root, "access_type_sku")) == "subscription_ended") throw new UsageException(Text.CopilotEnded);

            var resetsAt = Json.Time(Json.Get(root, "quota_reset_date_utc")) ?? Json.Time(Json.Get(root, "quota_reset_date"))
                ?? Json.Time(Json.Get(root, "limited_user_reset_date"));
            var windows = new List<UsageWindow>();
            var unlimited = false;
            foreach (var (key, label) in Quotas)
            {
                var quota = Json.Get(root, "quota_snapshots", key);
                if (Json.Get(quota, "unlimited") is true)
                {
                    unlimited = true;
                    continue;
                }
                // Older answers (Copilot Free) count chat and completions in monthly_quotas / limited_user_quotas.
                var left = PercentLeft(quota) ?? PercentLeft(Json.Num(Json.Get(root, "monthly_quotas", key)), Json.Num(Json.Get(root, "limited_user_quotas", key)));
                if (!left.HasValue) continue;

                windows.Add(new UsageWindow
                {
                    Label = label,
                    Length = UsageWindow.MonthEndingAt(resetsAt),
                    UsedPercent = Math.Max(0, Math.Min(100, 100 - left.Value)),
                    ResetsAt = resetsAt,
                    FixedReset = true,
                });
            }
            if (windows.Count == 0)
            {
                // Plans billed in AI credits (since June 2026) get placeholder quotas, and GitHub
                // publishes no credit allowance to measure against; report what was spent.
                var credits = Json.Num(Json.Get(root, "quota_snapshots", "premium_interactions", "credits_used"))
                    ?? Json.Num(Json.Get(root, "quota_snapshots", "chat", "credits_used"));
                if (credits.HasValue && (Json.Get(root, "token_based_billing") is true || unlimited || credits > 0)) throw new UsageException(Text.CopilotCreditsUsed(credits.Value));
                throw new UsageException(Text.NoLimits);
            }
            return windows;
        }

        // Seats billed in AI credits get placeholder quotas with nothing to measure (entitlement 0, remaining 0).
        static double? PercentLeft(object quota)
        {
            if (quota == null) return null;
            var entitlement = Json.Num(Json.Get(quota, "entitlement"));
            var remaining = Json.Num(Json.Get(quota, "remaining")) ?? Json.Num(Json.Get(quota, "quota_remaining"));
            if (entitlement == 0 && (remaining ?? 0) == 0) return null;
            return Json.Num(Json.Get(quota, "percent_remaining")) ?? PercentLeft(entitlement, remaining);
        }

        static double? PercentLeft(double? entitlement, double? remaining) =>
            entitlement > 0 && remaining.HasValue ? Math.Max(0, remaining.Value) / entitlement.Value * 100 : (double?)null;
    }
}
