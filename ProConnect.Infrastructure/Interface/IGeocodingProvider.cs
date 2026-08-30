using ProConnect.Domain.Model;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IGeocodingProvider
    {
        /// <summary>
        /// Resolves free-text location fields (as provided by LinkedIn/harvestapi) into a lat/long coordinate.
        /// Returns null if the location text couldn't be resolved to a coordinate (e.g. empty input, no match).
        /// </summary>
        Task<GeoCoordinate> Geocode(string city, string state, string country);
    }
}
