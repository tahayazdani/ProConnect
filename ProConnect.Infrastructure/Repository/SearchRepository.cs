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

            // is_active excludes deactivated users from ever appearing in search results.
            var query = db.Users.AsNoTracking().Where(u => u.userid != excludeUserId && u.is_active);

            if (!string.IsNullOrWhiteSpace(request.query))
            {
                var term = request.query.Trim();
                query = query.Where(u =>
                    (u.full_name != null && EF.Functions.ILike(u.full_name, $"%{term}%")) ||
                    (u.headline != null && EF.Functions.ILike(u.headline, $"%{term}%")) ||
                    (u.occupation != null && EF.Functions.ILike(u.occupation, $"%{term}%")) ||
                    (u.company != null && EF.Functions.ILike(u.company, $"%{term}%")));
            }

            if (!string.IsNullOrWhiteSpace(request.industry))
                query = query.Where(u => u.industry != null && EF.Functions.ILike(u.industry, $"%{request.industry}%"));

            if (!string.IsNullOrWhiteSpace(request.company))
                query = query.Where(u => u.company != null && EF.Functions.ILike(u.company, $"%{request.company}%"));

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
                // if the center point couldn't be resolved, radius_km is silently ignored (falls through to
                // any text filters below) rather than failing the whole search over a geocoding miss
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(request.city))
                    query = query.Where(u => u.city != null && EF.Functions.ILike(u.city, $"%{request.city}%"));

                if (!string.IsNullOrWhiteSpace(request.state))
                    query = query.Where(u => u.state != null && EF.Functions.ILike(u.state, $"%{request.state}%"));

                if (!string.IsNullOrWhiteSpace(request.country))
                    query = query.Where(u => u.country != null && EF.Functions.ILike(u.country, $"%{request.country}%"));
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
