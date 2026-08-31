using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Interface
{
    public interface IMatchRepository
    {
        /// <summary>
        /// Candidate pool for recommendations - excludes the given user, anyone they've already swiped on,
        /// and anyone blocked in either direction (see BlockUser - blocking is mutually invisible).
        /// Age (birthdate), industry, and location are pushed down as hard SQL WHERE filters (indexed - see
        /// UserEntityConfiguration) rather than loaded and filtered in memory, so the query cost stays
        /// bounded regardless of total active-user count. See MatchRepository.GetCandidates for the exact
        /// location-filtering priority (radius > city/state/country > none).
        /// `offset`/`limit` are a plain DB-level page over the filtered, ordered result set - callers use
        /// this to grow a cached batch incrementally rather than materializing the whole filtered pool at
        /// once (see MatchService.GetRecommendations).
        /// </summary>
        Task<IEnumerable<UserRequest>> GetCandidates(Guid userid, MatchCriteria criteria, int offset, int limit);
        /// <summary>
        /// Same filters as GetCandidates, without loading/ordering/paging any rows - lets callers report an
        /// accurate total_count/has_more for a bounded, incrementally-grown cache without materializing the
        /// whole filtered pool just to count it.
        /// </summary>
        Task<int> CountCandidates(Guid userid, MatchCriteria criteria);
        Task RecordSwipe(Guid userid, Guid targetUserId, SwipeAction action);
        /// <summary>
        /// Whether `likerUserId` has previously liked `targetUserId`.
        /// </summary>
        Task<bool> HasLiked(Guid likerUserId, Guid targetUserId);
        Task<Guid> CreateMatch(Guid userIdA, Guid userIdB);
        /// <summary>
        /// The given user's current matches (most recent first), each carrying the *other* party's
        /// public-facing profile fields. `offset`/`limit` are a plain DB-level page over
        /// matched_at_utc-descending order - same reasoning as GetCandidates: a popular/long-tenured user's
        /// full match list is never materialized just to serve one page of it (see MatchService.GetMatches).
        /// </summary>
        Task<IEnumerable<MatchSummary>> GetMatches(Guid userid, int offset, int limit);
        /// <summary>
        /// Total match count for `userid` - lets MatchService report total_count/has_more without loading
        /// every match row, same reasoning as CountCandidates.
        /// </summary>
        Task<int> CountMatches(Guid userid);
        /// <summary>
        /// Dissolves a match. Only succeeds (returns true) if `userid` is actually one side of `matchId` -
        /// this is what stops one user from unmatching a pair they aren't part of.
        /// </summary>
        Task<bool> Unmatch(Guid userid, Guid matchId);
        /// <summary>
        /// Blocks `blockedUserId` on behalf of `blockerUserId`: dissolves any existing match between the
        /// pair and records the block. Visibility is enforced as mutual regardless of which side placed
        /// it - see GetCandidates. Idempotent - blocking an already-blocked user is a no-op past the match
        /// teardown.
        /// </summary>
        Task BlockUser(Guid blockerUserId, Guid blockedUserId);
        /// <summary>
        /// "Who liked me" - everyone who has liked `userid` that `userid` hasn't swiped on back yet (once
        /// they respond, either direction, it's no longer pending - it's a match or a pass). Excludes
        /// deactivated users and anyone blocked in either direction, same as GetCandidates. `criteria`'s
        /// min_age/max_age are pushed down as a hard SQL filter, same semantics/reasoning as GetCandidates
        /// (unknown birthdate excluded whenever either bound is set). `offset`/`limit` page over
        /// most-recently-liked-first order at the DB level, so a popular user's full incoming-likes list is
        /// never materialized just to serve one page - see MatchService.GetIncomingLikes.
        /// </summary>
        Task<IEnumerable<UserRequest>> GetIncomingLikes(Guid userid, MatchCriteria criteria, int offset, int limit);
        /// <summary>
        /// Total incoming-like count for `userid` under the same filters as GetIncomingLikes, without
        /// loading/ordering/paging any rows.
        /// </summary>
        Task<int> CountIncomingLikes(Guid userid, MatchCriteria criteria);
        /// <summary>
        /// Everyone `userid` has previously passed on - deliberately excluded from GetCandidates so they
        /// never resurface in the regular deck, but explicitly retrievable here for the "see profiles you
        /// already passed on" recovery option once a user runs out of fresh candidates. Excludes deactivated
        /// users and anyone blocked in either direction, same as GetCandidates. Liked users aren't included
        /// (a like can only ever lead to a match, never appear here). Same age hard-filter and
        /// offset/limit DB-level paging (most-recently-passed-first) as GetIncomingLikes.
        /// </summary>
        Task<IEnumerable<UserRequest>> GetPassedCandidates(Guid userid, MatchCriteria criteria, int offset, int limit);
        /// <summary>
        /// Total passed-candidate count for `userid` under the same filters as GetPassedCandidates, without
        /// loading/ordering/paging any rows.
        /// </summary>
        Task<int> CountPassedCandidates(Guid userid, MatchCriteria criteria);
    }
}
