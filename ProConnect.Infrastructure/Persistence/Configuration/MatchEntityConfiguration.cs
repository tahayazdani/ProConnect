using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class MatchEntityConfiguration : IEntityTypeConfiguration<MatchEntity>
    {
        public void Configure(EntityTypeBuilder<MatchEntity> builder)
        {
            builder.ToTable("matches");
            builder.HasKey(m => m.matchid);

            builder.HasOne<Entity.UserEntity>()
                .WithMany()
                .HasForeignKey(m => m.userid_a)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Entity.UserEntity>()
                .WithMany()
                .HasForeignKey(m => m.userid_b)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(m => m.userid_a);
            builder.HasIndex(m => m.userid_b);
        }
    }
}
