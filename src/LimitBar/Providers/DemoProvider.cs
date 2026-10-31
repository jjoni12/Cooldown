using System;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar.Providers
{
    // Fixed sample numbers instead of real accounts, for screenshots: run with LIMITBAR_DEMO=1.
    public sealed class DemoProvider : IUsageProvider
    {
        readonly (string Label, TimeSpan Length, double Used, TimeSpan Left, bool Fixed)[] _windows;

        DemoProvider(string id, string name, params (string, TimeSpan, double, TimeSpan, bool)[] windows)
        {
            Id = id;
            Name = name;
            _windows = windows;
        }

        public static bool On => Environment.GetEnvironmentVariable("LIMITBAR_DEMO") == "1";

        public static IUsageProvider[] All() => new IUsageProvider[]
        {
            new DemoProvider("claude", "Claude",
                ("5h", TimeSpan.FromHours(5), 78, new TimeSpan(2, 14, 0), false),
                ("1w", TimeSpan.FromDays(7), 41, new TimeSpan(4, 6, 0, 0), false),
                ("1w|Fable", TimeSpan.FromDays(7), 18, new TimeSpan(4, 6, 0, 0), false)),
            new DemoProvider("gpt", "GPT",
                ("5h", TimeSpan.FromHours(5), 23, new TimeSpan(3, 40, 0), false),
                ("1w", TimeSpan.FromDays(7), 93, new TimeSpan(1, 2, 0, 0), false)),
            new DemoProvider("cursor", "Cursor",
                ("1w", TimeSpan.FromDays(7), 52, new TimeSpan(3, 9, 0, 0), true)),
        };

        public string Id { get; }
        public string Name { get; }
        public bool IsSetUp => true;

        public Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var usage = new ServiceUsage { Id = Id, Name = Name };
            foreach (var w in _windows)
            {
                usage.Windows.Add(new UsageWindow
                {
                    Label = w.Label,
                    Length = w.Length,
                    UsedPercent = w.Used,
                    ResetsAt = DateTimeOffset.Now + w.Left,
                    FixedReset = w.Fixed,
                });
            }
            return Task.FromResult(usage);
        }
    }
}
