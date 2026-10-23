namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Pagination request for INotificationService.GetNotifications - the user's own NotificationEntity
    /// history, most-recent-first. Same shape as TransactionPageRequest (no `refresh` - never cached, a
    /// direct, infrequently-hit DB-level offset/limit read, see INotificationRepository.GetNotifications).
    /// </summary>
    public class NotificationPageRequest
    {
        public NotificationPageRequest()
        {

        }
        public int page { get; set; } = 1;
        public int pagesize { get; set; } = 50;
    }
}
