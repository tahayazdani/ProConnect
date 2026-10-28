using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Request;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Persistence.Entity;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;

        public RefreshTokenRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task Create(Guid userid, string tokenhash, DateTime expiresat_utc)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            db.RefreshTokens.Add(new RefreshTokenEntity
            {
                refreshtokenid = Guid.NewGuid(),
                userid = userid,
                tokenhash = tokenhash,
                expiresat_utc = expiresat_utc,
                createdat_utc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        public async Task<RefreshTokenInfo> GetActiveByHash(string tokenhash)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t =>
                t.tokenhash == tokenhash
                && t.revokedat_utc == null
                && t.expiresat_utc > DateTime.UtcNow);

            return entity is null ? null : new RefreshTokenInfo
            {
                userid = entity.userid,
                tokenhash = entity.tokenhash,
                expiresat_utc = entity.expiresat_utc
            };
        }

        public async Task Rotate(string oldtokenhash, string newtokenhash, DateTime newexpiresat_utc)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var oldtoken = await db.RefreshTokens.FirstOrDefaultAsync(t => t.tokenhash == oldtokenhash);
            if (oldtoken is null)
            {
                // Nothing to rotate away from (caller already validated via GetActiveByHash, but this
                // guards against a concurrent rotation of the same token winning the race) - still issue
                // the new token so the caller isn't left without one.
                db.RefreshTokens.Add(new RefreshTokenEntity
                {
                    refreshtokenid = Guid.NewGuid(),
                    tokenhash = newtokenhash,
                    expiresat_utc = newexpiresat_utc,
                    createdat_utc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return;
            }

            oldtoken.revokedat_utc = DateTime.UtcNow;
            oldtoken.replacedbytokenhash = newtokenhash;

            db.RefreshTokens.Add(new RefreshTokenEntity
            {
                refreshtokenid = Guid.NewGuid(),
                userid = oldtoken.userid,
                tokenhash = newtokenhash,
                expiresat_utc = newexpiresat_utc,
                createdat_utc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public async Task Revoke(string tokenhash)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.RefreshTokens.FirstOrDefaultAsync(t => t.tokenhash == tokenhash);
            if (entity is null || entity.revokedat_utc != null)
                return;

            entity.revokedat_utc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task RevokeAllForUser(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // Bulk update rather than loading every row into memory first - mirrors
            // UserRepository.DeleteAccount's use of ExecuteDeleteAsync for the same reason.
            await db.RefreshTokens
                .Where(t => t.userid == userid && t.revokedat_utc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.revokedat_utc, DateTime.UtcNow));
        }
    }
}
