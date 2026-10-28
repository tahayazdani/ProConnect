namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Body for POST /refreshtoken and POST /logout - both take the raw refresh token value the client
    /// was previously issued (see SignUpResponse.refreshtoken / SignInResponse.refreshtoken). /refreshtoken
    /// redeems it for a new access+refresh pair (rotating this one out, see UserService.RefreshToken);
    /// /logout just revokes it outright (see UserService.Logout). Neither endpoint needs the caller's
    /// access token's userid claim to know whose token this is - the token itself is the identifier, which
    /// is exactly why /refreshtoken (unlike every other UserController endpoint) can stay [AllowAnonymous]:
    /// an already-expired access token shouldn't block a legitimate silent-refresh attempt.
    /// </summary>
    public class RefreshTokenRequest
    {
        public RefreshTokenRequest()
        {

        }
        public string refreshtoken { get; set; }
    }
}
