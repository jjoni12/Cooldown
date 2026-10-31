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
        static readonly TimeSpan RateLimitRetry = TimeSpan.FromSeconds(5);
        static readonly TimeSpan AfterReset = TimeSpan.FromSeconds(30);
        // The Claude endpoint answers 429 when asked too often.
        static readonly TimeSpan MinGap = TimeSpan.FromSeconds(30);

        readonly IUsageProvider[] _providers;
        readonly Dictionary<string, ServiceUsage> _latest = new Dictionary<string, ServiceUsage>();
        readonly Dictionary<string, DateTimeOffset> _due = new Dictionary<string, DateTimeOffset>();
        readonly Dictionary<string, DateTimeOffset> _lastAttempt = new Dictionary<string, DateTimeOffset>();
        readonly HashSet<string> _inFlight = new HashSet<string>();
        readonly HashSet<string> _rateLimited = new HashSet<string>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        DispatcherTimer _timer;
        DispatcherTimer _retry;
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
            _retry = new DispatcherTimer(RateLimitRetry, DispatcherPriority.Background, (s, e) =>
            {
                _retry.Stop();
                if (_timer.IsEnabled) Tick();
            }, dispatcher);
            _retry.Stop();
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

        public void RefreshAll()
        {
            foreach (var p in _providers) _due[p.Id] = DateTimeOffset.MinValue;
            Tick();
        }

        public void RefreshIfOlderThan(TimeSpan age)
        {
            var updated = LastUpdated;
            if (!updated.HasValue || DateTimeOffset.Now - updated.Value > age) RefreshAll();
        }

        void Tick()
        {
            var now = DateTimeOffset.Now;
            foreach (var p in Shown)
            {
                if (_inFlight.Contains(p.Id)) continue;
                if (_due.TryGetValue(p.Id, out var due) && due > now) continue;
                if (!_rateLimited.Contains(p.Id) && _lastAttempt.TryGetValue(p.Id, out var last) && now - last < MinGap) continue;
                _ = FetchAsync(p);
            }
            Changed?.Invoke();
        }

        async Task FetchAsync(IUsageProvider provider)
        {
            _inFlight.Add(provider.Id);
            _lastAttempt[provider.Id] = DateTimeOffset.Now;
            ServiceUsage result;
            var rateLimited = false;
            try
            {
                result = await Task.Run(() => provider.FetchAsync(_cts.Token));
                result.FetchedAt = DateTimeOffset.Now;
            }
            catch (UsageException ex)
            {
                Log.Info(provider.Id, ex.Message);
                rateLimited = ex.RateLimited;
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

            if (rateLimited)
            {
                _rateLimited.Add(provider.Id);
                _due.Remove(provider.Id);
                _retry?.Start();
            }
            else
            {
                _rateLimited.Remove(provider.Id);
                _due[provider.Id] = NextDue(result, DateTimeOffset.Now, result.Error == null ? Interval : ErrorBackoff);
            }

            Changed?.Invoke();
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
