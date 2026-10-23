using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class PaymentTransactionEntityConfiguration : IEntityTypeConfiguration<PaymentTransactionEntity>
    {
        public void Configure(EntityTypeBuilder<PaymentTransactionEntity> builder)
        {
            builder.ToTable("payment_transaction");
            builder.HasKey(t => t.transactionid);

            builder.Property(t => t.amount).HasColumnType("numeric(10,2)");

            builder.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(t => t.userid)
                .OnDelete(DeleteBehavior.Cascade);

            // GetTransactions pages most-recent-first per user.
            builder.HasIndex(t => new { t.userid, t.created_at_utc });

            // Idempotency key for inbound webhooks - see PaymentTransactionEntity remarks. Filtered/unique
            // only where non-null since not every transaction necessarily originates from a webhook event.
            builder.HasIndex(t => t.stripe_event_id).IsUnique().HasFilter("stripe_event_id IS NOT NULL");
        }
    }
}
