using ProConnect.Domain.Request;
using System;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IRefreshTokenRepository
    {
        /// <summary>
        /// Persists a brand-new refresh token row. Callers pass the already-hashed value
        /// (TokenService.HashToken) - the raw token is never written to the DB, see
        /// RefreshTokenEntity remarks.
        /// </summary>
        Task Create(Guid userid, string tokenhash, DateTime expiresat_utc);

        /// <summary>
        /// Looks up a still-redeemable token by its hash: must exist, be unrevoked
        /// (revokedat_utc is null), and not be past expiresat_utc. Returns null for anything else
        /// (unknown hash, already-rotated/revoked, or expired) - callers must treat all of those
        /// identically ("refresh token rejected"), never distinguishing which case it was in the
        /// response, so as not to help an attacker fingerprint a stolen/replayed token.
        /// </summary>
        Task<RefreshTokenInfo> GetActiveByHash(string tokenhash);

        /// <summary>
        /// Rotation: atomically marks the token identified by oldtokenhash as revoked and records
        /// newtokenhash as its replacement, then inserts the new row for newtokenhash/newexpiresat_utc.
        /// Used by UserService.RefreshToken so a given refresh token can only ever be redeemed once -
        /// replaying an already-rotated token afterwards hits the revoked branch of GetActiveByHash.
        /// </summary>
        Task Rotate(string oldtokenhash, string newtokenhash, DateTime newexpiresat_utc);

        /// <summary>
        /// Revokes a single token outright (no replacement) - used by Logout for the specific
        /// refresh token the client presents, so that token can never be redeemed again even though
        /// the access token it was paired with keeps working until its own (short) expiry.
        /// </summary>
        Task Revoke(string tokenhash);

        /// <summary>
        /// Revokes every outstanding, not-yet-revoked refresh token for a user in one pass - used by
        /// DeactivateAccount/DeleteAccount so an account that just went away (temporarily or
        /// permanently) can't have any of its live refresh tokens redeemed afterwards.
        /// </summary>
        Task RevokeAllForUser(Guid userid);
    }
}
