using System;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// A single row from MatchRepository.GetMatches - the match plus the *other* party's public-facing
    /// profile fields (never the caller's own), ready to map into MatchSummaryResponse.
    /// </summary>
    public class MatchSummary
    {
        public MatchSummary()
        {

        }
        public Guid matchid { get; set; }
        public DateTime matched_at_utc { get; set; }
        public Guid userid { get; set; }
        public string public_identifier { get; set; }
        public string full_name { get; set; }
        public string headline { get; set; }
        public string profile_pic_url { get; set; }
        public string occupation { get; set; }
        public string company { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
    }
}
