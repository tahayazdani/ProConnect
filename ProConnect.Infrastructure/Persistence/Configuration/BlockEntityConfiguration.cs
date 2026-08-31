using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class BlockEntityConfiguration : IEntityTypeConfiguration<BlockEntity>
    {
        public void Configure(EntityTypeBuilder<BlockEntity> builder)
        {
            builder.ToTable("blocks");
            builder.HasKey(b => new { b.blocker_user_id, b.blocked_user_id });

            // Same defensive pattern as MatchEntityConfiguration: two FKs into the same table can't both
            // cascade, so only one side cascades and UserRepository.DeleteAccount deletes blocks rows
            // explicitly (in both directions) before the user row regardless.
            builder.HasOne<Entity.UserEntity>()
                .WithMany()
                .HasForeignKey(b => b.blocker_user_id)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Entity.UserEntity>()
                .WithMany()
                .HasForeignKey(b => b.blocked_user_id)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(b => b.blocked_user_id);
        }
    }
}
