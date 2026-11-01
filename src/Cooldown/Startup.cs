using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace Cooldown
{
    /// <summary>
    /// "Start with Windows", as a sign-in task in Task Scheduler. Windows starts the programs in the Run key
    /// one after another well after sign-in (a minute or more on a busy PC); a sign-in task starts at once.
    /// The Run key stays as the fallback where Task Scheduler refuses.
    /// </summary>
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Cooldown";

        static string ExePath => Process.GetCurrentProcess().MainModule.FileName;
        static string RunCommand => "\"" + ExePath + "\"";

        public static bool Enabled
        {
            get => string.Equals(TaskExe(), ExePath, StringComparison.OrdinalIgnoreCase) || RunValue() == RunCommand;
            set
            {
                if (value)
                {
                    try
                    {
                        RegisterTask();
                        SetRunValue(false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Info("startup", "task: " + ex.Message);
                    }
                    SetRunValue(true);
                }
                else
                {
                    DeleteTask();
                    SetRunValue(false);
                }
            }
        }

        /// <summary>Before 0.2.1 this was the Run key; one already set that way moves to the task.</summary>
        public static void MoveFromRunKey()
        {
            try
            {
                if (RunValue() == RunCommand) Enabled = true;
            }
            catch (Exception ex)
            {
                Log.Error("startup", ex.Message);
            }
        }

        static string RunValue()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return key?.GetValue(Name) as string;
            }
        }

        static void SetRunValue(bool on)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) key.SetValue(Name, RunCommand);
                else key.DeleteValue(Name, false);
            }
        }

        static dynamic TaskFolder()
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            service.Connect();
            return service.GetFolder("\\");
        }

        // The program the task starts, or null if there's no task (or it's someone else's).
        static string TaskExe()
        {
            try
            {
                dynamic task = TaskFolder().GetTask(Name);
                if (!task.Enabled) return null;
                foreach (dynamic action in task.Definition.Actions)
                {
                    if (action.Type == 0) return Environment.ExpandEnvironmentVariables(((string)action.Path).Trim('"'));
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        static void RegisterTask()
        {
            var user = WindowsIdentity.GetCurrent().User.Value;
            var exe = SecurityElement.Escape(ExePath);
            var dir = SecurityElement.Escape(Path.GetDirectoryName(ExePath));
            // No time limit, and not held back or stopped on battery: the defaults would end it after three days.
            var xml = $@"<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Starts Cooldown when you sign in.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><UserId>{user}</UserId></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{exe}</Command><WorkingDirectory>{dir}</WorkingDirectory></Exec></Actions>
</Task>";
            const int CreateOrUpdate = 6, InteractiveToken = 3;
            TaskFolder().RegisterTask(Name, xml, CreateOrUpdate, null, null, InteractiveToken, null);
        }

        static void DeleteTask()
        {
            try
            {
                TaskFolder().DeleteTask(Name, 0);
            }
            catch (Exception)
            {
            }
        }
    }
}
