namespace ProConnect.Infrastructure.Provider
{
    /// <summary>
    /// Wire format for Nominatim's /search endpoint (https://nominatim.org/release-docs/latest/api/Search/).
    /// Provider-private - never leaves NominatimGeocodingProvider. Nominatim returns lat/lon as JSON strings
    /// (not numbers), so they're parsed to double after deserialization rather than mapped directly.
    /// </summary>
    public class NominatimResult
    {
        public string lat { get; set; }
        public string lon { get; set; }
    }
}
