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

            var (status, body) = await Http.GetAsync(UsageUrl, new Dictionary<string, string>
            {
                ["Cookie"] = $"WorkosCursorSessionToken={userId}%3A%3A{token}",
                ["Origin"] = "https://cursor.com",
                ["Referer"] = "https://cursor.com/dashboard",
                ["Accept"] = "application/json",
            }, ct);
            if (status == 401 || status == 403) throw new UsageException(Text.CursorSignIn);
            if (status != 200) throw UsageException.Server(Name, status);

            var root = Json.Parse(body);
            var used = Json.Num(Json.Get(root, "individualUsage", "plan", "totalPercentUsed"))
                ?? PercentFromMessage(Json.Str(Json.Get(root, "autoModelSelectedDisplayMessage")))
                ?? PercentFromMessage(Json.Str(Json.Get(root, "namedModelSelectedDisplayMessage")));
            if (!used.HasValue) throw new UsageException(Json.Get(root, "isUnlimited") is true ? Text.NoLimits : Text.FetchFailed);

            var start = Json.Time(Json.Get(root, "billingCycleStart"));
            var end = Json.Time(Json.Get(root, "billingCycleEnd"));
            var length = start.HasValue && end > start ? end.Value - start.Value : UsageWindow.MonthEndingAt(end);
            var usage = new ServiceUsage { Id = Id, Name = Name };
            usage.Windows.Add(new UsageWindow
            {
                // Newer plans refill a weekly pool; older and free plans a monthly one.
                Label = length.TotalDays < 10 ? "1w" : Text.MonthLabel,
                Length = length,
                UsedPercent = Math.Max(0, Math.Min(100, used.Value)),
                ResetsAt = end,
                FixedReset = true,
            });
            return usage;
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
