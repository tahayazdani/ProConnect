using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Domain.Const;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class ProductEntityConfiguration : IEntityTypeConfiguration<ProductEntity>
    {
        public void Configure(EntityTypeBuilder<ProductEntity> builder)
        {
            builder.ToTable("product");
            builder.HasKey(p => p.productid);
            builder.HasIndex(p => p.consumable_key);

            builder.Property(p => p.price).HasColumnType("numeric(10,2)");

            // Default seed bundles for Const.ConsumableKey.* - same "editable without a deploy" story as
            // subscription_plan: price/quantity_granted/is_active can all be changed straight in the table,
            // and a brand-new bundle size is just another row, no code change.
            builder.HasData(
                new ProductEntity
                {
                    productid = Guid.Parse("44444444-0000-0000-0000-000000000001"),
                    consumable_key = ConsumableKey.Rose,
                    display_name = "5 Roses",
                    description = "Stand out in 5 people's queues.",
                    quantity_granted = 5,
                    price = 9.99m,
                    currency = "usd",
                    is_active = true
                },
                new ProductEntity
                {
                    productid = Guid.Parse("44444444-0000-0000-0000-000000000002"),
                    consumable_key = ConsumableKey.Boost,
                    display_name = "1 Boost",
                    description = "Be a top profile in your area for 30 minutes.",
                    quantity_granted = 1,
                    price = 4.99m,
                    currency = "usd",
                    is_active = true
                }
            );
        }
    }
}
