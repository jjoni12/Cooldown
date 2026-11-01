using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cooldown.Providers
{
    /// <summary>
    /// Claude Code's sign-in, read from its own file. An expired one is renewed the way Claude Code renews it:
    /// same address, same app id, under the same locks, and saved back to the same file, so Claude Code
    /// and Cooldown keep sharing one sign-in. Nothing else in the file is touched.
    /// </summary>
    sealed class ClaudeLogin
    {
        const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
        const string ClaudeCodeAppId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        // Claude Code treats a lock untouched this long as left behind by a process that died.
        static readonly TimeSpan StaleLock = TimeSpan.FromSeconds(60);
        static readonly SemaphoreSlim Renewing = new SemaphoreSlim(1, 1);

        public string AccessToken { get; private set; }
        string _refreshToken;
        DateTimeOffset _expiresAt;
        string[] _scopes;

        public bool ExpiresSoon => _expiresAt < DateTimeOffset.Now.AddMinutes(2);

        public static string ConfigDir
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                return string.IsNullOrWhiteSpace(custom)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                    : custom;
            }
        }

        static string FilePath => Path.Combine(ConfigDir, ".credentials.json");

        public static bool Exists => File.Exists(FilePath);

        public static ClaudeLogin Read()
        {
            if (!File.Exists(FilePath)) return null;
            var oauth = Json.Get(Json.Parse(File.ReadAllText(FilePath)), "claudeAiOauth");
            var token = Json.Str(Json.Get(oauth, "accessToken"));
            if (string.IsNullOrEmpty(token)) return null;
            var expires = Json.Num(Json.Get(oauth, "expiresAt"));
            return new ClaudeLogin
            {
                AccessToken = token,
                _refreshToken = Json.Str(Json.Get(oauth, "refreshToken")),
                _expiresAt = expires.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)expires.Value) : DateTimeOffset.MinValue,
                _scopes = Json.Arr(Json.Get(oauth, "scopes")).OfType<string>().ToArray(),
            };
        }

        /// <summary>A fresh sign-in, or null if it can't be renewed without signing in again.</summary>
        public async Task<ClaudeLogin> RenewAsync(CancellationToken ct)
        {
            await Renewing.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using (await ClaudeCodeLocks.TakeAsync(ConfigDir, ct).ConfigureAwait(false))
                {
                    // Claude Code may have renewed it while we waited.
                    var current = Read();
                    if (current == null) return null;
                    if (current.AccessToken != AccessToken && !current.ExpiresSoon) return current;
                    if (string.IsNullOrEmpty(current._refreshToken)) return null;

                    var request = new Dictionary<string, object>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = current._refreshToken,
                        ["client_id"] = ClaudeCodeAppId,
                    };
                    if (current._scopes.Length > 0) request["scope"] = string.Join(" ", current._scopes);
                    var (status, body) = await Http.PostJsonAsync(TokenUrl, new Dictionary<string, string>(), Json.Write(request), ct).ConfigureAwait(false);
                    // 400 and 401 mean the sign-in itself was revoked or replaced.
                    if (status == 400 || status == 401) return null;
                    if (status != 200) throw UsageException.Server("Claude", status);

                    var answer = Json.Parse(body);
                    var token = Json.Str(Json.Get(answer, "access_token"));
                    var lifetime = Json.Num(Json.Get(answer, "expires_in"));
                    if (string.IsNullOrEmpty(token) || !lifetime.HasValue) throw UsageException.Server("Claude", status);
                    Save(answer, token, lifetime.Value);
                    return Read();
                }
            }
            finally
            {
                Renewing.Release();
            }
        }

        // Read again right before writing, so anything Claude Code changed meanwhile is kept.
        static void Save(object answer, string token, double lifetimeSeconds)
        {
            var root = (IDictionary<string, object>)Json.Parse(File.ReadAllText(FilePath));
            var oauth = (IDictionary<string, object>)root["claudeAiOauth"];
            var now = DateTimeOffset.UtcNow;
            oauth["accessToken"] = token;
            oauth["expiresAt"] = now.AddSeconds(lifetimeSeconds).ToUnixTimeMilliseconds();
            if (Json.Str(Json.Get(answer, "refresh_token")) is string refresh && refresh.Length > 0) oauth["refreshToken"] = refresh;
            if (Json.Num(Json.Get(answer, "refresh_token_expires_in")) is double refreshLifetime)
                oauth["refreshTokenExpiresAt"] = now.AddSeconds(refreshLifetime).ToUnixTimeMilliseconds();
            if (Json.Str(Json.Get(answer, "scope")) is string scope && scope.Length > 0)
                oauth["scopes"] = scope.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var temp = FilePath + ".cooldown";
            File.WriteAllText(temp, Json.Write(root));
            // The old refresh token no longer works, so this write has to land: a reader holding the
            // file open for a moment only delays it.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Replace(temp, FilePath, null);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(100);
                }
            }
            Log.Info("claude", "renewed the Claude Code sign-in");
        }

        /// <summary>The two lock folders Claude Code holds while it renews (made with mkdir, so taking one is atomic).</summary>
        sealed class ClaudeCodeLocks : IDisposable
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern bool CreateDirectory(string path, IntPtr security);

            const int ErrorAlreadyExists = 183;
            readonly List<string> _held = new List<string>();

            public static async Task<ClaudeCodeLocks> TakeAsync(string configDir, CancellationToken ct)
            {
                var dir = configDir.TrimEnd('\\', '/');
                var locks = new ClaudeCodeLocks();
                try
                {
                    foreach (var path in new[] { Path.Combine(dir, ".oauth_refresh.lock"), dir + ".lock" })
                    {
                        // Claude Code renews in a second or two; past about 20 seconds it's left to the next round.
                        for (var attempt = 0; !locks.TryTake(path); attempt++)
                        {
                            if (attempt >= 40) throw new UsageException(Text.FetchFailed, transient: true);
                            await Task.Delay(500, ct).ConfigureAwait(false);
                        }
                    }
                    return locks;
                }
                catch
                {
                    locks.Dispose();
                    throw;
                }
            }

            bool TryTake(string path)
            {
                if (CreateDirectory(path, IntPtr.Zero))
                {
                    _held.Add(path);
                    return true;
                }
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorAlreadyExists) throw new IOException("lock: " + new Win32Exception(error).Message);
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(path) > StaleLock)
                {
                    try
                    {
                        Directory.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
                return false;
            }

            public void Dispose()
            {
                foreach (var path in _held)
                {
                    try
                    {
                        Directory.Delete(path);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
                _held.Clear();
            }
        }
    }
}
