using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar
{
    public sealed class UsageWindow
    {
        public string Label;
        public TimeSpan Length;
        public double UsedPercent;
        public DateTimeOffset? ResetsAt;

        public double EffectivePercent =>
            ResetsAt.HasValue && ResetsAt.Value <= DateTimeOffset.Now ? 0 : UsedPercent;

        public bool FixedReset;

        public bool IsIdle => !FixedReset && Math.Round(EffectivePercent) == 0;

        public string TimeText => IsIdle ? Text.Duration(Length) : Text.Remaining(ResetsAt);

        public static TimeSpan MonthEndingAt(DateTimeOffset? reset) =>
            reset.HasValue ? reset.Value - reset.Value.AddMonths(-1) : TimeSpan.FromDays(30);
    }

    public sealed class ServiceUsage
    {
        public string Id;
        public string Name;
        public List<UsageWindow> Windows = new List<UsageWindow>();
        public string Error;
        public DateTimeOffset? FetchedAt;

        public static ServiceUsage Empty(IUsageProvider p) => new ServiceUsage { Id = p.Id, Name = p.Name };
    }

    public interface IUsageProvider
    {
        string Id { get; }
        string Name { get; }

        bool IsSetUp { get; }

        Task<ServiceUsage> FetchAsync(CancellationToken ct);
    }

    public sealed class UsageException : Exception
    {
        public UsageException(string message, bool rateLimited = false) : base(message) => RateLimited = rateLimited;

        public bool RateLimited { get; }

        public static UsageException Server(string service, int status) =>
            status == 429 ? new UsageException(Text.RateLimited(service), rateLimited: true) : new UsageException(Text.ServerError(service, status));
    }
}
