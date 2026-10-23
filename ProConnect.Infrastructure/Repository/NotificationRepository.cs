using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Persistence.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;

        public NotificationRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<Notification> CreateNotification(Notification notification)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = new NotificationEntity
            {
                notificationid = notification.notificationid == Guid.Empty ? Guid.NewGuid() : notification.notificationid,
                userid = notification.userid,
                type = notification.type,
                title = notification.title,
                body = notification.body,
                related_entity_id = notification.related_entity_id,
                is_read = notification.is_read,
                created_at_utc = notification.created_at_utc == default ? DateTime.UtcNow : notification.created_at_utc,
                read_at_utc = notification.read_at_utc
            };
            db.Notifications.Add(entity);
            await db.SaveChangesAsync();

            notification.notificationid = entity.notificationid;
            notification.created_at_utc = entity.created_at_utc;
            return notification;
        }

        public async Task<List<Notification>> GetNotifications(Guid userid, int offset, int limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entities = await db.Notifications.AsNoTracking()
                .Where(n => n.userid == userid)
                .OrderByDescending(n => n.created_at_utc).ThenBy(n => n.notificationid)
                .Skip(offset).Take(limit)
                .ToListAsync();

            return entities.Select(ToDomain).ToList();
        }

        public async Task<int> CountNotifications(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            return await db.Notifications.AsNoTracking().CountAsync(n => n.userid == userid);
        }

        public async Task<int> CountUnread(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            return await db.Notifications.AsNoTracking().CountAsync(n => n.userid == userid && !n.is_read);
        }

        public async Task<bool> MarkAsRead(Guid userid, Guid notificationid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Notifications
                .FirstOrDefaultAsync(n => n.userid == userid && n.notificationid == notificationid && !n.is_read);
            if (entity is null) return false;

            entity.is_read = true;
            entity.read_at_utc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<int> MarkAllAsRead(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var unread = await db.Notifications.Where(n => n.userid == userid && !n.is_read).ToListAsync();
            if (unread.Count == 0) return 0;

            var now = DateTime.UtcNow;
            foreach (var entity in unread)
            {
                entity.is_read = true;
                entity.read_at_utc = now;
            }
            await db.SaveChangesAsync();
            return unread.Count;
        }

        public async Task<bool> IsChannelEnabled(NotificationType type, NotificationChannel channel)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.NotificationChannelConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.notification_type == type && c.channel == channel);

            // Fail open - see INotificationRepository.IsChannelEnabled remarks.
            return entity?.is_enabled ?? true;
        }

        public async Task<List<UserDeviceToken>> GetDeviceTokens(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entities = await db.UserDeviceTokens.AsNoTracking()
                .Where(t => t.userid == userid)
                .ToListAsync();

            return entities.Select(e => new UserDeviceToken
            {
                userdevicetokenid = e.userdevicetokenid,
                userid = e.userid,
                device_token = e.device_token,
                platform = e.platform,
                created_at_utc = e.created_at_utc,
                last_seen_at_utc = e.last_seen_at_utc
            }).ToList();
        }

        public async Task<UserDeviceToken> UpsertDeviceToken(Guid userid, string device_token, DevicePlatform platform)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserDeviceTokens.FirstOrDefaultAsync(t => t.device_token == device_token);
            var now = DateTime.UtcNow;

            if (entity is null)
            {
                entity = new UserDeviceTokenEntity
                {
                    userdevicetokenid = Guid.NewGuid(),
                    device_token = device_token,
                    created_at_utc = now
                };
                db.UserDeviceTokens.Add(entity);
            }

            // Re-points an existing token to whichever user just registered it - see UserDeviceTokenEntity
            // remarks (shared device / sign-out-sign-in-as-someone-else).
            entity.userid = userid;
            entity.platform = platform;
            entity.last_seen_at_utc = now;

            await db.SaveChangesAsync();

            return new UserDeviceToken
            {
                userdevicetokenid = entity.userdevicetokenid,
                userid = entity.userid,
                device_token = entity.device_token,
                platform = entity.platform,
                created_at_utc = entity.created_at_utc,
                last_seen_at_utc = entity.last_seen_at_utc
            };
        }

        public async Task<bool> RemoveDeviceToken(Guid userid, string device_token)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.UserDeviceTokens.FirstOrDefaultAsync(t => t.userid == userid && t.device_token == device_token);
            if (entity is null) return false;

            db.UserDeviceTokens.Remove(entity);
            await db.SaveChangesAsync();
            return true;
        }

        private static Notification ToDomain(NotificationEntity e) => new Notification
        {
            notificationid = e.notificationid,
            userid = e.userid,
            type = e.type,
            title = e.title,
            body = e.body,
            related_entity_id = e.related_entity_id,
            is_read = e.is_read,
            created_at_utc = e.created_at_utc,
            read_at_utc = e.read_at_utc
        };
    }
}
