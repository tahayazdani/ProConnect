namespace ProConnect.Domain.Request
{
    /// <summary>
    /// All fields optional - anything left null keeps using the default derived from the user's own LinkedIn profile.
    /// Nobody is required to fill this out to start getting matches.
    /// </summary>
    public class MatchCriteriaRequest
    {
        public MatchCriteriaRequest()
        {

        }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public string industry { get; set; }
        public int? min_age { get; set; }
        public int? max_age { get; set; }
        public string[] interests { get; set; }

        /// <summary>
        /// Optional "within X km of me" filter, centered on the caller's own geocoded profile location.
        /// When set, takes priority over city/state/country for candidate filtering - see
        /// MatchRepository.GetCandidates.
        /// </summary>
        public double? radius_km { get; set; }
    }
}
