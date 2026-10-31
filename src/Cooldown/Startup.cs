using System.Diagnostics;
using Microsoft.Win32;

namespace Cooldown
{
    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Cooldown";

        static string Command => "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"";

        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Key))
                {
                    return key?.GetValue(Name) is string value && value == Command;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (value) key.SetValue(Name, Command);
                    else key.DeleteValue(Name, false);
                }
            }
        }
    }
}
