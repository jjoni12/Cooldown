using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LimitBar
{
    public sealed class UsageStore
    {
        static readonly TimeSpan ErrorBackoff = TimeSpan.FromMinutes(15);
        static readonly TimeSpan AfterReset = TimeSpan.FromSeconds(30);
        // The Claude endpoint answers 429 when asked too often.
        static readonly TimeSpan MinGap = TimeSpan.FromSeconds(30);
        // After a 429 a service is left alone this long (or as long as it asks); longer if it keeps
        // answering 429, so a 1-minute refresh interval doesn't run into the limit every time.
        static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(1);
        static readonly TimeSpan RepeatedRateLimitPause = TimeSpan.FromMinutes(2);

        readonly IUsageProvider[] _providers;
        readonly Dictionary<string, ServiceUsage> _latest = new Dictionary<string, ServiceUsage>();
        readonly Dictionary<string, DateTimeOffset> _due = new Dictionary<string, DateTimeOffset>();
        readonly Dictionary<string, DateTimeOffset> _lastAttempt = new Dictionary<string, DateTimeOffset>();
        readonly Dictionary<string, DateTimeOffset> _pausedUntil = new Dictionary<string, DateTimeOffset>();
        readonly Dictionary<string, int> _limitedInRow = new Dictionary<string, int>();
        readonly HashSet<string> _inFlight = new HashSet<string>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        DispatcherTimer _timer;
        DispatcherTimer _wake; // one-shot, for the moment a pause ends
        DateTimeOffset _wakeAt;
        bool _antigravityClaudeGpt = Settings.AntigravityClaudeGpt;

        public event Action Changed;

        public UsageStore(IUsageProvider[] providers)
        {
            _providers = providers;
            Settings.Changed += () =>
            {
                if (_antigravityClaudeGpt != Settings.AntigravityClaudeGpt)
                {
                    _antigravityClaudeGpt = Settings.AntigravityClaudeGpt;
                    _latest.Remove("antigravity");
                    _due.Remove("antigravity");
                    _lastAttempt.Remove("antigravity");
                }
                Tick();
            };
        }

        static TimeSpan Interval => TimeSpan.FromMinutes(Settings.RefreshMinutes);

        public IReadOnlyList<IUsageProvider> Providers => _providers;

        IEnumerable<IUsageProvider> Shown => _providers.Where(p => Settings.IsShown(p.Id));

        public IReadOnlyList<ServiceUsage> Snapshot =>
            Shown.Select(p => _latest.TryGetValue(p.Id, out var u) ? u : ServiceUsage.Empty(p)).ToList();

        public DateTimeOffset? LastUpdated =>
            _latest.Values.Where(u => u.FetchedAt.HasValue).Select(u => u.FetchedAt).DefaultIfEmpty(null).Max();

        public void Start()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (s, e) => Tick(), dispatcher);
            _timer.Start();
            _wake = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _wake.Tick += (s, e) =>
            {
                _wake.Stop();
                if (_timer.IsEnabled) Tick(); // not while locked
            };
            Tick();

            SystemEvents.SessionSwitch += (s, e) => dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Reason == SessionSwitchReason.SessionLock)
                {
                    _timer.Stop();
                }
                else if (e.Reason == SessionSwitchReason.SessionUnlock && !_timer.IsEnabled)
                {
                    _timer.Start();
                    Tick();
                }
            }));
        }

        public void Stop()
        {
            _timer?.Stop();
            _cts.Cancel();
        }

        /// <summary>The refresh button: the user asked, so this goes through even a rate-limit pause.</summary>
        public void RefreshAll()
        {
            foreach (var p in _providers)
            {
                _due[p.Id] = DateTimeOffset.MinValue;
                _pausedUntil.Remove(p.Id);
                _lastAttempt.Remove(p.Id);
            }
            Tick();
        }

        /// <summary>On opening the panel; a service paused after a 429 stays paused.</summary>
        public void RefreshIfOlderThan(TimeSpan age)
        {
            var updated = LastUpdated;
            if (updated.HasValue && DateTimeOffset.Now - updated.Value <= age) return;
            foreach (var p in _providers) _due[p.Id] = DateTimeOffset.MinValue;
            Tick();
        }

        void Tick()
        {
            var now = DateTimeOffset.Now;
            foreach (var p in Shown)
            {
                if (_inFlight.Contains(p.Id)) continue;
                if (_pausedUntil.TryGetValue(p.Id, out var paused) && paused > now) continue;
                if (_due.TryGetValue(p.Id, out var due) && due > now) continue;
                if (_lastAttempt.TryGetValue(p.Id, out var last) && now - last < MinGap) continue;
                _ = FetchAsync(p);
            }
            Changed?.Invoke();
        }

        async Task FetchAsync(IUsageProvider provider)
        {
            _inFlight.Add(provider.Id);
            _lastAttempt[provider.Id] = DateTimeOffset.Now;
            ServiceUsage result;
            DateTimeOffset? pausedUntil = null;
            try
            {
                result = await Task.Run(() => provider.FetchAsync(_cts.Token));
                result.FetchedAt = DateTimeOffset.Now;
            }
            catch (RateLimitedException ex)
            {
                var inRow = _limitedInRow.TryGetValue(provider.Id, out var n) ? n + 1 : 1;
                _limitedInRow[provider.Id] = inRow;
                var pause = inRow == 1 ? RateLimitPause : RepeatedRateLimitPause;
                if (ex.RetryAfter > pause) pause = ex.RetryAfter.Value;
                var until = DateTimeOffset.Now + pause;
                if (_pausedUntil.TryGetValue(provider.Id, out var earlier) && earlier > until) until = earlier;
                pausedUntil = until;
                result = new ServiceUsage { Id = provider.Id, Name = provider.Name, Error = Text.RateLimited(provider.Name, until) };
                Log.Info(provider.Id, $"429 #{inRow}, paused {pause.TotalMinutes:0.#} min");
            }
            catch (UsageException ex)
            {
                Log.Info(provider.Id, ex.Message);
                result = new ServiceUsage { Id = provider.Id, Name = provider.Name, Error = ex.Message };
            }
            catch (Exception ex)
            {
                Log.Error(provider.Id, ex);
                result = new ServiceUsage { Id = provider.Id, Name = provider.Name, Error = Text.FetchFailed };
            }
            finally
            {
                _inFlight.Remove(provider.Id);
            }
            if (_cts.IsCancellationRequested) return;

            if (result.Error != null && _latest.TryGetValue(provider.Id, out var previous) && previous.Windows.Count > 0)
            {
                result.Windows = previous.Windows;
                result.FetchedAt = previous.FetchedAt;
            }
            _latest[provider.Id] = result;

            if (pausedUntil.HasValue)
            {
                _pausedUntil[provider.Id] = pausedUntil.Value;
                _due[provider.Id] = pausedUntil.Value;
                WakeAt(pausedUntil.Value);
            }
            else
            {
                if (result.Error == null)
                {
                    _limitedInRow.Remove(provider.Id);
                    _pausedUntil.Remove(provider.Id);
                }
                _due[provider.Id] = NextDue(result, DateTimeOffset.Now, result.Error == null ? Interval : ErrorBackoff);
            }

            Changed?.Invoke();
        }

        void WakeAt(DateTimeOffset at)
        {
            if (_wake == null || (_wake.IsEnabled && _wakeAt <= at)) return; // an earlier wake is already set
            var wait = at - DateTimeOffset.Now;
            _wake.Stop();
            _wake.Interval = wait > TimeSpan.FromSeconds(1) ? wait : TimeSpan.FromSeconds(1);
            _wakeAt = at;
            _wake.Start();
        }

        static DateTimeOffset NextDue(ServiceUsage usage, DateTimeOffset from, TimeSpan wait)
        {
            var next = from + wait;
            var soonestReset = usage.Windows.Where(w => !w.IsIdle && w.ResetsAt > from).Select(w => w.ResetsAt.Value).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            if (soonestReset != DateTimeOffset.MaxValue && soonestReset + AfterReset < next) next = soonestReset + AfterReset;
            return next;
        }
    }
}
