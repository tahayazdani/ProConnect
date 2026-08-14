namespace ProConnect.Domain.Config
{
    /// <summary>
    /// Config for calling the harvestapi/linkedin-profile-scraper Actor on Apify via its synchronous
    /// run-sync-get-dataset-items API (docs: https://docs.apify.com/api/v2/act-run-sync-get-dataset-items-post).
    /// Replaces the dead ProxyCurlConfig - Proxycurl was sued by LinkedIn/Microsoft and shut down in 2025.
    /// </summary>
    public class HarvestApiConfig
    {
        public HarvestApiConfig()
        {

        }
        public string apitoken { get; set; }
        public string baseurl { get; set; }
        public string actorid { get; set; }
        public string profilescrapermode { get; set; }
        public int timeoutinseconds { get; set; }
    }
}
