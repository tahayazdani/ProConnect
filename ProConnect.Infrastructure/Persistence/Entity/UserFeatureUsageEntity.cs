using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// Backs FeatureGateService.TryConsume's counter for a counted feature (e.g. daily_likes) - one row per
    /// (userid, feature_key, period_key), where period_key is a short string derived from ResetPeriod
    /// (e.g. "2026-10-23" for Daily, "2026-10" for Monthly) so a new period starts a fresh row rather than
    /// needing a reset job. No Redis atomic increment primitive exists in ICache/Cache/RedisCache (checked
    /// before choosing this design), and this codebase's EF Core LINQ-only convention rules out raw SQL -
    /// so this is a plain read-then-increment-or-insert via EF, same documented rare-race trade-off as
    /// MatchService.TriggerBackgroundRefill elsewhere in this codebase. TryConsume reads current usage and
    /// only increments if under the limit, so repeated rejected attempts never inflate the counter past the
    /// cap.
    /// </summary>
    public class UserFeatureUsageEntity
    {
        public Guid userid { get; set; }
        public string feature_key { get; set; }
        public string period_key { get; set; }
        public int used_count { get; set; }
        public DateTime updated_at_utc { get; set; }
    }
}
