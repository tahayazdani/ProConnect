using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
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

        public async Task<IEnumerable<UserRequest>> GetCandidates(Guid userid, MatchCriteria criteria, int offset, int limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var (query, selfLocation, orderByDistance) = await BuildCandidateQuery(db, userid, criteria);

            // Stable deterministic order is required either way so offset-based paging doesn't skip/repeat
            // rows across successive GetCandidates calls as the batch grows (see MatchService.GetRecommendations).
            query = orderByDistance
                ? query.OrderBy(u => u.location!.Distance(selfLocation)).ThenBy(u => u.userid)
                : query.OrderBy(u => u.userid);

            var candidates = await query.Skip(offset).Take(limit).ToListAsync();

            return candidates.Select(ToDomain);
        }

        /// <summary>
        /// Same filters as GetCandidates, minus the ordering/paging - used by MatchService to report an
        /// accurate total_count/has_more without loading and scoring every matching row. Cheap relative to
        /// GetCandidates itself since every hard filter it applies is backed by an index (see
        /// UserEntityConfiguration), so this is a index-driven count, not a full scan.
        /// </summary>
        public async Task<int> CountCandidates(Guid userid, MatchCriteria criteria)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var (query, _, _) = await BuildCandidateQuery(db, userid, criteria);

            return await query.CountAsync();
        }

        private static async Task<(IQueryable<UserEntity> query, Point selfLocation, bool orderByDistance)> BuildCandidateQuery(
            ProConnectDbContext db, Guid userid, MatchCriteria criteria)
        {
            // anyone the user has already swiped on (either direction of the decision) is excluded so the
            // same profile isn't recommended twice
            var alreadySwiped = db.Swipes.Where(s => s.userid == userid).Select(s => s.targetuserid);

            // blocking is mutual - excludes both "I blocked them" and "they blocked me" regardless of who
            // placed it (see BlockUser).
            var blockedEitherDirection = db.Blocks
                .Where(b => b.blocker_user_id == userid || b.blocked_user_id == userid)
                .Select(b => b.blocker_user_id == userid ? b.blocked_user_id : b.blocker_user_id);

            // is_active excludes deactivated users from ever being recommended as a candidate.
            var query = db.Users.AsNoTracking()
                .Where(u => u.userid != userid && u.is_active
                    && !alreadySwiped.Contains(u.userid)
                    && !blockedEitherDirection.Contains(u.userid));

            // Age range - hard filter on the generated+indexed `birthdate` column - see ApplyAgeFilter.
            query = ApplyAgeFilter(query, criteria);

            // Industry - hard, exact, case-insensitive filter on the generated+indexed `industry_normalized`
            // column. Unset criteria.industry means no filter at all (everyone passes), same as age.
            if (!string.IsNullOrWhiteSpace(criteria.industry))
            {
                var normalizedIndustry = criteria.industry.Trim().ToLowerInvariant();
                query = query.Where(u => u.industry_normalized == normalizedIndustry);
            }

            // Location - three-way priority, see IMatchRepository.GetCandidates doc comment:
            //   1. radius_km set -> hard PostGIS filter centered on the searching user's own geocoded point
            //   2. else city/state/country set -> hard hierarchical text filter (only the fields provided)
            //   3. else -> no location filter at all; if the searching user has a geocoded point, order by
            //      distance ascending as a secondary signal instead of restricting the pool
            Point selfLocation = null;
            var orderByDistance = false;

            var hasTextLocationCriteria = !string.IsNullOrWhiteSpace(criteria.country)
                || !string.IsNullOrWhiteSpace(criteria.state)
                || !string.IsNullOrWhiteSpace(criteria.city);

            if (criteria.radius_km.HasValue)
            {
                selfLocation = await db.Users.AsNoTracking()
                    .Where(u => u.userid == userid)
                    .Select(u => u.location)
                    .FirstOrDefaultAsync();

                if (selfLocation is not null)
                {
                    var radiusInMeters = criteria.radius_km.Value * 1000;
                    query = query.Where(u => u.location != null && u.location.Distance(selfLocation) <= radiusInMeters);
                    orderByDistance = true;
                }
                // if the searching user has no geocoded location, radius is silently ignored - same
                // fallback SearchRepository.Search uses when a center point can't be resolved - rather than
                // failing the whole request over missing geocoding data.
            }
            else if (hasTextLocationCriteria)
            {
                if (!string.IsNullOrWhiteSpace(criteria.country))
                    query = query.Where(u => u.country != null && EF.Functions.ILike(u.country, criteria.country));

                if (!string.IsNullOrWhiteSpace(criteria.state))
                    query = query.Where(u => u.state != null && EF.Functions.ILike(u.state, criteria.state));

                if (!string.IsNullOrWhiteSpace(criteria.city))
                    query = query.Where(u => u.city != null && EF.Functions.ILike(u.city, criteria.city));
            }
            else
            {
                selfLocation = await db.Users.AsNoTracking()
                    .Where(u => u.userid == userid)
                    .Select(u => u.location)
                    .FirstOrDefaultAsync();

                orderByDistance = selfLocation is not null;
            }

            return (query, selfLocation, orderByDistance);
        }

        // Shared by GetCandidates/CountCandidates (via BuildCandidateQuery) and, since this session's scale
        // pass, GetIncomingLikes/GetPassedCandidates too - a null birthdate (unknown/partial birth date) is
        // excluded whenever either bound is set, matching the old in-memory IsWithinAgeRange semantics: an
        // unknown age can't be proven to satisfy a caller-specified range, so it's dropped rather than
        // assumed to pass.
        private static IQueryable<UserEntity> ApplyAgeFilter(IQueryable<UserEntity> query, MatchCriteria criteria)
        {
            if (!criteria.min_age.HasValue && !criteria.max_age.HasValue)
                return query;

            query = query.Where(u => u.birthdate != null);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (criteria.max_age.HasValue)
            {
                // youngest birthdate that still keeps someone at or under max_age - anyone born more
                // recently than this has not yet reached max_age + 1, so they're still in range.
                var earliestAllowedBirthdate = today.AddYears(-(criteria.max_age.Value + 1)).AddDays(1);
                query = query.Where(u => u.birthdate >= earliestAllowedBirthdate);
            }

            if (criteria.min_age.HasValue)
            {
                // oldest birthdate that still reaches min_age today - anyone born after this hasn't
                // turned min_age yet.
                var latestAllowedBirthdate = today.AddYears(-criteria.min_age.Value);
                query = query.Where(u => u.birthdate <= latestAllowedBirthdate);
            }

            return query;
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

        // offset/limit page over matched_at_utc-descending order at the DB level - a long-tenured user's
        // full match history is never materialized just to serve one page of it (see
        // MatchService.GetMatches). userid_a/userid_b each have their own index (MatchEntityConfiguration);
        // Postgres services the userid_a == userid || userid_b == userid predicate via a bitmap OR across
        // both rather than needing a single composite index (which wouldn't help an OR predicate anyway).
        public async Task<IEnumerable<MatchSummary>> GetMatches(Guid userid, int offset, int limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var matches = await db.Matches.AsNoTracking()
                .Where(m => m.userid_a == userid || m.userid_b == userid)
                .OrderByDescending(m => m.matched_at_utc).ThenBy(m => m.matchid)
                .Skip(offset).Take(limit)
                .ToListAsync();

            if (matches.Count == 0)
                return Enumerable.Empty<MatchSummary>();

            var otherUserIds = matches.Select(m => m.userid_a == userid ? m.userid_b : m.userid_a).ToList();

            // deliberately not filtered by is_active - a temporarily deactivated match partner still shows
            // up in the match list (deactivation only hides someone from future candidate search, it
            // doesn't sever existing matches - see UserRepository doc comments).
            var otherUsers = await db.Users.AsNoTracking()
                .Where(u => otherUserIds.Contains(u.userid))
                .ToDictionaryAsync(u => u.userid);

            return matches
                .Select(m =>
                {
                    var otherUserId = m.userid_a == userid ? m.userid_b : m.userid_a;
                    otherUsers.TryGetValue(otherUserId, out var other);

                    return new MatchSummary
                    {
                        matchid = m.matchid,
                        matched_at_utc = m.matched_at_utc,
                        userid = otherUserId,
                        public_identifier = other?.public_identifier,
                        full_name = other?.full_name,
                        headline = other?.headline,
                        profile_pic_url = other?.profile_pic_url,
                        occupation = other?.occupation,
                        company = other?.company,
                        city = other?.city,
                        state = other?.state,
                        country = other?.country
                    };
                })
                // `matches` is already in matched_at_utc-descending order from the SQL query above -
                // Select preserves source order, so no further sorting is needed (or correct: sorting
                // client-side here would only see this one page, not the true order).
                .ToList();
        }

        public async Task<int> CountMatches(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            return await db.Matches.CountAsync(m => m.userid_a == userid || m.userid_b == userid);
        }

        public async Task<bool> Unmatch(Guid userid, Guid matchId)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // the userid_a/userid_b check is what stops a caller from unmatching a pair they aren't part of
            var deletedCount = await db.Matches
                .Where(m => m.matchid == matchId && (m.userid_a == userid || m.userid_b == userid))
                .ExecuteDeleteAsync();

            return deletedCount > 0;
        }

        public async Task BlockUser(Guid blockerUserId, Guid blockedUserId)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            // blocking always tears down any existing connection between the pair, regardless of which
            // side of userid_a/userid_b either party landed on
            await db.Matches.Where(m =>
                    (m.userid_a == blockerUserId && m.userid_b == blockedUserId) ||
                    (m.userid_a == blockedUserId && m.userid_b == blockerUserId))
                .ExecuteDeleteAsync();

            var alreadyBlocked = await db.Blocks.AsNoTracking()
                .AnyAsync(b => b.blocker_user_id == blockerUserId && b.blocked_user_id == blockedUserId);

            if (!alreadyBlocked)
            {
                db.Blocks.Add(new BlockEntity
                {
                    blocker_user_id = blockerUserId,
                    blocked_user_id = blockedUserId,
                    blocked_at_utc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }

        // Filtered (unordered, unpaged) incoming-likers query, shared by GetIncomingLikes and
        // CountIncomingLikes so the count reflects exactly the same rows the page is drawn from.
        private static IQueryable<UserEntity> BuildIncomingLikesQuery(ProConnectDbContext db, Guid userid, MatchCriteria criteria)
        {
            // everyone who has liked userid...
            var likedByUserIds = db.Swipes
                .Where(s => s.targetuserid == userid && s.action == SwipeAction.Like)
                .Select(s => s.userid);

            // ...that userid hasn't swiped back on yet - once userid responds (like or pass), it's no
            // longer "pending": a reciprocal Like already becomes a match (and thus a swipe row) via
            // CreateMatch's caller, a Pass just resolves it the other way. Either way it drops off here.
            var alreadyResponded = db.Swipes.Where(s => s.userid == userid).Select(s => s.targetuserid);

            // same mutual-blocking exclusion as GetCandidates - a blocked liker should never surface here.
            var blockedEitherDirection = db.Blocks
                .Where(b => b.blocker_user_id == userid || b.blocked_user_id == userid)
                .Select(b => b.blocker_user_id == userid ? b.blocked_user_id : b.blocker_user_id);

            var query = db.Users.AsNoTracking()
                .Where(u => likedByUserIds.Contains(u.userid) && u.is_active
                    && !alreadyResponded.Contains(u.userid)
                    && !blockedEitherDirection.Contains(u.userid));

            return ApplyAgeFilter(query, criteria);
        }

        // offset/limit page over most-recently-liked-first order at the DB level - a popular user's full
        // incoming-likes list is never materialized just to serve one page of it (see
        // MatchService.GetIncomingLikes). Joins back to Swipes purely to get each liker's swiped_at_utc for
        // ordering; the composite (targetuserid, userid) index already covers this join's join-key lookup.
        public async Task<IEnumerable<UserRequest>> GetIncomingLikes(Guid userid, MatchCriteria criteria, int offset, int limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var likedAt = db.Swipes.Where(s => s.targetuserid == userid && s.action == SwipeAction.Like);

            var likers = await BuildIncomingLikesQuery(db, userid, criteria)
                .Join(likedAt, u => u.userid, s => s.userid, (u, s) => new { u, s.swiped_at_utc })
                .OrderByDescending(x => x.swiped_at_utc).ThenBy(x => x.u.userid)
                .Skip(offset).Take(limit)
                .Select(x => x.u)
                .ToListAsync();

            return likers.Select(ToDomain);
        }

        public async Task<int> CountIncomingLikes(Guid userid, MatchCriteria criteria)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            return await BuildIncomingLikesQuery(db, userid, criteria).CountAsync();
        }

        // Filtered (unordered, unpaged) passed-candidates query, shared by GetPassedCandidates and
        // CountPassedCandidates so the count reflects exactly the same rows the page is drawn from.
        private static IQueryable<UserEntity> BuildPassedCandidatesQuery(ProConnectDbContext db, Guid userid, MatchCriteria criteria)
        {
            // everyone userid has passed on - the mirror image of GetCandidates' alreadySwiped exclusion.
            // Only Pass, not Like: a liked-but-not-yet-reciprocated profile still belongs in the normal
            // recommendation flow (it just won't show up there again since GetCandidates excludes all
            // swipes regardless of direction), and a mutual like is already a match, not a "seen" profile.
            var passedUserIds = db.Swipes
                .Where(s => s.userid == userid && s.action == SwipeAction.Pass)
                .Select(s => s.targetuserid);

            // same mutual-blocking exclusion as GetCandidates - a blocked user should never resurface here
            // either, even if they were passed on before the block happened.
            var blockedEitherDirection = db.Blocks
                .Where(b => b.blocker_user_id == userid || b.blocked_user_id == userid)
                .Select(b => b.blocker_user_id == userid ? b.blocked_user_id : b.blocker_user_id);

            var query = db.Users.AsNoTracking()
                .Where(u => u.userid != userid && u.is_active
                    && passedUserIds.Contains(u.userid)
                    && !blockedEitherDirection.Contains(u.userid));

            return ApplyAgeFilter(query, criteria);
        }

        // offset/limit page over most-recently-passed-first order at the DB level - same reasoning as
        // GetIncomingLikes. Joins back to Swipes purely to get each passed candidate's swiped_at_utc for
        // ordering; the composite primary key (userid, targetuserid) already covers this join's join-key
        // lookup.
        public async Task<IEnumerable<UserRequest>> GetPassedCandidates(Guid userid, MatchCriteria criteria, int offset, int limit)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var passedAt = db.Swipes.Where(s => s.userid == userid && s.action == SwipeAction.Pass);

            var passed = await BuildPassedCandidatesQuery(db, userid, criteria)
                .Join(passedAt, u => u.userid, s => s.targetuserid, (u, s) => new { u, s.swiped_at_utc })
                .OrderByDescending(x => x.swiped_at_utc).ThenBy(x => x.u.userid)
                .Skip(offset).Take(limit)
                .Select(x => x.u)
                .ToListAsync();

            return passed.Select(ToDomain);
        }

        public async Task<int> CountPassedCandidates(Guid userid, MatchCriteria criteria)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            return await BuildPassedCandidatesQuery(db, userid, criteria).CountAsync();
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
