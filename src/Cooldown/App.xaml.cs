using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using Cooldown.Providers;

namespace Cooldown
{
    public partial class App : Application
    {
        Mutex _single;
        bool _ownsMutex;
        UsageStore _store;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _single = new Mutex(true, "Cooldown.SingleInstance", out _ownsMutex);
            if (!_ownsMutex)
            {
                Shutdown();
                return;
            }
            FromLimitBar.Run();
            Start();
        }

        // Kept out of OnStartup: preparing a method can already set up the classes it uses, and with
        // them the settings, which have to wait until FromLimitBar has copied them over.
        [MethodImpl(MethodImplOptions.NoInlining)]
        void Start()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Log.Error("ui", args.Exception);
                args.Handled = true;
            };
            // Off the UI thread an exception still ends the process; at least leave a trace of why.
            AppDomain.CurrentDomain.UnhandledException += (s, args) => Log.Error("crash", args.ExceptionObject.ToString());

            Resources["TextFont"] = Visuals.UiFont;
            Text.Changed += () =>
            {
                Visuals.UpdateFont();
                Resources["TextFont"] = Visuals.UiFont;
            };

            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            var providers = ProviderRegistry.Create();
            Settings.Introduce(providers);
            _store = new UsageStore(providers);

            var flyout = new FlyoutWindow(_store);
            var chip = new TaskbarChip(_store, flyout);
            flyout.Park();
            chip.Start();
            _store.Start();
            System.Threading.Tasks.Task.Run(() => Cooldown.Startup.MoveFromRunKey());
            Memory.TrimSoon(TimeSpan.FromSeconds(20));
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _store?.Stop();
            if (_ownsMutex) _single.ReleaseMutex();
            _single?.Dispose();
            base.OnExit(e);
        }
    }
}
