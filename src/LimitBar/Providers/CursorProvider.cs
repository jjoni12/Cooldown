using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar.Providers
{
    public sealed class CursorProvider : IUsageProvider
    {
        const string UsageUrl = "https://cursor.com/api/usage-summary";
        const string WeeklyUrl = "https://cursor.com/api/dashboard/get-sand-usage-status";

        static readonly string Database = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb");

        public string Id => "cursor";
        public string Name => "Cursor";
        public bool IsSetUp => File.Exists(Database);

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            if (!File.Exists(Database)) throw new UsageException(Text.CursorSignIn);
            var token = Sqlite.ReadText(Database, "SELECT value FROM ItemTable WHERE key = ?1", "cursorAuth/accessToken");
            var userId = string.IsNullOrEmpty(token) ? null : UserId(token);
            if (userId == null) throw new UsageException(Text.CursorSignIn);

            var headers = new Dictionary<string, string>
            {
                ["Cookie"] = $"WorkosCursorSessionToken={userId}%3A%3A{token}",
                ["Origin"] = "https://cursor.com",
                ["Referer"] = "https://cursor.com/dashboard",
                ["Accept"] = "application/json",
            };
            var (status, body) = await Http.GetAsync(UsageUrl, headers, ct);
            if (status == 401 || status == 403) throw new UsageException(Text.CursorSignIn);
            if (status != 200) throw UsageException.Server(Name, status);

            var weekly = await WeeklyPool(headers, ct);
            if (weekly != null) return new ServiceUsage { Id = Id, Name = Name, Windows = { weekly } };

            var root = Json.Parse(body);
            var used = Json.Num(Json.Get(root, "individualUsage", "plan", "totalPercentUsed"))
                ?? PercentFromMessage(Json.Str(Json.Get(root, "autoModelSelectedDisplayMessage")))
                ?? PercentFromMessage(Json.Str(Json.Get(root, "namedModelSelectedDisplayMessage")));
            if (!used.HasValue) throw new UsageException(Json.Get(root, "isUnlimited") is true ? Text.NoLimits : Text.FetchFailed);

            var start = Json.Time(Json.Get(root, "billingCycleStart"));
            var end = Json.Time(Json.Get(root, "billingCycleEnd"));
            var usage = new ServiceUsage { Id = Id, Name = Name };
            usage.Windows.Add(new UsageWindow
            {
                Label = Text.MonthLabel,
                Length = start.HasValue && end > start ? end.Value - start.Value : UsageWindow.MonthEndingAt(end),
                UsedPercent = Math.Max(0, Math.Min(100, used.Value)),
                ResetsAt = end,
                FixedReset = true,
            });
            return usage;
        }

        // The unified Cursor + Grok plans spend one weekly pool, reported apart from the monthly billing cycle.
        static async Task<UsageWindow> WeeklyPool(Dictionary<string, string> headers, CancellationToken ct)
        {
            try
            {
                var (status, body) = await Http.PostJsonAsync(WeeklyUrl, headers, "{}", ct);
                return status == 200 ? ParseWeeklyPool(body) : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException)
            {
                return null;
            }
        }

        static UsageWindow ParseWeeklyPool(string body)
        {
            var root = Json.Parse(body);
            var used = Json.Num(Json.Get(root, "usagePercent"));
            var resetsAt = Json.Time(Json.Get(root, "nextResetTimestampUtc"));
            if (!(Json.Get(root, "isUnified") is true) || Json.Get(root, "includedLimitZero") is true
                || !used.HasValue || !(resetsAt > DateTimeOffset.Now)) return null;
            return new UsageWindow
            {
                Label = "1w",
                Length = TimeSpan.FromDays(7),
                UsedPercent = Math.Max(0, Math.Min(100, used.Value)),
                ResetsAt = resetsAt,
                FixedReset = true,
            };
        }

        static string UserId(string jwt)
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload += new string('=', (4 - payload.Length % 4) % 4);
                var subject = Json.Str(Json.Get(Json.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))), "sub"));
                return subject?.Substring(subject.LastIndexOf('|') + 1);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        static double? PercentFromMessage(string message)
        {
            var match = message == null ? Match.Empty : Regex.Match(message, @"(\d+(?:\.\d+)?)\s*%");
            return match.Success ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : (double?)null;
        }
    }
}
