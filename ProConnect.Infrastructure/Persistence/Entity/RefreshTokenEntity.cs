using System;

namespace ProConnect.Infrastructure.Persistence.Entity
{
    /// <summary>
    /// EF Core persistence model for an issued refresh token (SignUp/LinkedInAuthentication issue one
    /// alongside the short-lived JWT access token - see TokenService.GenerateRefreshToken and
    /// UserService.RefreshToken). Only ever stores tokenhash, a SHA-256 hash of the actual opaque,
    /// cryptographically-random token value that was handed to the client - the raw value itself is never
    /// persisted anywhere, so a DB leak alone can't be replayed as a live session the way leaking a raw
    /// token would. Rotation-on-use: redeeming a token (RefreshTokenRepository.Rotate) revokes this row
    /// (revokedat_utc set) and records replacedbytokenhash, then inserts a brand-new row for the
    /// replacement - so reusing an already-rotated token (e.g. a stolen refresh token replayed after the
    /// legitimate client already rotated it) is detectable and rejected rather than silently accepted.
    /// </summary>
    public class RefreshTokenEntity
    {
        public Guid refreshtokenid { get; set; }
        public Guid userid { get; set; }
        public string tokenhash { get; set; }
        public DateTime expiresat_utc { get; set; }
        public DateTime createdat_utc { get; set; }

        // Null while the token is still redeemable. Set the moment it's rotated (used) or explicitly
        // revoked (Logout) - either way, RefreshTokenRepository.GetActiveByHash must treat a non-null
        // value here the same as "doesn't exist" for redemption purposes.
        public DateTime? revokedat_utc { get; set; }

        // Points at the tokenhash of the row that replaced this one on rotation - null for a token that
        // was revoked directly (Logout) rather than rotated, or that hasn't been redeemed/revoked yet.
        // Not a FK to keep rotation a single-row update (set revokedat_utc + this) without needing the new
        // row's id to exist yet.
        public string? replacedbytokenhash { get; set; }
    }
}
