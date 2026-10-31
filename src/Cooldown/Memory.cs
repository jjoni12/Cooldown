using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Cooldown
{
    static class Memory
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        static DispatcherTimer _timer;

        public static void TrimSoon(TimeSpan delay)
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
                _timer.Tick += (s, e) =>
                {
                    _timer.Stop();
                    Trim();
                };
            }
            _timer.Stop();
            _timer.Interval = delay;
            _timer.Start();
        }

        public static void Cancel() => _timer?.Stop();

        static void Trim()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
    }
}
