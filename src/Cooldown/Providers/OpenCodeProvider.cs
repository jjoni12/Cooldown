using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cooldown.Providers
{
    public sealed class OpenCodeProvider : IUsageProvider
    {
        const string DefaultOrigin = "https://opencode.ai";

        sealed class SignIn
        {
            public string Token;
            public string Origin = DefaultOrigin;
            public string WorkspaceId;
            public bool IsOAuth;
        }

        public string Id => "opencode";
        public string Name => "OpenCode Go";
        public bool IsSetUp => ReadSignIn() != null;

        static string DataDir
        {
            get
            {
                var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrWhiteSpace(data))
                    data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                return Path.Combine(data, "opencode");
            }
        }

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var signIn = ReadSignIn();
            if (signIn == null) throw new UsageException(Text.OpenCodeSignIn);

            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + signIn.Token,
                ["Accept"] = "application/json",
            };
            if (signIn.WorkspaceId != null) headers["x-opencode-org-id"] = signIn.WorkspaceId;
            var (status, body) = await Http.GetAsync(signIn.Origin + "/zen/go/v1/usage", headers, ct);

            if (status == 401) throw new UsageException(signIn.IsOAuth ? Text.OpenCodeSessionExpired : Text.OpenCodeKeyRejected);
            if (status == 403) throw new UsageException(Text.OpenCodeNoGo);
            if (status != 200) throw UsageException.Server(Name, status);

            var usage = new ServiceUsage { Id = Id, Name = Name, Windows = ReadWindows(Json.Parse(body)) };
            if (usage.Windows.Count == 0) throw new UsageException(Text.FetchFailed);
            return usage;
        }

        static List<UsageWindow> ReadWindows(object root)
        {
            var windows = new List<UsageWindow>();
            AddWindow(windows, Json.Get(root, "usage", "rolling"), _ => TimeSpan.FromHours(5));
            AddWindow(windows, Json.Get(root, "usage", "weekly"), _ => TimeSpan.FromDays(7));
            AddWindow(windows, Json.Get(root, "usage", "monthly"), UsageWindow.MonthEndingAt, Text.MonthLabel, fixedReset: true);
            return windows.OrderBy(w => w.Length).ToList();
        }

        static void AddWindow(List<UsageWindow> windows, object meter, Func<DateTimeOffset?, TimeSpan> length, string label = null, bool fixedReset = false)
        {
            var percent = Json.Num(Json.Get(meter, "percent"));
            if (!percent.HasValue) return;
            if (Json.Str(Json.Get(meter, "status")) == "rate-limited") percent = Math.Max(percent.Value, 100);
            var resetsAt = Json.Time(Json.Get(meter, "resetsAt"));
            var span = length(resetsAt);
            windows.Add(new UsageWindow
            {
                Label = label ?? Text.WindowLabel(span),
                Length = span,
                UsedPercent = Math.Max(0, Math.Min(100, percent.Value)),
                ResetsAt = resetsAt,
                FixedReset = fixedReset,
            });
        }

        static SignIn ReadSignIn()
        {
            var env = Environment.GetEnvironmentVariable("OPENCODE_API_KEY");
            if (!string.IsNullOrWhiteSpace(env)) return new SignIn { Token = env.Trim() };

            foreach (var integration in new[] { "opencode", "opencode-go" })
            {
                var signIn = FromStored(ReadStored(integration));
                if (signIn != null) return signIn;
            }

            try
            {
                var file = Path.Combine(DataDir, "auth.json");
                if (!File.Exists(file)) return null;
                var root = Json.Parse(File.ReadAllText(file));
                foreach (var entry in new[] { "opencode-go", "opencode" })
                {
                    var key = Json.Str(Json.Get(root, entry, "key"));
                    if (!string.IsNullOrWhiteSpace(key)) return new SignIn { Token = key.Trim() };
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                Log.Info("opencode", "can't read auth.json: " + ex.Message);
            }
            return null;
        }

        static object ReadStored(string integration)
        {
            var database = Path.Combine(DataDir, "opencode.db");
            if (!File.Exists(database)) return null;
            try
            {
                var value = Sqlite.ReadText(database,
                    "SELECT value FROM credential WHERE integration_id = ?1 ORDER BY active DESC, time_created DESC, id DESC LIMIT 1",
                    integration);
                return value == null ? null : Json.Parse(value);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is DllNotFoundException || ex is ArgumentException)
            {
                Log.Info("opencode", "can't read opencode.db: " + ex.Message);
                return null;
            }
        }

        static SignIn FromStored(object value)
        {
            var type = Json.Str(Json.Get(value, "type"));
            var token = Json.Str(Json.Get(value, type == "oauth" ? "access" : "key"));
            if ((type != "oauth" && type != "key") || string.IsNullOrWhiteSpace(token)) return null;
            return new SignIn
            {
                Token = token.Trim(),
                IsOAuth = type == "oauth",
                WorkspaceId = Json.Str(Json.Get(value, "metadata", "orgID")),
                Origin = ConsoleOrigin(Json.Str(Json.Get(value, "metadata", "server"))),
            };
        }

        static string ConsoleOrigin(string server)
        {
            if (string.IsNullOrEmpty(server) || !Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.Host.Equals("console.opencode.ai", StringComparison.OrdinalIgnoreCase))
                return DefaultOrigin;
            return uri.GetLeftPart(UriPartial.Authority);
        }
    }
}
