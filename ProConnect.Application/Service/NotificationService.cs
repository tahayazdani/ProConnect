using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProConnect.Application.Interface;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;

namespace ProConnect.Application.Service
{
    /// <summary>
    /// See INotificationService remarks. Notify is deliberately the only way a Notification row ever gets
    /// created - MatchService/SubscriptionService call it inline after whatever they were actually doing
    /// (recording a match, applying a webhook) has already durably succeeded, and Notify itself never
    /// throws, so a push/live-update failure can never unwind the caller's own successful write.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IPushNotificationGateway _pushNotificationGateway;
        private readonly INotificationNotifier _notificationNotifier;
        private readonly IEmailGateway _emailGateway;
        private readonly IEmailTemplateRenderer _emailTemplateRenderer;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            INotificationRepository notificationRepository,
            IPushNotificationGateway pushNotificationGateway,
            INotificationNotifier notificationNotifier,
            IEmailGateway emailGateway,
            IEmailTemplateRenderer emailTemplateRenderer,
            IUserRepository userRepository,
            ILogger<NotificationService> logger)
        {
            _notificationRepository = notificationRepository;
            _pushNotificationGateway = pushNotificationGateway;
            _notificationNotifier = notificationNotifier;
            _emailGateway = emailGateway;
            _emailTemplateRenderer = emailTemplateRenderer;
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task Notify(Guid userid, NotificationType type, string title, string body, Guid? related_entity_id = null)
        {
            Notification notification;
            try
            {
                notification = await _notificationRepository.CreateNotification(new Notification
                {
                    userid = userid,
                    type = type,
                    title = title,
                    body = body,
                    related_entity_id = related_entity_id,
                    is_read = false
                });
            }
            catch (Exception ex)
            {
                // Unlike the push/live-update steps below, persisting the row IS the source of truth - if
                // this fails there's nothing further to do, but it still must never bubble up and fail
                // whatever the caller (e.g. MatchService.Swipe) was actually doing.
                _logger.LogError(ex, $"Notify failed to persist a '{type}' notification for userid '{userid}'.");
                return;
            }

            var response = ToResponse(notification);

            await TryPushNotify(userid, type, title, body, related_entity_id);
            await TryEmailNotify(userid, type, title, body);
            await TryLiveNotify(userid, response);
        }

        private async Task TryPushNotify(Guid userid, NotificationType type, string title, string body, Guid? related_entity_id)
        {
            try
            {
                if (!await _notificationRepository.IsChannelEnabled(type, NotificationChannel.Push))
                    return;

                var tokens = await _notificationRepository.GetDeviceTokens(userid);
                if (tokens.Count == 0) return;

                var data = new Dictionary<string, string> { ["notification_type"] = type.ToString() };
                if (related_entity_id.HasValue)
                    data["related_entity_id"] = related_entity_id.Value.ToString();

                await _pushNotificationGateway.SendPushAsync(tokens.Select(t => t.device_token), title, body, data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Push dispatch failed for a '{type}' notification to userid '{userid}' (notification row was still persisted).");
            }
        }

        private async Task TryEmailNotify(Guid userid, NotificationType type, string title, string body)
        {
            try
            {
                if (!await _notificationRepository.IsChannelEnabled(type, NotificationChannel.Email))
                    return;

                // IEmailTemplateRenderer.Render returns null when no template file exists yet for `type` -
                // a silent no-op (see its remarks), same shape as "no device tokens registered" being a
                // no-op for the push channel just above.
                var user = await _userRepository.GetById(userid);
                if (user is null || string.IsNullOrWhiteSpace(user.emailaddress))
                    return;

                var placeholders = new Dictionary<string, string>
                {
                    ["FirstName"] = string.IsNullOrWhiteSpace(user.first_name) ? "there" : user.first_name,
                    ["Title"] = title,
                    ["Body"] = body
                };

                var rendered = _emailTemplateRenderer.Render(type, placeholders);
                if (rendered is null) return;

                await _emailGateway.SendEmailAsync(user.emailaddress, rendered.Subject, rendered.HtmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Email dispatch failed for a '{type}' notification to userid '{userid}' (notification row was still persisted).");
            }
        }

        private async Task TryLiveNotify(Guid userid, NotificationResponse response)
        {
            try
            {
                await _notificationNotifier.NotifyNewNotification(userid, response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Live NotifyNewNotification failed for userid '{userid}' (notification '{response.notificationid}' was still persisted).");
            }
        }

        private async Task TryNotifyUnreadCountChanged(Guid userid)
        {
            try
            {
                var unread = await _notificationRepository.CountUnread(userid);
                await _notificationNotifier.NotifyUnreadCountChanged(userid, unread);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Live NotifyUnreadCountChanged failed for userid '{userid}'.");
            }
        }

        public async Task<ProConnectResponse<NotificationPageResponse>> GetNotifications(Guid userid, NotificationPageRequest request)
        {
            try
            {
                request ??= new NotificationPageRequest();

                var offset = (request.page - 1) * request.pagesize;
                var notifications = await _notificationRepository.GetNotifications(userid, offset, request.pagesize);
                var totalCount = await _notificationRepository.CountNotifications(userid);

                return new ProConnectResponse<NotificationPageResponse>
                {
                    body = new NotificationPageResponse
                    {
                        results = notifications.Select(ToResponse).ToList(),
                        total_count = totalCount,
                        page = request.page,
                        pagesize = request.pagesize,
                        has_more = (long)request.page * request.pagesize < totalCount
                    },
                    message = "Notifications have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetNotifications failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<UnreadCountResponse>> GetUnreadCount(Guid userid)
        {
            try
            {
                var unread = await _notificationRepository.CountUnread(userid);
                return new ProConnectResponse<UnreadCountResponse>
                {
                    body = new UnreadCountResponse { unread_count = unread },
                    message = "Unread count has been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetUnreadCount failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> MarkAsRead(Guid userid, Guid notificationid)
        {
            try
            {
                var marked = await _notificationRepository.MarkAsRead(userid, notificationid);
                if (!marked)
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "Notification not found.",
                        message = "Notification not found."
                    };

                await TryNotifyUnreadCountChanged(userid);

                return new ProConnectResponse<string> { body = "ok", message = "Notification marked as read." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"MarkAsRead failed for userid '{userid}' -> notificationid '{notificationid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> MarkAllAsRead(Guid userid)
        {
            try
            {
                var count = await _notificationRepository.MarkAllAsRead(userid);
                if (count > 0)
                    await TryNotifyUnreadCountChanged(userid);

                return new ProConnectResponse<string>
                {
                    body = "ok",
                    message = $"{count} notification(s) marked as read."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"MarkAllAsRead failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> RegisterDevice(Guid userid, RegisterDeviceTokenRequest request)
        {
            try
            {
                if (request is null || string.IsNullOrWhiteSpace(request.device_token))
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "A valid device_token is required.",
                        message = "A valid device_token is required."
                    };

                var result = await _notificationRepository.UpsertDeviceToken(userid, request.device_token, request.platform);

                // BUG-23 fix (G-9): the upsert is deliberately keyed by device_token alone (shared/reset
                // device scenario - see UserDeviceTokenEntity remarks), so re-pointing itself is intended
                // behavior and still succeeds here. What was missing was any audit trail for the case where
                // it silently moves a token away from a *different* user - log it so there's at least a
                // record if a user reports they stopped getting push notifications unexpectedly.
                if (result.previous_userid.HasValue && result.previous_userid.Value != userid)
                    _logger.LogWarning($"Device token re-registered: userid '{userid}' took over a device_token previously owned by userid '{result.previous_userid.Value}'.");

                return new ProConnectResponse<string> { body = "ok", message = "Device has been registered for push notifications." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"RegisterDevice failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> UnregisterDevice(Guid userid, string device_token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(device_token))
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "A valid device_token is required.",
                        message = "A valid device_token is required."
                    };

                // BUG-24 fix (G-26): Kestrel binds route segments without ever decoding a %2F back into a
                // literal '/' (confirmed live via diagnostic logging - see NotificationController.UnregisterDevice
                // remarks), but RegisterDevice/UpsertDeviceToken persisted the token exactly as received in the
                // JSON body, i.e. already un-encoded. So a token containing a literal '/' round-trips through
                // the URL still percent-encoded and never matches the stored row. Un-escaping just this one
                // sequence - the only one routing is known to leave alone - repairs the comparison without
                // touching any other character, and is a no-op for the vast majority of tokens (FCM/APNs
                // registration tokens don't contain a literal slash to begin with).
                var normalized_device_token = device_token?.Replace("%2F", "/").Replace("%2f", "/");
                var removed = await _notificationRepository.RemoveDeviceToken(userid, normalized_device_token);
                if (!removed)
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "Device not found.",
                        message = "Device not found."
                    };

                return new ProConnectResponse<string> { body = "ok", message = "Device has been unregistered." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UnregisterDevice failed for userid '{userid}'.");
                throw;
            }
        }

        private static NotificationResponse ToResponse(Notification n) => new NotificationResponse
        {
            notificationid = n.notificationid,
            type = n.type,
            title = n.title,
            body = n.body,
            related_entity_id = n.related_entity_id,
            is_read = n.is_read,
            created_at_utc = n.created_at_utc,
            read_at_utc = n.read_at_utc
        };
    }
}
