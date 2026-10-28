using System;

namespace ProConnect.Application.Interface
{
    public interface ITokenService
    {
        string GenerateToken(Guid userid, string public_identifier, string email);

        /// <summary>
        /// Generates a brand-new opaque, cryptographically-random refresh token value (NOT a JWT - see
        /// RefreshTokenEntity remarks on why). Callers must hash it with HashToken before persisting; the
        /// raw value returned here is handed to the client exactly once and never stored anywhere.
        /// </summary>
        string GenerateRefreshToken();

        /// <summary>
        /// One-way SHA-256 hash (hex-encoded) of a raw refresh token value, used both when persisting a
        /// newly-issued token and when looking one up by the value a client presents to /refreshtoken.
        /// Deliberately unsalted: the input itself is already a high-entropy random value (not a
        /// user-chosen secret reused elsewhere), so there's nothing a per-row salt would protect against
        /// here that the token's own entropy doesn't already cover, and a salt would prevent the direct
        /// hash-and-look-up-by-equality that GetActiveByHash relies on.
        /// </summary>
        string HashToken(string rawtoken);
    }
}
