using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProConnect.Domain.Config;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Provider
{
    /// <summary>
    /// Resolves free-text city/state/country (as provided by LinkedIn/harvestapi) into lat/long via Nominatim
    /// (OpenStreetMap) - the free geocoding option chosen instead of a paid provider (e.g. Google Geocoding).
    /// Results are cached indefinitely in practice (CacheExpiry.Geocode) since a location's coordinates don't
    /// change, which also keeps repeat lookups of common cities well within Nominatim's usage policy
    /// (https://operations.osmfoundation.org/policies/nominatim/, ~1 request/second, descriptive User-Agent
    /// required). A simple in-process throttle additionally enforces a minimum gap between actual outbound
    /// calls (i.e. on cache misses) as a courtesy on top of caching.
    /// </summary>
    public class NominatimGeocodingProvider : IGeocodingProvider
    {
        private static readonly TimeSpan MinRequestGap = TimeSpan.FromSeconds(1);
        private static readonly SemaphoreSlim ThrottleLock = new SemaphoreSlim(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        private readonly IHttpClientUtilities _httpClientUtilities;
        private readonly Cache _cache;
        private readonly GeocodingConfig _config;
        private readonly ILogger<NominatimGeocodingProvider> _logger;

        public NominatimGeocodingProvider(
            IHttpClientUtilities httpClientUtilities,
            Cache cache,
            IOptions<ProConnectConfig> proConnectConfig,
            ILogger<NominatimGeocodingProvider> logger)
        {
            _httpClientUtilities = httpClientUtilities;
            _cache = cache;
            _config = proConnectConfig.Value?.geocodingConfig
                ?? throw new InvalidOperationException("ProConnectConfig:GeocodingConfig configuration section is missing.");
            _logger = logger;
        }

        public async Task<GeoCoordinate> Geocode(string city, string state, string country)
        {
            var query = BuildQuery(city, state, country);
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var key = CacheKey(query);

            return await _cache.Get(key, async () =>
            {
                await Throttle();

                var path = $"search?q={Uri.EscapeDataString(query)}&format=json&limit=1&addressdetails=0";
                var headers = new Dictionary<string, string> { ["User-Agent"] = _config.useragent };

                _logger.LogDebug($"Geocoding '{query}' via Nominatim");

                var results = await _httpClientUtilities.GetAsync<List<NominatimResult>>(
                    _config.baseurl,
                    path,
                    TimeSpan.FromSeconds(_config.timeoutinseconds),
                    headers: headers);

                var match = results?.FirstOrDefault();
                if (match is null
                    || !double.TryParse(match.lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                    || !double.TryParse(match.lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                {
                    _logger.LogDebug($"Nominatim returned no usable coordinate for '{query}'");
                    return null;
                }

                return new GeoCoordinate { latitude = latitude, longitude = longitude };
            }, CacheRegion.GEOCODE, CacheExpiry.Geocode);
        }

        private static string BuildQuery(string city, string state, string country)
        {
            var parts = new[] { city, state, country }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim());

            return string.Join(", ", parts);
        }

        private static string CacheKey(string query) => $"geocode:{query}".ToLowerInvariant();

        // Ensures at least MinRequestGap elapses between real outbound Nominatim calls across all
        // concurrent callers (the provider is registered as a singleton, so one throttle instance app-wide).
        private static async Task Throttle()
        {
            await ThrottleLock.WaitAsync();
            try
            {
                var elapsedSinceLastRequest = DateTime.UtcNow - _lastRequestUtc;
                var waitTime = MinRequestGap - elapsedSinceLastRequest;
                if (waitTime > TimeSpan.Zero)
                    await Task.Delay(waitTime);

                _lastRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                ThrottleLock.Release();
            }
        }
    }
}
