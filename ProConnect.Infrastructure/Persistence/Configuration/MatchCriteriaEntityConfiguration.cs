using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class MatchCriteriaEntityConfiguration : IEntityTypeConfiguration<MatchCriteriaEntity>
    {
        public void Configure(EntityTypeBuilder<MatchCriteriaEntity> builder)
        {
            builder.ToTable("match_criteria");
            builder.HasKey(c => c.userid);

            builder.HasOne<Entity.UserEntity>()
                .WithOne()
                .HasForeignKey<MatchCriteriaEntity>(c => c.userid)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Property(c => c.interests).HasColumnType("text[]");
        }
    }
}
