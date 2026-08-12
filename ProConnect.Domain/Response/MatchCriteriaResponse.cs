namespace ProConnect.Domain.Response
{
    /// <summary>
    /// The effective criteria actually used for scoring - explicit overrides merged with defaults
    /// derived from the user's own LinkedIn profile, so this is never empty even if the user never set anything.
    /// </summary>
    public class MatchCriteriaResponse
    {
        public MatchCriteriaResponse()
        {

        }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public string industry { get; set; }
        public int? min_age { get; set; }
        public int? max_age { get; set; }
        public string[] interests { get; set; }
    }
}
