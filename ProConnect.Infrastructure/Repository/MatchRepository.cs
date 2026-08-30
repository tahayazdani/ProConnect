using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Const;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Persistence.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class MatchRepository : IMatchRepository
    {
        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;

        public MatchRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<IEnumerable<UserRequest>> GetCandidates(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // anyone the user has already swiped on (either direction of the decision) is excluded so the
            // same profile isn't recommended twice
            var alreadySwiped = db.Swipes.Where(s => s.userid == userid).Select(s => s.targetuserid);

            // is_active excludes deactivated users from ever being recommended as a candidate.
            var candidates = await db.Users.AsNoTracking()
                .Where(u => u.userid != userid && u.is_active && !alreadySwiped.Contains(u.userid))
                .ToListAsync();

            return candidates.Select(ToDomain);
        }

        public async Task RecordSwipe(Guid userid, Guid targetUserId, SwipeAction action)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Swipes.FirstOrDefaultAsync(s => s.userid == userid && s.targetuserid == targetUserId);
            if (entity is null)
            {
                entity = new SwipeEntity { userid = userid, targetuserid = targetUserId };
                db.Swipes.Add(entity);
            }

            entity.action = action;
            entity.swiped_at_utc = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        public async Task<bool> HasLiked(Guid likerUserId, Guid targetUserId)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            return await db.Swipes.AsNoTracking()
                .AnyAsync(s => s.userid == likerUserId && s.targetuserid == targetUserId && s.action == SwipeAction.Like);
        }

        public async Task<Guid> CreateMatch(Guid userIdA, Guid userIdB)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // idempotent - if the pair already matched (in either order), return the existing match instead
            // of creating a duplicate row
            var existing = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m =>
                (m.userid_a == userIdA && m.userid_b == userIdB) ||
                (m.userid_a == userIdB && m.userid_b == userIdA));

            if (existing is not null)
                return existing.matchid;

            var entity = new MatchEntity
            {
                matchid = Guid.NewGuid(),
                userid_a = userIdA,
                userid_b = userIdB,
                matched_at_utc = DateTime.UtcNow
            };

            db.Matches.Add(entity);
            await db.SaveChangesAsync();

            return entity.matchid;
        }

        private static UserRequest ToDomain(UserEntity entity) => new UserRequest
        {
            userid = entity.userid,
            sub = entity.sub,
            emailaddress = entity.emailaddress,
            public_identifier = entity.public_identifier,
            profile_pic_url = entity.profile_pic_url,
            background_cover_image_url = entity.background_cover_image_url,
            first_name = entity.first_name,
            last_name = entity.last_name,
            full_name = entity.full_name,
            follower_count = entity.follower_count,
            occupation = entity.occupation,
            headline = entity.headline,
            summary = entity.summary,
            country = entity.country,
            country_full_name = entity.country_full_name,
            city = entity.city,
            state = entity.state,
            latitude = entity.latitude,
            longitude = entity.longitude,
            gender = entity.gender,
            birth_date = (entity.birth_day, entity.birth_month, entity.birth_year) switch
            {
                (null, null, null) => null,
                _ => new date { day = entity.birth_day ?? 0, month = entity.birth_month ?? 0, year = entity.birth_year ?? 0 }
            },
            industry = entity.industry,
            company = entity.company,
            interests = entity.interests,
            is_active = entity.is_active
        };
    }
}
