using NetTopologySuite.Geometries;
using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model for a user - deliberately separate from ProConnect.Domain.Request.UserRequest
    /// so the Domain project stays free of any EF Core/Npgsql dependency. Repositories map between the two.
    /// birth_date is flattened to three nullable ints (Domain's `date` type is a mutable class with day/
    /// month/year, not something EF needs to know about) - a full DateOnly isn't used because harvestapi can
    /// return partial dates (e.g. year only).
    /// </summary>
    public class UserEntity
    {
        public Guid userid { get; set; }
        public string? sub { get; set; }
        public string? emailaddress { get; set; }
        public string? public_identifier { get; set; }
        public string? profile_pic_url { get; set; }
        public string? background_cover_image_url { get; set; }
        public string? first_name { get; set; }
        public string? last_name { get; set; }
        public string? full_name { get; set; }
        public int follower_count { get; set; }
        public string? occupation { get; set; }
        public string? headline { get; set; }
        public string? summary { get; set; }
        public string? country { get; set; }
        public string? country_full_name { get; set; }
        public string? city { get; set; }
        public string? state { get; set; }
        public double? latitude { get; set; }
        public double? longitude { get; set; }

        // Geography(Point, 4326) mirror of latitude/longitude, kept in sync by UserRepository whenever they
        // change - lets SearchRepository run true radius/proximity filtering via Point.Distance(...), which
        // Npgsql's NetTopologySuite plugin translates to a PostGIS ST_Distance call, instead of hand-written SQL.
        public Point? location { get; set; }

        public string? gender { get; set; }
        public int? birth_day { get; set; }
        public int? birth_month { get; set; }
        public int? birth_year { get; set; }
        public string? industry { get; set; }
        public string? company { get; set; }
        public string[]? interests { get; set; }

        // Soft-delete/deactivation flag - see UserRequest.is_active for the full rationale. Column
        // default is true (UserEntityConfiguration) so the migration backfills existing rows as active.
        public bool is_active { get; set; } = true;
    }
}
