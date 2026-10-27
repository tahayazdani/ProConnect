using Microsoft.EntityFrameworkCore;
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
    public class MessageRepository : IMessageRepository
    {
        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;

        public MessageRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<bool> IsParticipant(Guid userid, Guid matchId)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            return await db.Matches.AnyAsync(m => m.matchid == matchId && (m.userid_a == userid || m.userid_b == userid));
        }

        public async Task<Message> SendMessageIfParticipant(Guid matchId, Guid senderUserId, string content)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var lockKey = (long)HashCode.Combine(matchId);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})");

            var isParticipant = await db.Matches.AnyAsync(m => m.matchid == matchId && (m.userid_a == senderUserId || m.userid_b == senderUserId));
            if (!isParticipant)
            {
                await transaction.CommitAsync();
                return null;
            }

            var entity = new MessageEntity
            {
                messageid = Guid.NewGuid(),
                matchid = matchId,
                senderuserid = senderUserId,
                content = content,
                sent_at_utc = DateTime.UtcNow,
                is_read = false,
                read_at_utc = null
            };

            db.Messages.Add(entity);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return ToDomain(entity);
        }

        public async Task<IEnumerable<Message>> GetMessages(Guid matchId, int offset, int limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // most-recent-first, same ordering convention as MatchRepository.GetMatches - messageid as a
            // tiebreaker keeps paging stable for messages sent in the same tick.
            var entities = await db.Messages.AsNoTracking()
                .Where(m => m.matchid == matchId)
                .OrderByDescending(m => m.sent_at_utc).ThenByDescending(m => m.messageid)
                .Skip(offset).Take(limit)
                .ToListAsync();

            return entities.Select(ToDomain);
        }

        public async Task<int> CountMessages(Guid matchId)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            return await db.Messages.CountAsync(m => m.matchid == matchId);
        }

        public async Task<int> MarkAsRead(Guid matchId, Guid readerUserId)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // Bulk, single round-trip update (no rows loaded into memory) - only touches messages sent by
            // the *other* participant (senderuserid != readerUserId) that aren't already read, so a reader
            // re-opening an already-read conversation is a no-op write. Returns rows-affected so the
            // caller (MessageService) can skip pushing a MessagesRead notification when nothing changed.
            return await db.Messages
                .Where(m => m.matchid == matchId && m.senderuserid != readerUserId && !m.is_read)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.is_read, true)
                    .SetProperty(m => m.read_at_utc, DateTime.UtcNow));
        }

        private static Message ToDomain(MessageEntity entity) => new Message
        {
            messageid = entity.messageid,
            matchid = entity.matchid,
            senderuserid = entity.senderuserid,
            content = entity.content,
            sent_at_utc = entity.sent_at_utc,
            is_read = entity.is_read,
            read_at_utc = entity.read_at_utc
        };
    }
}
