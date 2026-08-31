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

        /// <summary>
        /// Optional "within X km of me" hard filter, centered on the user's own geocoded location - takes
        /// priority over city/state/country when set (see MatchRepository.GetCandidates). No profile-based
        /// default, same as min_age/max_age - LinkedIn carries no radius preference either.
        /// </summary>
        public double? radius_km { get; set; }
    }
}
