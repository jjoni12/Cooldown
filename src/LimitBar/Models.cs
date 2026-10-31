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
        public UsageException(string message) : base(message) { }

        public static UsageException Server(string service, int status) => new UsageException(Text.ServerError(service, status));
    }

    /// <summary>The service answered 429; RetryAfter is how long it asked us to wait, if it said.</summary>
    public sealed class RateLimitedException : Exception
    {
        public RateLimitedException(TimeSpan? retryAfter) : base("429") => RetryAfter = retryAfter;

        public TimeSpan? RetryAfter { get; }
    }
}
