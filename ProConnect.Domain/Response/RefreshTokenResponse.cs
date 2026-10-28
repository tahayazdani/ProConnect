namespace ProConnect.Domain.Response
{
    /// <summary>
    /// Result of a successful POST /refreshtoken call - a brand-new access+refresh pair, replacing the
    /// one the client presented (see UserService.RefreshToken / RefreshTokenEntity's rotation-on-use
    /// design). The client must discard the old refreshtoken value immediately; it was revoked server-side
    /// as part of issuing this one and will be rejected if presented again.
    /// </summary>
    public class RefreshTokenResponse
    {
        public RefreshTokenResponse()
        {

        }
        public string token { get; set; }
        public string refreshtoken { get; set; }
    }
}
