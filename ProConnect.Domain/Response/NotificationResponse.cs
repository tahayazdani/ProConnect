using System;
using System.Collections.Generic;
using ProConnect.Domain.Const;

namespace ProConnect.Domain.Response
{
    /// <summary>One row of the user's own NotificationEntity history - see INotificationService.GetNotifications.</summary>
    public class NotificationResponse
    {
        public NotificationResponse()
        {

        }
        public Guid notificationid { get; set; }
        public NotificationType type { get; set; }
        public string title { get; set; }
        public string body { get; set; }
        public Guid? related_entity_id { get; set; }
        public bool is_read { get; set; }
        public DateTime created_at_utc { get; set; }
        public DateTime? read_at_utc { get; set; }
    }

    /// <summary>Paged wrapper, same shape as TransactionPageResponse - a plain DB-level offset/limit read,
    /// never cached (see NotificationPageRequest).</summary>
    public class NotificationPageResponse
    {
        public NotificationPageResponse()
        {

        }
        public List<NotificationResponse> results { get; set; } = new();
        public int total_count { get; set; }
        public int page { get; set; }
        public int pagesize { get; set; }
        public bool has_more { get; set; }
    }

    /// <summary>Lightweight response for GET /notifications/unread-count - a client's badge-count poll
    /// shouldn't have to page through the full list just to get this number.</summary>
    public class UnreadCountResponse
    {
        public UnreadCountResponse()
        {

        }
        public int unread_count { get; set; }
    }
}
