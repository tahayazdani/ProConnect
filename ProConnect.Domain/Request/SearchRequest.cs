namespace ProConnect.Domain.Request
{
    /// <summary>
    /// Ad hoc lookup, distinct from MatchCriteria - all filters optional, no swipe-state awareness
    /// (independent of who the caller has already liked/passed).
    /// </summary>
    public class SearchRequest
    {
        public SearchRequest()
        {

        }
        public string query { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public string industry { get; set; }
        public string company { get; set; }

        // Optional true proximity search, in kilometers, layered on top of the plain text city/state/country
        // filters above. When set, city/state/country (whichever are provided) are geocoded via
        // IGeocodingProvider into a center point and candidates are filtered to users whose own geocoded
        // coordinates fall within this radius, rather than requiring an exact text match on location fields.
        // Ignored (falls back to the plain text filters) if none of city/state/country are provided, since
        // there'd be nothing to geocode a center point from.
        public double? radius_km { get; set; }
        public int page { get; set; } = 1;
        public int pagesize { get; set; } = 20;
    }
}
