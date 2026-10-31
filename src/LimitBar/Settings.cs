using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace LimitBar
{
    static class Settings
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LimitBar");
        static readonly string File = Path.Combine(Dir, "settings.json");

        public static readonly int[] RefreshChoices = { 1, 5, 15 };

        static readonly HashSet<string> Hidden = new HashSet<string>();
        static readonly HashSet<string> Known = new HashSet<string>();

        public static int RefreshMinutes { get; private set; } = 5;

        public static bool AntigravityClaudeGpt { get; private set; }

        public static string Language { get; private set; }

        public static event Action Changed;

        static Settings() => Load();

        public static bool IsShown(string serviceId) => !Hidden.Contains(serviceId);

        // A service seen for the first time starts out shown only if this PC is set up for it.
        public static void Introduce(IReadOnlyList<IUsageProvider> providers)
        {
            var fresh = providers.Where(p => !Known.Contains(p.Id)).ToList();
            if (fresh.Count == 0) return;
            foreach (var provider in fresh)
            {
                Known.Add(provider.Id);
                if (!provider.IsSetUp) Hidden.Add(provider.Id);
            }
            if (providers.Count > 0 && !providers.Any(p => IsShown(p.Id))) Hidden.Remove(providers[0].Id);
            Save();
        }

        public static void ToggleShown(string serviceId, IEnumerable<string> allIds)
        {
            if (IsShown(serviceId))
            {
                if (allIds.Count(IsShown) <= 1) return;
                Hidden.Add(serviceId);
            }
            else
            {
                Hidden.Remove(serviceId);
            }
            SaveAndNotify();
        }

        public static void SetRefreshMinutes(int minutes)
        {
            if (minutes == RefreshMinutes || !RefreshChoices.Contains(minutes)) return;
            RefreshMinutes = minutes;
            SaveAndNotify();
        }

        /// <summary>A language code, or null to follow Windows.</summary>
        public static void SetLanguage(string code)
        {
            if (string.IsNullOrEmpty(code)) code = null;
            if (code == Language) return;
            Language = code;
            SaveAndNotify();
        }

        public static void SetAntigravityClaudeGpt(bool claudeGpt)
        {
            if (claudeGpt == AntigravityClaudeGpt) return;
            AntigravityClaudeGpt = claudeGpt;
            SaveAndNotify();
        }

        static void SaveAndNotify()
        {
            Save();
            Changed?.Invoke();
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var data = new Dictionary<string, object>
                {
                    ["refreshMinutes"] = RefreshMinutes,
                    ["antigravityGroup"] = AntigravityClaudeGpt ? "claude-gpt" : "gemini",
                    ["hidden"] = Hidden.ToArray(),
                    ["known"] = Known.ToArray(),
                };
                if (!string.IsNullOrEmpty(Language)) data["language"] = Language;
                System.IO.File.WriteAllText(File, new JavaScriptSerializer().Serialize(data));
            }
            catch (Exception ex)
            {
                Log.Error("settings", ex.Message);
            }
        }

        static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return;
                var root = Json.Parse(System.IO.File.ReadAllText(File));
                var minutes = Json.Num(Json.Get(root, "refreshMinutes"));
                if (minutes.HasValue && RefreshChoices.Contains((int)minutes.Value)) RefreshMinutes = (int)minutes.Value;
                AntigravityClaudeGpt = Json.Str(Json.Get(root, "antigravityGroup")) == "claude-gpt";
                Language = Json.Str(Json.Get(root, "language"));
                foreach (var id in Json.Arr(Json.Get(root, "hidden")).OfType<string>()) Hidden.Add(id);
                foreach (var id in Json.Arr(Json.Get(root, "known")).OfType<string>()) Known.Add(id);
            }
            catch (Exception ex)
            {
                Log.Error("settings", ex.Message);
            }
        }
    }
}
