using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Application.Interface
{
    public interface IMatchService
    {
        Task<ProConnectResponse<MatchCriteriaResponse>> GetCriteria(Guid userid);
        Task<ProConnectResponse<MatchCriteriaResponse>> UpdateCriteria(Guid userid, MatchCriteriaRequest request);
        /// <summary>
        /// Paged - age/industry/location are hard-filtered and ordered in SQL (see
        /// MatchRepository.GetCandidates), so what's cached per user is only a bounded window
        /// (MatchRecommendationBatch, ~RecommendationBatchFetchSize rows) into that filtered result set, not
        /// the whole matching pool. The window grows via a cheap offset-paged DB call as a user pages
        /// through it, refilling proactively in the background once few unseen rows remain so paging never
        /// waits on the DB. Cached for CacheExpiry.MatchRecommendation (15 min) per user. Set request.refresh
        /// to force an immediate rebuild instead of waiting out the TTL (e.g. after criteria change).
        /// </summary>
        Task<ProConnectResponse<MatchCandidatePageResponse>> GetRecommendations(Guid userid, MatchCandidatePageRequest request);
        Task<ProConnectResponse<SwipeResponse>> Like(Guid userid, Guid targetUserId);
        Task<ProConnectResponse<SwipeResponse>> Pass(Guid userid, Guid targetUserId);
        /// <summary>
        /// Paged (like GetRecommendations/GetIncomingLikes/GetSeenProfiles) rather than returning every
        /// match in one response - most-recent-match-first, DB-level offset/limit (see
        /// IMatchRepository.GetMatches) so a long-tenured/highly-active user's full match history is never
        /// materialized just to serve one page. Never cached (matches change relatively rarely per user, and
        /// this isn't part of the hot swiping path), so request.refresh is a no-op here, same as
        /// GetIncomingLikes/GetSeenProfiles.
        /// </summary>
        Task<ProConnectResponse<MatchSummaryPageResponse>> GetMatches(Guid userid, MatchCandidatePageRequest request);
        /// <summary>
        /// Unilateral - either side of a match can end it at any time, no agreement from the other party
        /// required (standard for dating apps). Does not affect future search visibility, since the
        /// existing swipe records already keep an unmatched pair from being re-recommended to each other.
        /// </summary>
        Task<ProConnectResponse<string>> Unmatch(Guid userid, Guid matchId);
        /// <summary>
        /// Blocking is mutual: neither side will ever be recommended the other again, and any existing
        /// match between them is dissolved as part of the same action - see IMatchRepository.BlockUser.
        /// </summary>
        Task<ProConnectResponse<string>> BlockUser(Guid userid, Guid targetUserId);
        /// <summary>
        /// "Who liked me" - candidates who've already liked `userid` and are waiting on a response.
        /// Age-hard-filtered and DB-level offset/limit paged (most-recent-like-first) the same way
        /// GetRecommendations' candidate pool is - see IMatchRepository.GetIncomingLikes - so a popular
        /// user's full incoming-likes list is never materialized just to serve one page. Each returned item
        /// still carries a soft relevance score (city/state/country/industry/interest match against
        /// criteria) for display, computed only over the page returned, not the whole pool. Liking one back
        /// here creates a match immediately, same as a normal Like swipe would. Never cached (request.refresh
        /// is a no-op here), so there's nothing to force-refresh.
        /// </summary>
        Task<ProConnectResponse<MatchCandidatePageResponse>> GetIncomingLikes(Guid userid, MatchCandidatePageRequest request);
        /// <summary>
        /// "See profiles you already passed on" - the explicit recovery option once a user's regular deck
        /// (GetRecommendations) reports has_more = false and they don't want to change criteria instead.
        /// Age-hard-filtered and DB-level offset/limit paged (most-recent-pass-first), same reasoning as
        /// GetIncomingLikes, but pulled from the user's own pass history rather than GetCandidates, since
        /// passed profiles are permanently excluded from the regular deck. Never cached (like
        /// GetIncomingLikes) since it's only ever called on-demand, not part of the hot swiping path -
        /// request.refresh is a no-op. Liking a profile from this list works exactly like liking one from the
        /// normal deck (same Like endpoint, same match logic).
        /// </summary>
        Task<ProConnectResponse<MatchCandidatePageResponse>> GetSeenProfiles(Guid userid, MatchCandidatePageRequest request);
        /// <summary>
        /// Best-effort proactive cache warm-up for GetRecommendations - not a client-facing endpoint, meant
        /// to be fired (fire-and-forget) by UserService right after a session-establishing event
        /// (sign up / sign in / the "/me" check) so the recommendation window is already sitting in Redis
        /// by the time the client actually requests the deck, instead of paying the first GetCandidates
        /// round trip inside that request's critical path. A no-op if a window is already cached (never
        /// forces a refresh - that is what MatchCandidatePageRequest.refresh is for), and swallows/logs its
        /// own failures rather than throwing, since a failed warm-up should never be visible to the caller
        /// that triggered it - the cold path in GetRecommendations is still a correct fallback.
        /// </summary>
        Task WarmRecommendations(Guid userid);
    }
}
