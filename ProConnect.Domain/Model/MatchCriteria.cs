using System;

namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Optional overrides on top of the user's own LinkedIn profile (see MatchService.ResolveEffectiveCriteria).
    /// Any field left null falls back to the equivalent profile field, so nobody has to fill this in to get matches.
    /// min_age/max_age have no profile equivalent - LinkedIn has no signal for dating-age preference.
    /// </summary>
    public class MatchCriteria
    {
        public MatchCriteria()
        {

        }
        public Guid userid { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public string industry { get; set; }
        public int? min_age { get; set; }
        public int? max_age { get; set; }
        public string[] interests { get; set; }
    }
}
