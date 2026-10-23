using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class NotificationEntityConfiguration : IEntityTypeConfiguration<NotificationEntity>
    {
        public void Configure(EntityTypeBuilder<NotificationEntity> builder)
        {
            builder.ToTable("notification");
            builder.HasKey(n => n.notificationid);

            builder.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(n => n.userid)
                .OnDelete(DeleteBehavior.Cascade);

            // GetNotifications pages most-recent-first per user.
            builder.HasIndex(n => new { n.userid, n.created_at_utc });

            // CountUnread filters on this pair without a table scan - every unread-count badge poll hits it.
            builder.HasIndex(n => new { n.userid, n.is_read });
        }
    }
}
