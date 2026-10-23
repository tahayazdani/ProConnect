using System;
using ProConnect.Domain.Const;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model mirroring ProConnect.Domain.Model.Notification - one row per notified event
    /// (NewMatch/NewLike from MatchService.Swipe, subscription/payment events from SubscriptionService's
    /// webhook handlers). This table IS the in-app/audit-trail channel - it's always written regardless of
    /// NotificationChannelConfigEntity, which only gates whether a push/email was *additionally* sent for
    /// the same row.
    /// </summary>
    public class NotificationEntity
    {
        public Guid notificationid { get; set; }
        public Guid userid { get; set; }
        public NotificationType type { get; set; }
        public string title { get; set; }
        public string body { get; set; }
        public Guid? related_entity_id { get; set; }
        public bool is_read { get; set; }
        public DateTime created_at_utc { get; set; }
        public DateTime? read_at_utc { get; set; }
    }
}
