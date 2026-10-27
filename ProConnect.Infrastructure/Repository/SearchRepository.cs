using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Persistence.Entity;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Repository
{
    /// <summary>
    /// Plain text city/state/country filters and true radius/proximity search share one repository since
    /// they're mutually exclusive per request (see SearchRequest.radius_km) and both operate on the same
    /// candidate query. Radius search is done entirely via LINQ - UserEntity.location is a PostGIS
    /// geography(Point,4326) column (via Npgsql's NetTopologySuite plugin), and Point.Distance(...) is
    /// translated to ST_Distance by the EF Core provider, so no raw SQL/Dapper is involved.
    /// </summary>
    public class SearchRepository : ISearchRepository
    {
        private static readonly GeometryFactory GeometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

        private readonly IDbContextFactory<ProConnectDbContext> _dbContextFactory;
        private readonly IGeocodingProvider _geocodingProvider;

        public SearchRepository(IDbContextFactory<ProConnectDbContext> dbContextFactory, IGeocodingProvider geocodingProvider)
        {
            _dbContextFactory = dbContextFactory;
            _geocodingProvider = geocodingProvider;
        }

        public async Task<SearchResult> Search(SearchRequest request, Guid excludeUserId)
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            // BUG-26 fix (G-34): Search had no blocking exclusion at all, so a user who blocked (or was
            // blocked by) excludeUserId in either direction still surfaced in full. Mirrors the identical
            // blockedEitherDirection pattern already used three times in MatchRepository.cs.
            var blockedEitherDirection = db.Blocks
                .Where(b => b.blocker_user_id == excludeUserId || b.blocked_user_id == excludeUserId)
                .Select(b => b.blocker_user_id == excludeUserId ? b.blocked_user_id : b.blocker_user_id);

            // is_active excludes deactivated users from ever appearing in search results.
            var query = db.Users.AsNoTracking().Where(u =>
                u.userid != excludeUserId && u.is_active && !blockedEitherDirection.Contains(u.userid));

            if (!string.IsNullOrWhiteSpace(request.query))
            {
                // BUG-28 fix (G-36): term was interpolated into the ILike pattern raw, so a caller typing a
                // literal '%' or '_' got Postgres LIKE wildcard behavior instead of a literal-text match
                // (e.g. industry=_ matched every non-empty industry). Escaping the three LIKE metacharacters
                // (\, %, _) and passing the matching escapeChar makes '%'/'_' behave as literal characters,
                // same as every other filter field below.
                var term = $"%{EscapeLikePattern(request.query.Trim())}%";
                query = query.Where(u =>
                    (u.full_name != null && EF.Functions.ILike(u.full_name, term, LikeEscapeChar)) ||
                    (u.headline != null && EF.Functions.ILike(u.headline, term, LikeEscapeChar)) ||
                    (u.occupation != null && EF.Functions.ILike(u.occupation, term, LikeEscapeChar)) ||
                    (u.company != null && EF.Functions.ILike(u.company, term, LikeEscapeChar)));
            }

            if (!string.IsNullOrWhiteSpace(request.industry))
            {
                var pattern = $"%{EscapeLikePattern(request.industry)}%";
                query = query.Where(u => u.industry != null && EF.Functions.ILike(u.industry, pattern, LikeEscapeChar));
            }

            if (!string.IsNullOrWhiteSpace(request.company))
            {
                var pattern = $"%{EscapeLikePattern(request.company)}%";
                query = query.Where(u => u.company != null && EF.Functions.ILike(u.company, pattern, LikeEscapeChar));
            }

            Point centerPoint = null;
            if (request.radius_km.HasValue)
            {
                var center = await _geocodingProvider.Geocode(request.city, request.state, request.country);
                if (center is not null)
                {
                    centerPoint = GeometryFactory.CreatePoint(new Coordinate(center.longitude, center.latitude));
                    var radiusInMeters = request.radius_km.Value * 1000;

                    query = query.Where(u => u.location != null && u.location.Distance(centerPoint) <= radiusInMeters);
                }
                else
                {
                    // BUG-27 fix (G-? / SE1-E3): the comment here always claimed a geocode miss "falls
                    // through to any text filters below", but that was never true - the text-filter block
                    // lived in the else branch of this very if, so it was unreachable whenever radius_km
                    // was set. A geocode miss therefore silently applied NO location filter at all (not
                    // even the city/state/country text match it claimed to fall back to), returning every
                    // active user regardless of the city/state/country the caller actually asked for.
                    // Explicitly calling the same text-filter helper here makes the documented fallback real.
                    query = ApplyTextLocationFilters(query, request);
                }
            }
            else
            {
                query = ApplyTextLocationFilters(query, request);
            }

            var totalcount = await query.CountAsync();

            // nearest-first when searching by radius, otherwise a stable alphabetical order
            query = centerPoint is not null
                ? query.OrderBy(u => u.location!.Distance(centerPoint))
                : query.OrderBy(u => u.full_name).ThenBy(u => u.userid);

            var page = await query
                .Skip((request.page - 1) * request.pagesize)
                .Take(request.pagesize)
                .ToListAsync();

            return new SearchResult
            {
                items = page.Select(ToDomain).ToList(),
                totalcount = totalcount
            };
        }

        // BUG-28 fix (G-36): see the EscapeLikePattern/LikeEscapeChar usage above - same escaping applied
        // here so city/state/country text filters can't be broadened by a literal '%' or '_' either.
        private const string LikeEscapeChar = "\\";

        private static string EscapeLikePattern(string value) =>
            value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        private static IQueryable<UserEntity> ApplyTextLocationFilters(IQueryable<UserEntity> query, SearchRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.city))
            {
                var pattern = $"%{EscapeLikePattern(request.city)}%";
                query = query.Where(u => u.city != null && EF.Functions.ILike(u.city, pattern, LikeEscapeChar));
            }

            if (!string.IsNullOrWhiteSpace(request.state))
            {
                var pattern = $"%{EscapeLikePattern(request.state)}%";
                query = query.Where(u => u.state != null && EF.Functions.ILike(u.state, pattern, LikeEscapeChar));
            }

            if (!string.IsNullOrWhiteSpace(request.country))
            {
                var pattern = $"%{EscapeLikePattern(request.country)}%";
                query = query.Where(u => u.country != null && EF.Functions.ILike(u.country, pattern, LikeEscapeChar));
            }

            return query;
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
            industry = entity.industry,
            company = entity.company,
            interests = entity.interests,
            is_active = entity.is_active
        };
    }
}
