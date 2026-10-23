using System;
using ProConnect.Domain.Const;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// Per-(notification_type, channel) on/off switch - "flip a flag, no redeploy" story for which channels
    /// a given NotificationType is additionally pushed over, same philosophy as PlanFeatureEntity. Seeded
    /// (see NotificationChannelConfigEntityConfiguration) with every NotificationType x Push = enabled and
    /// every NotificationType x Email = disabled; a future email-sending gateway can be turned on per-type
    /// by flipping existing rows, no schema change needed.
    /// </summary>
    public class NotificationChannelConfigEntity
    {
        public Guid notificationchannelconfigid { get; set; }
        public NotificationType notification_type { get; set; }
        public NotificationChannel channel { get; set; }
        public bool is_enabled { get; set; }
    }
}
