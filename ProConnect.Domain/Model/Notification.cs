using System;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Domain-layer mirror of NotificationEntity - see INotificationRepository/INotificationService. Always
    /// created regardless of NotificationChannelConfigEntity ("the" in-app/audit-trail channel), independent
    /// of whether a push/email was additionally sent for the same event.
    /// </summary>
    public class Notification
    {
        public Guid notificationid { get; set; }
        public Guid userid { get; set; }
        public NotificationType type { get; set; }
        public string title { get; set; }
        public string body { get; set; }

        // e.g. a matchid for NewMatch, a transactionid for payment events - optional, lets a client deep-link
        // on tap without needing to parse it out of `body`.
        public Guid? related_entity_id { get; set; }
        public bool is_read { get; set; }
        public DateTime created_at_utc { get; set; }
        public DateTime? read_at_utc { get; set; }
    }
}
