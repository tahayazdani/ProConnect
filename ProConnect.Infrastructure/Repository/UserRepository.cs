using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Npgsql;
using ProConnect.Domain.Const;
using ProConnect.Domain.Exception;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Persistence.Entity;
using System;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    public class UserRepository : IUserRepository
    {
        // SRID 4326 (WGS 84, plain lat/long) matches the "geography (Point, 4326)" column type configured
        // in UserEntityConfiguration and what Nominatim returns.
        private static readonly GeometryFactory GeometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;
        private readonly Cache _cache;

        public UserRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory, Cache cache)
        {
            _dbContextFactory = dbContextFactory;
            _cache = cache;
        }

        public async Task<SignUpResponse> SignUp(UserRequest request)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = ToEntity(request);
            db.Users.Add(entity);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx
                                                && pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Defense-in-depth against a true concurrent-request race: UserService.SignUp's
                // proactive GetBySubForAuthentication/GetByEmailForAuthentication checks are
                // check-then-act, so two genuinely simultaneous signups for the same new sub/email can
                // both pass the check before either one's insert has committed. Translate the resulting
                // 23505 into a typed exception the Application layer can map back to the same clean
                // message the proactive check would have returned, instead of letting a raw Npgsql
                // exception escape through the generic catch as an unhandled 500 (the exact shape
                // BUG-01/BUG-14's proactive checks exist to avoid in the non-race case).
                var conflictField = pgEx.ConstraintName switch
                {
                    "IX_users_sub" => "sub",
                    "IX_users_emailaddress" => "email",
                    _ => "unknown"
                };
                throw new DuplicateUserException(conflictField);
            }

            return ToSignUpResponse(entity);
        }

        // is_active == false everywhere below means "treat as if this user doesn't exist" - a
        // deactivated user is invisible to GetById/GetBySub (blocks their own self-service profile
        // and LinkedIn re-sign-in lookups) just as much as they're invisible to other users' searches
        // and match candidates (see MatchRepository/SearchRepository).
        //
        // Cache-aside (CacheRegion.USERPROFILE) - this is a hot-path lookup, hit on every
        // MatchService.ResolveEffectiveCriteria call (i.e. every recommendations/incoming-likes/seen/
        // criteria request), previously an uncached DB round trip on every single one of those. A
        // deactivated user (null result) is deliberately NOT cached - Cache.Set is a no-op for null data
        // (see Cache.Set), so deactivation is reflected on the very next read rather than waiting out the
        // TTL. Invalidated proactively on every write below rather than relying purely on the TTL to expire.
        public async Task<UserRequest> GetById(Guid userid)
        {
            return await _cache.Get(UserCacheKey(userid), async () =>
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync();

                var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.userid == userid && u.is_active);
                return entity is null ? null : ToDomain(entity);
            }, CacheRegion.USERPROFILE, CacheExpiry.UserProfile);
        }

        public async Task<UserRequest> GetBySub(string sub)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.sub == sub && u.is_active);
            return entity is null ? null : ToDomain(entity);
        }

        // Deliberately unfiltered by is_active, unlike GetBySub above - see interface doc comment.
        public async Task<UserRequest> GetBySubForAuthentication(string sub)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.sub == sub);
            return entity is null ? null : ToDomain(entity);
        }

        // BUG-14 fix: email-equivalent of GetBySubForAuthentication above - deliberately unfiltered by
        // is_active, so a deactivated user's email still blocks a brand-new duplicate signup.
        public async Task<UserRequest> GetByEmailForAuthentication(string email)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.emailaddress == email);
            return entity is null ? null : ToDomain(entity);
        }

        public async Task<UserRequest> Update(UserRequest request)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Users.FirstOrDefaultAsync(u => u.userid == request.userid);
            if (entity is null)
                return null;

            CopyToEntity(request, entity);
            await db.SaveChangesAsync();

            // stale GetById cache entry would otherwise keep serving the pre-edit profile for up to
            // CacheExpiry.UserProfile
            await _cache.Delete(UserCacheKey(request.userid), CacheRegion.USERPROFILE);

            return ToDomain(entity);
        }

        // Direct, unfiltered-by-is_active lookup (unlike GetById) so an already-deactivated account can
        // still be targeted idempotently, and so this doesn't depend on the record still being active
        // at the moment of the call. Returns false only when the userid doesn't exist at all.
        public async Task<bool> Deactivate(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Users.FirstOrDefaultAsync(u => u.userid == userid);
            if (entity is null)
                return false;

            entity.is_active = false;
            await db.SaveChangesAsync();

            // without this, a cached (pre-deactivation) GetById result could keep this user showing up as
            // active to callers of ResolveEffectiveCriteria for up to CacheExpiry.UserProfile
            await _cache.Delete(UserCacheKey(userid), CacheRegion.USERPROFILE);

            return true;
        }

        // Mirror of Deactivate - see interface doc comment. Only ever called from
        // UserService.LinkedInAuthentication, not exposed as its own endpoint.
        public async Task<bool> Reactivate(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var entity = await db.Users.FirstOrDefaultAsync(u => u.userid == userid);
            if (entity is null)
                return false;

            entity.is_active = true;
            await db.SaveChangesAsync();

            // GetById never caches a deactivated (null) result to begin with (see GetById), but clearing
            // here too keeps this method's invalidation behavior symmetric with Deactivate rather than
            // relying on that as the only reason it's safe.
            await _cache.Delete(UserCacheKey(userid), CacheRegion.USERPROFILE);

            return true;
        }

        // Explicit, ordered deletion inside a transaction rather than relying purely on DB cascade FKs -
        // matches.userid_b and blocks.blocked_user_id are intentionally OnDelete(Restrict), not Cascade
        // (see MatchEntityConfiguration / BlockEntityConfiguration), so a bare "delete the user row" would
        // fail with a FK violation for any match/block where this user is on the Restrict side. Deleting
        // children first (swipes, matches, blocks, match criteria) before the user row itself sidesteps
        // that regardless of each FK's configured OnDelete behavior. ExecuteDeleteAsync issues a bulk
        // DELETE per table instead of loading rows into memory first.
        public async Task<bool> DeleteAccount(Guid userid)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var exists = await db.Users.AnyAsync(u => u.userid == userid);
            if (!exists)
                return false;

            await using var transaction = await db.Database.BeginTransactionAsync();

            await db.Swipes.Where(s => s.userid == userid || s.targetuserid == userid).ExecuteDeleteAsync();
            await db.Matches.Where(m => m.userid_a == userid || m.userid_b == userid).ExecuteDeleteAsync();
            await db.Blocks.Where(b => b.blocker_user_id == userid || b.blocked_user_id == userid).ExecuteDeleteAsync();
            await db.MatchCriteria.Where(c => c.userid == userid).ExecuteDeleteAsync();
            await db.Users.Where(u => u.userid == userid).ExecuteDeleteAsync();

            await transaction.CommitAsync();

            await _cache.Delete(UserCacheKey(userid), CacheRegion.USERPROFILE);

            return true;
        }

        private static string UserCacheKey(Guid userid) => $"user:{userid}";

        private static UserEntity ToEntity(UserRequest request)
        {
            // New signups always start active regardless of whatever is_active a caller-built
            // UserRequest happens to carry - deactivation only ever happens via the dedicated
            // Deactivate method below, never through the generic SignUp/Update path.
            var entity = new UserEntity { userid = request.userid, is_active = true };
            CopyToEntity(request, entity);
            return entity;
        }

        // Shared by SignUp (new entity) and Update (tracked entity) so both stay in sync field-for-field,
        // including the location Point mirror of latitude/longitude.
        private static void CopyToEntity(UserRequest request, UserEntity entity)
        {
            entity.sub = request.sub;
            entity.emailaddress = request.emailaddress;
            entity.public_identifier = request.public_identifier;
            entity.profile_pic_url = request.profile_pic_url;
            entity.background_cover_image_url = request.background_cover_image_url;
            entity.first_name = request.first_name;
            entity.last_name = request.last_name;
            entity.full_name = request.full_name;
            entity.follower_count = request.follower_count;
            entity.occupation = request.occupation;
            entity.headline = request.headline;
            entity.summary = request.summary;
            entity.country = request.country;
            entity.country_full_name = request.country_full_name;
            entity.city = request.city;
            entity.state = request.state;
            entity.latitude = request.latitude;
            entity.longitude = request.longitude;
            entity.location = (request.latitude.HasValue && request.longitude.HasValue)
                ? GeometryFactory.CreatePoint(new Coordinate(request.longitude.Value, request.latitude.Value))
                : null;
            entity.gender = request.gender;
            entity.birth_day = request.birth_date?.day;
            entity.birth_month = request.birth_date?.month;
            entity.birth_year = request.birth_date?.year;
            entity.industry = request.industry;
            entity.company = request.company;
            entity.interests = request.interests;
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

        private static SignUpResponse ToSignUpResponse(UserEntity entity) => new SignUpResponse
        {
            userid = entity.userid,
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
            gender = entity.gender,
            birth_date = (entity.birth_day, entity.birth_month, entity.birth_year) switch
            {
                (null, null, null) => null,
                _ => new date { day = entity.birth_day ?? 0, month = entity.birth_month ?? 0, year = entity.birth_year ?? 0 }
            }
        };
    }
}
