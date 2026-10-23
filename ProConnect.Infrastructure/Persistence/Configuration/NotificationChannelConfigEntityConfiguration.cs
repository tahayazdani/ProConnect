using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Domain.Const;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class NotificationChannelConfigEntityConfiguration : IEntityTypeConfiguration<NotificationChannelConfigEntity>
    {
        public void Configure(EntityTypeBuilder<NotificationChannelConfigEntity> builder)
        {
            builder.ToTable("notification_channel_config");
            builder.HasKey(c => c.notificationchannelconfigid);
            builder.HasIndex(c => new { c.notification_type, c.channel }).IsUnique();

            // Seeded with every NotificationType x Push = enabled, every NotificationType x Email =
            // disabled - see NotificationChannelConfigEntity remarks. Flipping a row's is_enabled directly
            // in this table is the entire story for turning a channel on/off per type, no deploy needed.
            builder.HasData(
                SeedRow("00000001", NotificationType.NewMatch, NotificationChannel.Push, true),
                SeedRow("00000002", NotificationType.NewMatch, NotificationChannel.Email, false),
                SeedRow("00000003", NotificationType.NewLike, NotificationChannel.Push, true),
                SeedRow("00000004", NotificationType.NewLike, NotificationChannel.Email, false),
                SeedRow("00000005", NotificationType.SubscriptionActivated, NotificationChannel.Push, true),
                SeedRow("00000006", NotificationType.SubscriptionActivated, NotificationChannel.Email, false),
                SeedRow("00000007", NotificationType.SubscriptionRenewed, NotificationChannel.Push, true),
                SeedRow("00000008", NotificationType.SubscriptionRenewed, NotificationChannel.Email, false),
                SeedRow("00000009", NotificationType.PaymentFailed, NotificationChannel.Push, true),
                SeedRow("00000010", NotificationType.PaymentFailed, NotificationChannel.Email, false),
                SeedRow("00000011", NotificationType.SubscriptionCanceled, NotificationChannel.Push, true),
                SeedRow("00000012", NotificationType.SubscriptionCanceled, NotificationChannel.Email, false)
            );
        }

        private static NotificationChannelConfigEntity SeedRow(string idSuffix, NotificationType type, NotificationChannel channel, bool enabled) =>
            new NotificationChannelConfigEntity
            {
                notificationchannelconfigid = Guid.Parse($"55555555-0000-0000-0000-{idSuffix.PadLeft(12, '0')}"),
                notification_type = type,
                channel = channel,
                is_enabled = enabled
            };
    }
}
