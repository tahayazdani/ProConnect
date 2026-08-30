using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class SwipeEntityConfiguration : IEntityTypeConfiguration<SwipeEntity>
    {
        public void Configure(EntityTypeBuilder<SwipeEntity> builder)
        {
            builder.ToTable("swipes");
            builder.HasKey(s => new { s.userid, s.targetuserid });

            builder.HasOne<Entity.UserEntity>()
                .WithMany()
                .HasForeignKey(s => s.userid)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Entity.UserEntity>()
                .WithMany()
                .HasForeignKey(s => s.targetuserid)
                .OnDelete(DeleteBehavior.Cascade);

            // HasLiked(likerUserId, targetUserId) is checked on every Like swipe (to detect a reciprocal
            // match) - the composite PK above already covers (userid, targetuserid) lookups, this covers
            // the reverse-direction lookup HasLiked performs.
            builder.HasIndex(s => new { s.targetuserid, s.userid });
        }
    }
}
