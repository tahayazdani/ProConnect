using System;

namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Domain-layer view of a still-redeemable refresh token row, returned by
    /// IRefreshTokenRepository.GetActiveByHash - deliberately separate from the EF Core
    /// RefreshTokenEntity (same "Domain stays free of persistence types" split as UserRequest/UserEntity)
    /// and from the controller's incoming RefreshTokenRequest body (that one just carries the raw token
    /// string the client presents; this one is what the server already knows about it).
    /// </summary>
    public class RefreshTokenInfo
    {
        public RefreshTokenInfo()
        {

        }
        public Guid userid { get; set; }
        public string tokenhash { get; set; }
        public DateTime expiresat_utc { get; set; }
    }
}
