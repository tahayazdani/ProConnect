namespace ProConnect.Domain.Model
{
    /// <summary>
    /// Result of IFeatureGateService.TryConsume - lets a call site (e.g. MatchService.Swipe's Like path)
    /// branch on whether the attempt was allowed and, if it wasn't, surface the limit in its own error
    /// message without a second round trip to ResolveEffectivePlan.
    /// </summary>
    public class FeatureConsumeResult
    {
        public FeatureConsumeResult()
        {

        }
        public bool allowed { get; set; }

        /// <summary>Null when the feature itself isn't enabled on the user's plan at all (allowed is
        /// always false in that case) or when it's enabled with no cap.</summary>
        public int? limit { get; set; }

        /// <summary>Usage *after* this call if allowed; usage as it stood *before* this call (i.e.
        /// already at/over limit) if not allowed - either way, "how many have you used so far".</summary>
        public int used { get; set; }
    }
}
