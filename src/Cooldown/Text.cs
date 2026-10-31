using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Cooldown
{
    static class Text
    {
        static readonly string UserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cooldown", "lang");

        static Dictionary<string, string> Strings = new Dictionary<string, string>();
        static string _chosen;

        public static string Language { get; private set; } = "en";

        /// <summary>Raised on the UI thread after the language changed in settings.</summary>
        public static event Action Changed;

        // Built-in languages are embedded from Assets\lang\<code>.json; a file in %APPDATA%\Cooldown\lang
        // adds a language or overrides strings of one. Missing keys fall back to English.
        static Text()
        {
            Load();
            Cooldown.Settings.Changed += () =>
            {
                if (Cooldown.Settings.Language == _chosen) return;
                Load();
                Changed?.Invoke();
            };
        }

        static void Load()
        {
            _chosen = Cooldown.Settings.Language;
            var strings = Read("en") ?? new Dictionary<string, string>();
            var language = "en";
            foreach (var code in Candidates())
            {
                var found = Read(code);
                if (found == null) continue;
                foreach (var pair in found) strings[pair.Key] = pair.Value;
                language = code;
                break;
            }
            Strings = strings;
            Language = language;
        }

        /// <summary>Built-in languages and language files in %APPDATA%\Cooldown\lang, by their own names.</summary>
        public static IReadOnlyList<(string Code, string Name)> Available()
        {
            var codes = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .Where(n => n.StartsWith("lang.") && n.EndsWith(".json"))
                .Select(n => n.Substring(5, n.Length - 10))
                .ToList();
            if (Directory.Exists(UserFolder)) codes.AddRange(Directory.GetFiles(UserFolder, "*.json").Select(Path.GetFileNameWithoutExtension));
            return codes.Where(IsCode).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(code => (code, NameOf(code)))
                .OrderBy(l => l.Item2, StringComparer.Ordinal)
                .ToList();
        }

        static string NameOf(string code)
        {
            if (Read(code) is Dictionary<string, string> strings && strings.TryGetValue("LanguageName", out var name)) return name;
            try { return CultureInfo.GetCultureInfo(code).NativeName; } catch (CultureNotFoundException) { return code; }
        }

        public static string Refresh => Get(nameof(Refresh));
        public static string LanguageSetting => Get(nameof(LanguageSetting));
        public static string UseWindowsLanguage => Get(nameof(UseWindowsLanguage));
        public static string Settings => Get(nameof(Settings));
        public static string Back => Get(nameof(Back));
        public static string RunAtStartup => Get(nameof(RunAtStartup));
        public static string Quit => Get(nameof(Quit));
        public static string ShownServices => Get(nameof(ShownServices));
        public static string General => Get(nameof(General));
        public static string AntigravityGroup => Get(nameof(AntigravityGroup));
        public static string RefreshInterval => Get(nameof(RefreshInterval));
        public static string Loading => Get(nameof(Loading));
        public static string FetchFailed => Get(nameof(FetchFailed));

        public static string ClaudeSignIn => Get(nameof(ClaudeSignIn));
        public static string CodexSignIn => Get(nameof(CodexSignIn));
        public static string AgyMissing => Get(nameof(AgyMissing));
        public static string AgySignIn => Get(nameof(AgySignIn));
        public static string AgyTimeout => Get(nameof(AgyTimeout));
        public static string GhMissing => Get(nameof(GhMissing));
        public static string GhSignIn => Get(nameof(GhSignIn));
        public static string CopilotNone => Get(nameof(CopilotNone));
        public static string CopilotEnded => Get(nameof(CopilotEnded));
        public static string CursorSignIn => Get(nameof(CursorSignIn));
        public static string OpenCodeSignIn => Get(nameof(OpenCodeSignIn));
        public static string OpenCodeKeyRejected => Get(nameof(OpenCodeKeyRejected));
        public static string OpenCodeSessionExpired => Get(nameof(OpenCodeSessionExpired));
        public static string OpenCodeNoGo => Get(nameof(OpenCodeNoGo));
        public static string GrokSignIn => Get(nameof(GrokSignIn));
        public static string GrokExpired => Get(nameof(GrokExpired));
        public static string GrokNoPercent => Get(nameof(GrokNoPercent));
        public static string NoLimits => Get(nameof(NoLimits));
        public static string LimitsUnavailable => Get(nameof(LimitsUnavailable));

        public static string Minutes(int n) => Format(nameof(Minutes), n);
        public static string ServerError(string service, int status) => Format(nameof(ServerError), service, status);
        public static string RateLimited(string service, DateTimeOffset retryAt) =>
            Format(nameof(RateLimited), service, retryAt.LocalDateTime.ToString("t", CultureInfo.CurrentCulture));
        public static string CantConnect(string service) => Format(nameof(CantConnect), service);
        public static string CommandTimedOut(string service) => Format(nameof(CommandTimedOut), service);
        public static string CommandBadOutput(string service) => Format(nameof(CommandBadOutput), service);

        public const string GroupGemini = "Gemini";
        public const string GroupClaudeGpt = "Claude + GPT";

        public const string MonthLabel = "1mo";
        public const string PremiumLabel = "premium";
        public const string ChatLabel = "chat";
        public const string CompletionsLabel = "completions";
        public const string CreditsLabel = "credits";

        public static string CopilotCreditsUsed(double credits) =>
            Format(nameof(CopilotCreditsUsed), credits.ToString("0.#", CultureInfo.CurrentCulture));
        public static string CodexCreditsLeft(double credits) =>
            Format(nameof(CodexCreditsLeft), credits.ToString("0.#", CultureInfo.CurrentCulture));

        public static string WindowName(string label)
        {
            var scope = label.IndexOf('|');
            if (scope > 0) return WindowName(label.Substring(0, scope)) + " · " + label.Substring(scope + 1);
            switch (label)
            {
                case "5h": return Get("Window5h");
                case "1w": return Get("WindowWeekly");
                case MonthLabel: return Get("WindowMonthly");
                case PremiumLabel: return Get("CopilotPremium");
                case ChatLabel: return Get("CopilotChat");
                case CompletionsLabel: return Get("CopilotCompletions");
                case CreditsLabel: return Get("CreditLimit");
            }
            var match = Regex.Match(label, @"^(\d+)([dhm])$");
            if (!match.Success) return label;
            var n = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            switch (match.Groups[2].Value)
            {
                case "d": return Format("WindowDays", n);
                case "h": return Format("WindowHours", n);
                default: return Format("WindowMinutes", n);
            }
        }

        public static string Updated(DateTimeOffset? at)
        {
            if (!at.HasValue) return "";
            var ago = DateTimeOffset.Now - at.Value;
            if (ago.TotalMinutes < 1) return Get("UpdatedNow");
            if (ago.TotalHours < 1) return Format("UpdatedMinutesAgo", (int)ago.TotalMinutes);
            return Format("UpdatedHoursAgo", (int)ago.TotalHours);
        }

        public static string Remaining(DateTimeOffset? at) =>
            at.HasValue ? Duration((int)Math.Ceiling((at.Value - DateTimeOffset.Now).TotalMinutes)) : "—";

        public static string Duration(TimeSpan length) => Duration((int)Math.Round(length.TotalMinutes));

        static string Duration(int minutes)
        {
            if (minutes <= 0) return "0m";
            int d = minutes / 1440, h = minutes % 1440 / 60, m = minutes % 60;
            if (d > 0) return h > 0 ? $"{d}d {h}h" : $"{d}d";
            if (h > 0) return m > 0 ? $"{h}h {m}m" : $"{h}h";
            return $"{m}m";
        }

        public static string WindowLabel(TimeSpan length)
        {
            if (length.TotalDays >= 1 && Math.Abs(length.TotalDays % 1) < 1e-9)
                return length.TotalDays == 7 ? "1w" : $"{length.TotalDays:0}d";
            if (length.TotalHours >= 1 && Math.Abs(length.TotalHours % 1) < 1e-9)
                return $"{length.TotalHours:0}h";
            return $"{Math.Max(1, length.TotalMinutes):0}m";
        }

        static string Get(string key) => Strings.TryGetValue(key, out var value) ? value : key;

        static string Format(string key, params object[] args)
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, Get(key), args);
            }
            catch (FormatException)
            {
                return Get(key);
            }
        }

        /// <summary>The chosen language (COOLDOWN_LANG, then "language" in settings.json), else Windows' display language, most specific first.</summary>
        static IEnumerable<string> Candidates()
        {
            var chosen = Environment.GetEnvironmentVariable("COOLDOWN_LANG");
            if (string.IsNullOrWhiteSpace(chosen)) chosen = Cooldown.Settings.Language;
            var culture = CultureInfo.CurrentUICulture;
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                yield return chosen.Trim();
                try { culture = CultureInfo.GetCultureInfo(chosen.Trim()); } catch (CultureNotFoundException) { }
            }
            for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent) yield return c.Name;
            // Only one variant is built in for these.
            if (culture.TwoLetterISOLanguageName == "pt") yield return "pt-BR";
            if (culture.TwoLetterISOLanguageName == "zh") yield return "zh-Hans";
        }

        static bool IsCode(string code) => Regex.IsMatch(code, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$");

        static Dictionary<string, string> Read(string code)
        {
            if (!IsCode(code)) return null;
            Dictionary<string, string> result = null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"lang.{code}.json"))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream)) result = Merge(reader.ReadToEnd(), result, code);
                }
            }
            var file = Path.Combine(UserFolder, code + ".json");
            if (File.Exists(file)) result = Merge(File.ReadAllText(file), result, code);
            return result;
        }

        static Dictionary<string, string> Merge(string json, Dictionary<string, string> into, string code)
        {
            into = into ?? new Dictionary<string, string>();
            try
            {
                if (Json.Parse(json) is IDictionary<string, object> map)
                {
                    foreach (var pair in map.Where(p => p.Value is string)) into[pair.Key] = (string)pair.Value;
                }
            }
            catch (Exception ex)
            {
                Log.Error("lang", $"{code}: {ex.Message}");
            }
            return into;
        }
    }
}
