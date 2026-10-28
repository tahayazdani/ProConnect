using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Provider;
using ProConnect.Infrastructure.Repository;
using System;
using System.Collections.Generic;

namespace ProConnect.Tests.Infrastructure.Provider
{
    /// <summary>
    /// NominatimGeocodingProvider.Geocode always routes through the concrete Cache (see SearchServiceTests
    /// for the same cache-aside mocking pattern: a mocked ICache.GetCacheItem reporting a miss so the real
    /// origin-fetch delegate - the part under test here - actually runs) before calling IHttpClientUtilities.
    /// CacheAsideConfig.enablecacheaside is left false so Cache.Set no-ops and these tests don't need to
    /// stub ICache.Set at all. The in-process throttle (static SemaphoreSlim/_lastRequestUtc) adds at most
    /// ~1s delay the first time any test in the process runs a real geocode - acceptable here since nothing
    /// asserts timing.
    /// </summary>
    public class NominatimGeocodingProviderTests
    {
        private readonly Mock<IHttpClientUtilities> _httpClientUtilities = new();
        private readonly Mock<ICache> _icache = new();
        private readonly GeocodingConfig _geocodingConfig = new GeocodingConfig
        {
            baseurl = "https://nominatim.example.com",
            useragent = "ProConnect-Test/1.0",
            timeoutinseconds = 5
        };

        public NominatimGeocodingProviderTests()
        {
            _icache.Setup(c => c.GetCacheItem<CacheItem<Domain.Model.GeoCoordinate>>(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(default(CacheItem<CacheItem<Domain.Model.GeoCoordinate>>));
        }

        private NominatimGeocodingProvider CreateProvider()
        {
            var cache = new Cache(new Mock<ILogger<Cache>>().Object, _icache.Object, new CacheAsideConfig { enablecacheaside = false });
            var proConnectConfig = Options.Create(new ProConnectConfig { geocodingConfig = _geocodingConfig });
            var cacheExpiryConfig = new CacheExpiryConfig { geocode = 90 * 24 * 60 * 60 };

            return new NominatimGeocodingProvider(
                _httpClientUtilities.Object,
                cache,
                proConnectConfig,
                cacheExpiryConfig,
                new Mock<ILogger<NominatimGeocodingProvider>>().Object);
        }

        [Fact]
        public async Task Geocode_WithBlankCityStateCountry_ReturnsNullWithoutCallingHttpClient()
        {
            var provider = CreateProvider();

            var result = await provider.Geocode(null, "", "   ");

            Assert.Null(result);
            _httpClientUtilities.Verify(h => h.GetAsync<List<NominatimResult>>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()),
                Times.Never);
        }

        [Fact]
        public async Task Geocode_WithValidMatch_ReturnsParsedCoordinate()
        {
            _httpClientUtilities.Setup(h => h.GetAsync<List<NominatimResult>>(
                    _geocodingConfig.baseurl, It.IsAny<string>(), It.IsAny<TimeSpan>(), null, It.IsAny<Dictionary<string, string>>()))
                .ReturnsAsync(new List<NominatimResult> { new NominatimResult { lat = "43.6532", lon = "-79.3832" } });

            var provider = CreateProvider();

            var result = await provider.Geocode("Toronto", "Ontario", "Canada");

            Assert.NotNull(result);
            Assert.Equal(43.6532, result.latitude);
            Assert.Equal(-79.3832, result.longitude);
        }

        [Fact]
        public async Task Geocode_WhenNoResultsReturned_ReturnsNull()
        {
            _httpClientUtilities.Setup(h => h.GetAsync<List<NominatimResult>>(
                    _geocodingConfig.baseurl, It.IsAny<string>(), It.IsAny<TimeSpan>(), null, It.IsAny<Dictionary<string, string>>()))
                .ReturnsAsync(new List<NominatimResult>());

            var provider = CreateProvider();

            var result = await provider.Geocode("Nowhereville", null, null);

            Assert.Null(result);
        }

        [Fact]
        public async Task Geocode_WhenLatLonUnparseable_ReturnsNull()
        {
            _httpClientUtilities.Setup(h => h.GetAsync<List<NominatimResult>>(
                    _geocodingConfig.baseurl, It.IsAny<string>(), It.IsAny<TimeSpan>(), null, It.IsAny<Dictionary<string, string>>()))
                .ReturnsAsync(new List<NominatimResult> { new NominatimResult { lat = "not-a-number", lon = "-79.3832" } });

            var provider = CreateProvider();

            var result = await provider.Geocode("Toronto", null, "Canada");

            Assert.Null(result);
        }

        [Fact]
        public async Task Geocode_PassesUserAgentHeaderAndTimeoutFromConfig()
        {
            Dictionary<string, string> capturedHeaders = null;
            _httpClientUtilities.Setup(h => h.GetAsync<List<NominatimResult>>(
                    _geocodingConfig.baseurl, It.IsAny<string>(), TimeSpan.FromSeconds(_geocodingConfig.timeoutinseconds), null, It.IsAny<Dictionary<string, string>>()))
                .Callback<string, string, TimeSpan, string, Dictionary<string, string>>((_, __, ___, ____, headers) => capturedHeaders = headers)
                .ReturnsAsync(new List<NominatimResult> { new NominatimResult { lat = "1.0", lon = "2.0" } });

            var provider = CreateProvider();

            await provider.Geocode("Paris", null, "France");

            Assert.NotNull(capturedHeaders);
            Assert.Equal(_geocodingConfig.useragent, capturedHeaders["User-Agent"]);
        }
    }
}
