using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Persistence.Entity;
using System;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class MatchCriteriaRepository : IMatchCriteriaRepository
    {
        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;

        public MatchCriteriaRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<MatchCriteria> GetByUserId(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.MatchCriteria.AsNoTracking().FirstOrDefaultAsync(c => c.userid == userid);
            return entity is null ? null : ToDomain(entity);
        }

        // MatchService.UpdateCriteria always sends a full replacement (not a partial patch), so upsert here
        // simply overwrites every field on an existing row rather than merging.
        public async Task<MatchCriteria> Upsert(MatchCriteria criteria)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.MatchCriteria.FirstOrDefaultAsync(c => c.userid == criteria.userid);
            if (entity is null)
            {
                entity = new MatchCriteriaEntity { userid = criteria.userid };
                db.MatchCriteria.Add(entity);
            }

            entity.city = criteria.city;
            entity.state = criteria.state;
            entity.country = criteria.country;
            entity.industry = criteria.industry;
            entity.min_age = criteria.min_age;
            entity.max_age = criteria.max_age;
            entity.interests = criteria.interests;

            await db.SaveChangesAsync();

            return ToDomain(entity);
        }

        private static MatchCriteria ToDomain(MatchCriteriaEntity entity) => new MatchCriteria
        {
            userid = entity.userid,
            city = entity.city,
            state = entity.state,
            country = entity.country,
            industry = entity.industry,
            min_age = entity.min_age,
            max_age = entity.max_age,
            interests = entity.interests
        };
    }
}
