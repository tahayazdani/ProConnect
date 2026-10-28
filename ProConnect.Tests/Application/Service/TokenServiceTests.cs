using Microsoft.Extensions.Options;
using ProConnect.Application.Service;
using ProConnect.Domain.Config;

namespace ProConnect.Tests.Application.Service
{
    /// <summary>
    /// Covers TokenService.GenerateRefreshToken and HashToken - the two pieces underpinning the whole
    /// refresh-token flow (see RefreshTokenEntity remarks). GenerateToken (the JWT access token) isn't
    /// covered here since it's a thin wrapper over JwtSecurityTokenHandler with no interesting branches
    /// of our own.
    /// </summary>
    public class TokenServiceTests
    {
        private static TokenService CreateService()
        {
            var config = new ProConnectConfig
            {
                jwtConfig = new JwtConfig
                {
                    secret = "unit-test-signing-secret-at-least-32-bytes-long",
                    issuer = "proconnect-tests",
                    audience = "proconnect-tests",
                    expiryinminutes = 15,
                    refreshtokenexpiryindays = 30
                }
            };

            return new TokenService(Options.Create(config));
        }

        [Fact]
        public void GenerateRefreshToken_ReturnsUrlSafeBase64WithNoPadding()
        {
            var service = CreateService();

            var token = service.GenerateRefreshToken();

            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
        }

        [Fact]
        public void GenerateRefreshToken_ReturnsADifferentValueEachCall()
        {
            var service = CreateService();

            var first = service.GenerateRefreshToken();
            var second = service.GenerateRefreshToken();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void HashToken_IsDeterministic()
        {
            var service = CreateService();

            var first = service.HashToken("some-raw-refresh-token-value");
            var second = service.HashToken("some-raw-refresh-token-value");

            Assert.Equal(first, second);
        }

        [Fact]
        public void HashToken_DifferentInputsProduceDifferentHashes()
        {
            var service = CreateService();

            var first = service.HashToken("raw-token-a");
            var second = service.HashToken("raw-token-b");

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void HashToken_MatchesKnownSha256Hex()
        {
            var service = CreateService();

            // Independently verified: `echo -n "abc" | shasum -a 256` ->
            // ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad (uppercased by
            // Convert.ToHexString, which is what HashToken uses).
            var hash = service.HashToken("abc");

            Assert.Equal(
                "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                hash);
        }
    }
}
