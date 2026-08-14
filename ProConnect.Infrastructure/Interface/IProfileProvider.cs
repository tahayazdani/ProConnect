using ProConnect.Domain.Response;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    /// <summary>
    /// Fetches an enriched LinkedIn profile by public identifier (or full profile URL). Implementations
    /// hide whichever third-party scraper/API is behind this (currently harvestapi/linkedin-profile-scraper
    /// on Apify - see HarvestApiProfileProvider) so swapping providers later is a DI registration change,
    /// not a rewrite of UserService. Returns null if the profile can't be found/scraped.
    /// </summary>
    public interface IProfileProvider
    {
        Task<PersonProfileResponse> GetProfile(string publicIdentifierOrUrl);
    }
}
