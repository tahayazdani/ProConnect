using System;

namespace ProConnect.Domain.Response
{
    public class MatchCandidateResponse
    {
        public MatchCandidateResponse()
        {

        }
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
        public double score { get; set; }
    }
}
