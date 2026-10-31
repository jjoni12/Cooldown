using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Cooldown
{
    /// <summary>Cooldown was called LimitBar before 0.2.0; on start it takes over from an older install.</summary>
    static class FromLimitBar
    {
        const string OldName = "LimitBar";

        public static void Run()
        {
            Try("process", CloseOldApp);
            Try("settings", CopySettings);
            Try("startup", TakeOverStartup);
        }

        // Both would sit on the taskbar otherwise. The old one has nothing unsaved.
        static void CloseOldApp()
        {
            foreach (var process in Process.GetProcessesByName(OldName))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileVersionInfo.ProductName != OldName) continue;
                        process.Kill();
                        process.WaitForExit(3000);
                        Log.Info("migrate", "closed " + OldName);
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                    {
                    }
                }
            }
        }

        // Settings, language files, logos and services.json, once, before Cooldown has a folder of its own.
        static void CopySettings()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var from = Path.Combine(appData, OldName);
            var to = Path.Combine(appData, "Cooldown");
            if (!Directory.Exists(from) || Directory.Exists(to)) return;

            // Copied beside and moved into place, so a half-done copy is never taken for a finished one.
            var temp = to + ".copying";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(temp, file.Substring(from.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }
            Directory.CreateDirectory(temp);
            Directory.Move(temp, to);
            Log.Info("migrate", "copied settings from " + OldName);
        }

        // Whoever had LimitBar start with Windows gets Cooldown starting with it instead.
        static void TakeOverStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key?.GetValue(OldName) == null) return;
                key.DeleteValue(OldName, false);
            }
            Startup.Enabled = true;
            Log.Info("migrate", "start with Windows moved from " + OldName);
        }

        static void Try(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("migrate", $"{step}: {ex.Message}");
            }
        }
    }
}
