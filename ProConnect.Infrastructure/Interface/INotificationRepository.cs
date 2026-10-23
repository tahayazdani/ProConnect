using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>
    /// Data access for the whole Phase 6 notification schema (notification, notification_channel_config,
    /// user_device_token). INotificationService is the only caller - this repository has no opinion on push
    /// delivery (that's IPushNotificationGateway) or live in-app updates (that's INotificationNotifier) -
    /// purely EF Core LINQ reads/writes, same split as ISubscriptionRepository.
    /// </summary>
    public interface INotificationRepository
    {
        Task<Notification> CreateNotification(Notification notification);

        /// <summary>Most-recent-first, DB-level offset/limit paged - see NotificationPageRequest.</summary>
        Task<List<Notification>> GetNotifications(Guid userid, int offset, int limit);
        Task<int> CountNotifications(Guid userid);
        Task<int> CountUnread(Guid userid);

        /// <summary>False if no matching unread row exists for this user (already read, wrong owner, or
        /// doesn't exist) - callers treat that as a soft "not found", same convention as MatchService.Unmatch.</summary>
        Task<bool> MarkAsRead(Guid userid, Guid notificationid);

        /// <summary>Returns the number of rows actually flipped, so a caller can skip a pointless
        /// "0 marked as read" live-update push - same reasoning as IMessageNotifier.NotifyMessagesRead's readCount.</summary>
        Task<int> MarkAllAsRead(Guid userid);

        /// <summary>Defaults to true (fail open) if no row exists for the pair - a brand-new NotificationType
        /// added to the enum without a corresponding seed row shouldn't silently suppress its own push.</summary>
        Task<bool> IsChannelEnabled(NotificationType type, NotificationChannel channel);

        Task<List<UserDeviceToken>> GetDeviceTokens(Guid userid);

        /// <summary>Upserts by device_token (not by (userid, device_token)) - see UserDeviceTokenEntity
        /// remarks on why the same physical token re-registering under a different user moves the row.</summary>
        Task<UserDeviceToken> UpsertDeviceToken(Guid userid, string device_token, DevicePlatform platform);

        Task<bool> RemoveDeviceToken(Guid userid, string device_token);
    }
}
