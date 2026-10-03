namespace PhoneGrade.Core.SecurityServices;

/// <summary>IMEI blacklist checking service with pluggable provider pattern.</summary>
public static class BlacklistCheckService
{
    public class BlacklistStatus
    {
        public bool IsBlacklisted { get; set; }
        public string? Reason { get; set; }
        public string Source { get; set; } = "Unknown";
    }

    public interface IBlacklistProvider
    {
        Task<BlacklistStatus> CheckAsync(string imei);
    }

    /// <summary>No-op provider that always returns unknown status. Used for testing.</summary>
    public class NoOpBlacklistProvider : IBlacklistProvider
    {
        public Task<BlacklistStatus> CheckAsync(string imei)
        {
            return Task.FromResult(new BlacklistStatus
            {
                IsBlacklisted = false,
                Source = "NoOp"
            });
        }
    }
}