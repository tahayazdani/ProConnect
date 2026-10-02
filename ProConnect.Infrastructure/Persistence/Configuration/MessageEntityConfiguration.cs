using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProConnect.Infrastructure.Persistence.Entity;

namespace ProConnect.Infrastructure.Persistence.Configuration
{
    public class MessageEntityConfiguration : IEntityTypeConfiguration<MessageEntity>
    {
        public void Configure(EntityTypeBuilder<MessageEntity> builder)
        {
            builder.ToTable("messages");
            builder.HasKey(m => m.messageid);

            // Cascade off the match itself - unmatching hard-deletes the matches row (MatchRepository.
            // Unmatch), so this makes "delete every message in that conversation too" automatic at the DB
            // level, with no extra cleanup code needed in MatchRepository.Unmatch or UserRepository.
            // DeleteAccount (the latter already deletes matches rows before users rows, which transitively
            // cascades any remaining messages away before the senderuserid FK below would even be checked).
            builder.HasOne<Entity.MatchEntity>()
                .WithMany()
                .HasForeignKey(m => m.matchid)
                .OnDelete(DeleteBehavior.Cascade);

            // senderuserid is informational (which side of the match sent it) rather than the row's scoping
            // key - Restrict here, same reasoning as MatchEntityConfiguration/BlockEntityConfiguration's
            // Restrict side, though in practice the matchid cascade above already clears every message a
            // user could have sent by the time UserRepository.DeleteAccount reaches the users row.
            builder.HasOne<Entity.UserEntity>()
                .WithMany()
                .HasForeignKey(m => m.senderuserid)
                .OnDelete(DeleteBehavior.Restrict);

            // Covers MessageRepository.GetMessages/CountMessages/MarkAsRead - all scoped to
            // "WHERE matchid = ... ORDER BY sent_at_utc" - directly from the index, without a separate
            // lookup + sort step.
            builder.HasIndex(m => new { m.matchid, m.sent_at_utc });
        }
    }
}
