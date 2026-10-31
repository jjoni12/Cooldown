using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar
{
    static class Cli
    {
        public sealed class Result
        {
            public int ExitCode;
            public string Stdout = "";
            public string Stderr = "";
            public bool TimedOut;
        }

        public static string Find(string name, params string[] fallbacks)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                var clean = dir.Trim().Trim('"');
                if (clean.Length == 0) continue;
                foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
                {
                    try
                    {
                        var candidate = Path.Combine(clean, name + ext);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }
            foreach (var fallback in fallbacks)
            {
                var candidate = Environment.ExpandEnvironmentVariables(fallback);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        public static Task<Result> RunAsync(string exe, string args, TimeSpan timeout,
            string workDir = null, IDictionary<string, string> env = null, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var isScript = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
                var psi = new ProcessStartInfo
                {
                    FileName = isScript ? "cmd.exe" : exe,
                    Arguments = isScript ? $"/d /s /c \"\"{exe}\" {args}\"" : args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = workDir ?? Path.GetTempPath(),
                };
                if (env != null)
                {
                    foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
                }

                using (var job = new KillOnCloseJob())
                using (var p = Process.Start(psi))
                {
                    job.Add(p);
                    try
                    {
                        p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
                    {
                    }
                    p.StandardInput.Close();
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();

                    var deadline = DateTime.UtcNow + timeout;
                    while (!p.WaitForExit(250))
                    {
                        if (ct.IsCancellationRequested || DateTime.UtcNow > deadline)
                        {
                            job.Dispose();
                            return new Result { ExitCode = -1, TimedOut = true };
                        }
                    }

                    // Children that keep the pipe open would block ReadToEnd; closing the job ends them.
                    Task.WaitAny(new Task[] { stdout }, TimeSpan.FromSeconds(3));
                    job.Dispose();
                    Task.WaitAll(new Task[] { stdout, stderr }, TimeSpan.FromSeconds(3));
                    return new Result
                    {
                        ExitCode = p.ExitCode,
                        Stdout = stdout.Status == TaskStatus.RanToCompletion ? stdout.Result : "",
                        Stderr = stderr.Status == TaskStatus.RanToCompletion ? stderr.Result : "",
                    };
                }
            }, ct);
        }

        sealed class KillOnCloseJob : IDisposable
        {
            [StructLayout(LayoutKind.Sequential)]
            struct BasicLimits
            {
                public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass, SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct IoCounters
            {
                public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
                public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct ExtendedLimits
            {
                public BasicLimits Basic;
                public IoCounters Io;
                public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
            }

            const int JobObjectExtendedLimitInformation = 9;
            const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
            [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int size);
            [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

            IntPtr _handle;

            public KillOnCloseJob()
            {
                _handle = CreateJobObject(IntPtr.Zero, null);
                if (_handle == IntPtr.Zero) throw new Win32Exception();
                var info = new ExtendedLimits { Basic = { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
                SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(typeof(ExtendedLimits)));
            }

            public void Add(Process p)
            {
                if (_handle != IntPtr.Zero) AssignProcessToJobObject(_handle, p.Handle);
            }

            public void Dispose()
            {
                if (_handle == IntPtr.Zero) return;
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
