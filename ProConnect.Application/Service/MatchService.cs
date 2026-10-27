using Microsoft.Extensions.Logging;
using ProConnect.Application.Interface;
using ProConnect.Domain.Const;
using ProConnect.Domain.Model;
using ProConnect.Domain.Request;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Application.Service
{
    public class MatchService : IMatchService
    {
        // weighted scoring - see PROJECT_NOTES.md for the reasoning (rule-based, cold-start friendly)
        // location tiers are additive (not else-if): same city implies same state and country, so an
        // exact city match naturally scores highest without leaving state/country-only matches at zero.
        private const double CityMatchScore = 40;
        private const double StateMatchScore = 20;
        private const double CountryMatchScore = 10;
        private const double IndustryMatchScore = 25;
        private const double InterestMatchScore = 15;
        private const double MaxInterestScore = 60;

        private readonly IMatchCriteriaRepository _matchCriteriaRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IUserRepository _userRepository;
        private readonly IFeatureGateService _featureGateService;
        private readonly INotificationService _notificationService;
        private readonly Cache _cache;
        private readonly ILogger<MatchService> _logger;

        public MatchService(
            IMatchCriteriaRepository matchCriteriaRepository,
            IMatchRepository matchRepository,
            IUserRepository userRepository,
            IFeatureGateService featureGateService,
            INotificationService notificationService,
            Cache cache,
            ILogger<MatchService> logger)
        {
            _matchCriteriaRepository = matchCriteriaRepository;
            _matchRepository = matchRepository;
            _userRepository = userRepository;
            _featureGateService = featureGateService;
            _notificationService = notificationService;
            _cache = cache;
            _logger = logger;
        }

        public async Task<ProConnectResponse<MatchCriteriaResponse>> GetCriteria(Guid userid)
        {
            try
            {
                var criteria = await ResolveEffectiveCriteria(userid);

                return new ProConnectResponse<MatchCriteriaResponse>
                {
                    body = MapToResponse(criteria),
                    message = "Match criteria have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetCriteria failed for userid '{userid}'.");
                throw;
            }
        }

        /// <summary>
        /// Merges explicit overrides (if any were saved) with defaults derived from the user's own LinkedIn
        /// profile, so recommendations work out of the box without an onboarding questionnaire. Only
        /// min_age/max_age have no profile-based default, since LinkedIn carries no dating-age preference.
        /// </summary>
        private async Task<MatchCriteria> ResolveEffectiveCriteria(Guid userid)
        {
            var stored = await _matchCriteriaRepository.GetByUserId(userid);
            var profile = await _userRepository.GetById(userid);

            return new MatchCriteria
            {
                userid = userid,
                city = stored?.city ?? profile?.city,
                state = stored?.state ?? profile?.state,
                country = stored?.country ?? profile?.country,
                industry = stored?.industry ?? profile?.industry,
                interests = (stored?.interests is { Length: > 0 }) ? stored.interests : profile?.interests,
                min_age = stored?.min_age,
                max_age = stored?.max_age,
                // no profile-based default, same reasoning as min_age/max_age - LinkedIn carries no
                // "how far am I willing to travel" preference.
                radius_km = stored?.radius_km
            };
        }

        public async Task<ProConnectResponse<MatchCriteriaResponse>> UpdateCriteria(Guid userid, MatchCriteriaRequest request)
        {
            try
            {
                if (request is null)
                    return new ProConnectResponse<MatchCriteriaResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Request body is required.",
                        message = "Request body is required."
                    };

                if (request.min_age.HasValue && request.max_age.HasValue && request.min_age > request.max_age)
                    return new ProConnectResponse<MatchCriteriaResponse>
                    {
                        issuccessfull = false,
                        errormessage = "min_age cannot be greater than max_age.",
                        message = "min_age cannot be greater than max_age."
                    };

                if (request.radius_km.HasValue && request.radius_km.Value <= 0)
                    return new ProConnectResponse<MatchCriteriaResponse>
                    {
                        issuccessfull = false,
                        errormessage = "radius_km must be greater than zero.",
                        message = "radius_km must be greater than zero."
                    };

                // Radius-based search is a paid-tier feature (see Const.FeatureKey.RadiusSearchEnabled) -
                // a Free user attempting to set it gets a clear rejection rather than having it silently
                // dropped or silently honored.
                if (request.radius_km.HasValue && !await _featureGateService.IsEnabled(userid, FeatureKey.RadiusSearchEnabled))
                    return new ProConnectResponse<MatchCriteriaResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Radius-based search is a premium feature. Upgrade your plan to filter by distance.",
                        message = "Radius-based search is a premium feature. Upgrade your plan to filter by distance."
                    };

                var criteria = new MatchCriteria
                {
                    userid = userid,
                    city = request.city,
                    state = request.state,
                    country = request.country,
                    industry = request.industry,
                    min_age = request.min_age,
                    max_age = request.max_age,
                    interests = request.interests,
                    radius_km = request.radius_km
                };

                await _matchCriteriaRepository.Upsert(criteria);

                // existing cached recommendations no longer reflect the new criteria
                await _cache.Delete(RecommendationCacheKey(userid), CacheRegion.MATCHRECOMMENDATION);

                return new ProConnectResponse<MatchCriteriaResponse>
                {
                    body = MapToResponse(await ResolveEffectiveCriteria(userid)),
                    message = "Match criteria have been updated successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"UpdateCriteria failed for userid '{userid}'.");
                throw;
            }
        }

        // How many DB rows a single GetCandidates fetch pulls at a time. Deliberately much bigger than any
        // realistic pagesize (max 50, see MatchCandidatePageRequestValidator) so most sessions never need a
        // second DB round trip at all.
        private const int RecommendationBatchFetchSize = 100;

        // Once fewer than this many unseen items are left in the cached batch, kick off a background
        // refill so the batch never actually runs dry while a user is still swiping through it - see
        // TriggerBackgroundRefill.
        private const int RecommendationRefillThreshold = 20;

        public async Task<ProConnectResponse<MatchCandidatePageResponse>> GetRecommendations(Guid userid, MatchCandidatePageRequest request)
        {
            try
            {
                request ??= new MatchCandidatePageRequest();
                var criteria = await ResolveEffectiveCriteria(userid);

                // refresh bypasses the cached batch entirely so the very next line rebuilds it from scratch
                // instead of serving whatever's still sitting in Redis - see MatchCandidatePageRequest.refresh
                // for when a client is expected to set this.
                if (request.refresh)
                    await _cache.Delete(RecommendationCacheKey(userid), CacheRegion.MATCHRECOMMENDATION);

                var neededThrough = request.page * request.pagesize;

                var batch = await _cache.Get<MatchRecommendationBatch>(RecommendationCacheKey(userid), CacheRegion.MATCHRECOMMENDATION)
                    ?? new MatchRecommendationBatch();

                // Grow synchronously only if the page being asked for right now isn't covered yet (e.g. cold
                // start, or a client jumping straight to a deep page) - this is the only path that can make
                // a caller wait on GetCandidates; everything else is served from what's already cached.
                if (batch.Items.Count < neededThrough && !batch.ExhaustedPool)
                    batch = await GrowBatch(userid, criteria, batch);

                var page = BuildPageFromBatch(batch, request);

                // Proactively top up the cache once we're close to the end of what's cached, so the *next*
                // page request almost never has to wait on GetCandidates itself - see TriggerBackgroundRefill.
                var remainingAfterThisPage = batch.Items.Count - neededThrough;
                if (!batch.ExhaustedPool && remainingAfterThisPage < RecommendationRefillThreshold)
                    TriggerBackgroundRefill(userid, criteria, batch);

                return new ProConnectResponse<MatchCandidatePageResponse>
                {
                    body = page,
                    message = "Recommendations have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetRecommendations failed for userid '{userid}'.");
                throw;
            }
        }

        // Pulls the next RecommendationBatchFetchSize rows starting at batch.NextOffset, appends them, and
        // persists the grown batch back to Redis. TotalCount is refreshed on every grow so
        // MatchCandidatePageResponse.total_count/has_more stay accurate as candidates come and go, without
        // ever loading the full matching pool just to count it - see IMatchRepository.CountCandidates.
        private async Task<MatchRecommendationBatch> GrowBatch(Guid userid, MatchCriteria criteria, MatchRecommendationBatch batch)
        {
            var fetched = (await _matchRepository.GetCandidates(userid, criteria, batch.NextOffset, RecommendationBatchFetchSize))?.ToList()
                ?? new List<UserRequest>();

            batch.Items.AddRange(fetched.Select(c => MapToCandidate(criteria, c)));
            batch.NextOffset += fetched.Count;
            batch.ExhaustedPool = fetched.Count < RecommendationBatchFetchSize;
            batch.TotalCount = await _matchRepository.CountCandidates(userid, criteria);

            await _cache.Set(RecommendationCacheKey(userid), batch, CacheRegion.MATCHRECOMMENDATION, CacheExpiry.MatchRecommendation);

            return batch;
        }

        // Fire-and-forget: all Match/Cache services are registered Singleton (see Program.cs/DI.cs), so
        // capturing them in a detached Task is safe - there's no scoped DbContext or request-lifetime
        // dependency to be torn down out from under it. Runs GrowBatch against a snapshot of the batch that
        // was already served to the caller; if two refills race, the loser's Set just overwrites with an
        // equally-valid (slightly different offset ordering) grown batch - never a correctness issue, only
        // a wasted DB call in the rare concurrent-request case.
        private void TriggerBackgroundRefill(Guid userid, MatchCriteria criteria, MatchRecommendationBatch batch)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await GrowBatch(userid, criteria, batch);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Background recommendation refill failed for userid '{userid}'.");
                }
            });
        }

        // Unlike ScoreCandidate (used by GetIncomingLikes/GetSeenProfiles), city/state/country/industry/age
        // are no longer scored here - they're already hard-filtered in SQL by MatchRepository.GetCandidates,
        // so every candidate in the batch already satisfies them. Only interests remain as a soft ranking
        // signal, since there's no SQL-level interest filter.
        private static MatchCandidateResponse MapToCandidate(MatchCriteria criteria, UserRequest candidate)
        {
            double score = 0;

            if (criteria.interests != null && candidate.interests != null)
            {
                var sharedInterestCount = criteria.interests
                    .Where(i => !string.IsNullOrEmpty(i))
                    .Count(i => candidate.interests.Any(ci => string.Equals(ci, i, StringComparison.OrdinalIgnoreCase)));

                score += Math.Min(sharedInterestCount * InterestMatchScore, MaxInterestScore);
            }

            return new MatchCandidateResponse
            {
                userid = candidate.userid,
                public_identifier = candidate.public_identifier,
                full_name = candidate.full_name,
                headline = candidate.headline,
                profile_pic_url = candidate.profile_pic_url,
                occupation = candidate.occupation,
                company = candidate.company,
                city = candidate.city,
                state = candidate.state,
                country = candidate.country,
                score = score
            };
        }

        // Unlike BuildPage (which slices an already-fully-materialized list for GetIncomingLikes/
        // GetSeenProfiles), this slices a bounded, incrementally-grown batch - total_count/has_more come
        // from the batch's own TotalCount (refreshed on every GrowBatch call), not from all.Count, since the
        // batch only ever holds a window into the full matching pool.
        private static MatchCandidatePageResponse BuildPageFromBatch(MatchRecommendationBatch batch, MatchCandidatePageRequest request)
        {
            var items = batch.Items
                .Skip((request.page - 1) * request.pagesize)
                .Take(request.pagesize)
                .ToList();

            return new MatchCandidatePageResponse
            {
                results = items,
                total_count = batch.TotalCount,
                page = request.page,
                pagesize = request.pagesize,
                has_more = (long)request.page * request.pagesize < batch.TotalCount
            };
        }

        public async Task<ProConnectResponse<SwipeResponse>> Like(Guid userid, Guid targetUserId)
            => await Swipe(userid, targetUserId, SwipeAction.Like);

        public async Task<ProConnectResponse<SwipeResponse>> Pass(Guid userid, Guid targetUserId)
            => await Swipe(userid, targetUserId, SwipeAction.Pass);

        private async Task<ProConnectResponse<SwipeResponse>> Swipe(Guid userid, Guid targetUserId, SwipeAction action)
        {
            try
            {
                if (targetUserId == Guid.Empty)
                    return new ProConnectResponse<SwipeResponse>
                    {
                        issuccessfull = false,
                        errormessage = "A valid target user is required.",
                        message = "A valid target user is required."
                    };

                if (targetUserId == userid)
                    return new ProConnectResponse<SwipeResponse>
                    {
                        issuccessfull = false,
                        errormessage = "You cannot swipe on yourself.",
                        message = "You cannot swipe on yourself."
                    };

                // BUG-03 fix: GetById is_active-filtered (same as GetCandidates), so this also rejects
                // swiping on a deactivated user, not just a never-existed one - either way there's no
                // profile to actually swipe on, and the old code let this fall through to RecordSwipe's FK
                // constraint, which raised a raw Npgsql 23503 foreign-key-violation 500 instead of a clean
                // error.
                var targetProfile = await _userRepository.GetById(targetUserId);
                if (targetProfile is null)
                    return new ProConnectResponse<SwipeResponse>
                    {
                        issuccessfull = false,
                        errormessage = "Target user not found.",
                        message = "Target user not found."
                    };

                // BUG-08 fix: GetCandidates/GetIncomingLikes/GetPassedCandidates already excluded blocked
                // pairs from ever being *shown*, but a client that already had a blocked user's id (e.g.
                // from before the block was placed, or a replayed/forged request) could still swipe on
                // them directly - this closes that gap with the same mutual-blocking semantics.
                if (await _matchRepository.IsBlockedEitherDirection(userid, targetUserId))
                    return new ProConnectResponse<SwipeResponse>
                    {
                        issuccessfull = false,
                        errormessage = "You can't swipe on this user.",
                        message = "You can't swipe on this user."
                    };

                // Daily like cap (see Const.FeatureKey.DailyLikes) only applies to Likes - Pass is always
                // free and unmetered. Checked (and, on success, incremented) before RecordSwipe so a
                // denied like never gets persisted.
                //
                // BUG-04/BUG-11 fix: this consume is optimistic - it happens before we know whether
                // RecordSwipe's write is actually a net-new Like (as opposed to a repeat Like already on
                // record for this exact pair). We can't check that first without reopening the same race
                // (two concurrent identical Like calls could both observe "not yet liked" before either
                // writes), so instead RecordSwipe itself resolves this race-free (via an advisory lock -
                // see MatchRepository) and tells us whether it was genuinely new; if it wasn't, we
                // refund the unit we optimistically charged. A denied Like still never gets persisted,
                // since TryConsume runs (and can short-circuit) before RecordSwipe is ever called.
                if (action == SwipeAction.Like)
                {
                    var consumeResult = await _featureGateService.TryConsume(userid, FeatureKey.DailyLikes);
                    if (!consumeResult.allowed)
                        return new ProConnectResponse<SwipeResponse>
                        {
                            issuccessfull = false,
                            errormessage = consumeResult.limit.HasValue
                                ? $"You've reached your daily like limit ({consumeResult.limit}). Upgrade your plan or come back later."
                                : "Liking is not available on your current plan.",
                            message = "Daily like limit reached."
                        };
                }

                var (wasNewLike, previousAction) = await _matchRepository.RecordSwipe(userid, targetUserId, action);

                // Re-Like of an already-Like row (BUG-11): we just optimistically charged above but
                // RecordSwipe says this wasn't actually a new Like, so give the unit back.
                if (action == SwipeAction.Like && !wasNewLike)
                    await _featureGateService.RefundConsumption(userid, FeatureKey.DailyLikes);

                // Like -> non-Like (BUG-04): the original Like's row just got overwritten by this Pass
                // (or any other non-Like action), erasing the evidence that justified its quota charge.
                // No consume happens on this call (only Like consumes), so without this the charge from
                // the earlier Like would stick around forever with no corresponding Like row to reconcile
                // against - used_count silently drifting above COUNT(*) FROM swipes WHERE action = Like.
                // Refunding here keeps the two in sync regardless of how many times a user flips their
                // swipe on the same target.
                if (action != SwipeAction.Like && previousAction == SwipeAction.Like)
                    await _featureGateService.RefundConsumption(userid, FeatureKey.DailyLikes);

                if (action != SwipeAction.Like)
                    return new ProConnectResponse<SwipeResponse>
                    {
                        body = new SwipeResponse { ismatch = false },
                        message = "Swipe recorded."
                    };

                var targetAlreadyLikedUser = await _matchRepository.HasLiked(targetUserId, userid);
                if (!targetAlreadyLikedUser)
                {
                    // Found during BUG-11 reverification: NotifyNewLike used to fire on every Like call
                    // that reaches this branch, including a re-Like of a target already Liked (wasNewLike
                    // false) - so N concurrent/duplicate Like taps on the same never-reciprocated target
                    // (a double-tap, a flaky-network retry, the exact BUG-11 race scenario) produced N
                    // duplicate "someone liked you" notifications for what is, and should remain, a single
                    // event. Gating on wasNewLike makes this consistent with the quota-refund gating just
                    // above: a re-Like is a no-op in every observable way, not just for billing.
                    //
                    // Fires for every *new* like regardless of subscriber tier - see NotifyNewLike remarks
                    // for why this stays deliberately generic (no liker identity) despite that.
                    if (wasNewLike)
                        await NotifyNewLike(targetUserId);

                    return new ProConnectResponse<SwipeResponse>
                    {
                        body = new SwipeResponse { ismatch = false },
                        message = "Like recorded."
                    };
                }

                var (matchId, created) = await _matchRepository.CreateMatch(userid, targetUserId);

                // BUG-12 fix: only notify when this call actually created the match - CreateMatch is
                // idempotent and race-safe (advisory lock on the canonicalized pair - see MatchRepository),
                // so when both sides' reciprocal Like land at nearly the same time, exactly one of the two
                // Swipe calls sees created == true and fires the notification; the other sees the
                // already-existing match and skips it, instead of both firing and producing duplicate
                // "It's a match!" notifications for the same pair - reproduced in BUGS_FOUND.md BUG-12.
                if (created)
                    await NotifyNewMatch(userid, targetUserId, matchId);

                return new ProConnectResponse<SwipeResponse>
                {
                    body = new SwipeResponse { ismatch = true, matchid = matchId },
                    message = "It's a match!"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Swipe ({action}) failed for userid '{userid}' -> targetUserId '{targetUserId}'.");
                throw;
            }
        }

        // Fires to both sides once CreateMatch has already durably succeeded - matching itself reveals both
        // parties' identities to each other (unlike a one-sided like), so naming the other person in the
        // body is fine here. INotificationService.Notify itself never throws, but the two GetById lookups
        // for display names could, so this whole helper is wrapped the same "log and swallow" way
        // NotificationService's own TryPushNotify/TryLiveNotify are - a notification-text failure must
        // never unwind the match that was just recorded.
        private async Task NotifyNewMatch(Guid userid, Guid targetUserId, Guid matchId)
        {
            try
            {
                var user = await _userRepository.GetById(userid);
                var target = await _userRepository.GetById(targetUserId);

                await _notificationService.Notify(userid, NotificationType.NewMatch, "It's a match!",
                    !string.IsNullOrWhiteSpace(target?.full_name) ? $"You and {target.full_name} matched!" : "You have a new match!",
                    matchId);

                await _notificationService.Notify(targetUserId, NotificationType.NewMatch, "It's a match!",
                    !string.IsNullOrWhiteSpace(user?.full_name) ? $"You and {user.full_name} matched!" : "You have a new match!",
                    matchId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"NotifyNewMatch failed for userid '{userid}' <-> targetUserId '{targetUserId}' (match '{matchId}' was still recorded).");
            }
        }

        // Fires for every like, regardless of the liked user's subscriber tier - unlike NotifyNewMatch,
        // "who liked me" visibility is still gated behind FeatureKey.WhoLikedMeVisible (see
        // GetIncomingLikes), so this deliberately carries no liker identity and no related_entity_id - a
        // push notification that revealed the liker's userid would let a Free user resolve/view that
        // profile directly and bypass the paywall entirely. Generic text only.
        private async Task NotifyNewLike(Guid targetUserId)
        {
            try
            {
                await _notificationService.Notify(targetUserId, NotificationType.NewLike, "Someone likes you!",
                    "You have a new like waiting for you.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"NotifyNewLike failed for targetUserId '{targetUserId}'.");
            }
        }

        public async Task<ProConnectResponse<MatchSummaryPageResponse>> GetMatches(Guid userid, MatchCandidatePageRequest request)
        {
            try
            {
                request ??= new MatchCandidatePageRequest();

                var offset = (request.page - 1) * request.pagesize;
                var matches = await _matchRepository.GetMatches(userid, offset, request.pagesize);
                var totalCount = await _matchRepository.CountMatches(userid);

                var results = (matches ?? Enumerable.Empty<MatchSummary>())
                    .Select(m => new MatchSummaryResponse
                    {
                        matchid = m.matchid,
                        matched_at_utc = m.matched_at_utc,
                        userid = m.userid,
                        public_identifier = m.public_identifier,
                        full_name = m.full_name,
                        headline = m.headline,
                        profile_pic_url = m.profile_pic_url,
                        occupation = m.occupation,
                        company = m.company,
                        city = m.city,
                        state = m.state,
                        country = m.country
                    })
                    .ToList();

                return new ProConnectResponse<MatchSummaryPageResponse>
                {
                    body = new MatchSummaryPageResponse
                    {
                        results = results,
                        total_count = totalCount,
                        page = request.page,
                        pagesize = request.pagesize,
                        has_more = (long)request.page * request.pagesize < totalCount
                    },
                    message = "Matches have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetMatches failed for userid '{userid}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> Unmatch(Guid userid, Guid matchId)
        {
            try
            {
                var unmatched = await _matchRepository.Unmatch(userid, matchId);
                if (!unmatched)
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "Match not found.",
                        message = "Match not found."
                    };

                _logger.LogInformation($"Userid '{userid}' unmatched matchid '{matchId}'.");

                return new ProConnectResponse<string>
                {
                    body = "Unmatched",
                    message = "You are no longer matched."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unmatch failed for userid '{userid}' -> matchId '{matchId}'.");
                throw;
            }
        }

        public async Task<ProConnectResponse<string>> BlockUser(Guid userid, Guid targetUserId)
        {
            try
            {
                if (targetUserId == Guid.Empty)
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "A valid target user is required.",
                        message = "A valid target user is required."
                    };

                if (targetUserId == userid)
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "You cannot block yourself.",
                        message = "You cannot block yourself."
                    };

                // BUG-20 fix: unlike Swipe (BUG-03), there was no existence check at all here - a
                // syntactically-valid-but-nonexistent targetUserId fell straight through to
                // MatchRepository.BlockUser's insert, which hit the Blocks table's FK constraint and
                // surfaced as a raw unhandled-exception 500 (reproduced in BUGS_FOUND.md BUG-20).
                // Deliberately uses the unfiltered Exists check, not GetById - a deactivated target should
                // still be blockable (contrast Swipe, which should NOT let you swipe on a deactivated
                // user).
                if (!await _userRepository.Exists(targetUserId))
                    return new ProConnectResponse<string>
                    {
                        issuccessfull = false,
                        errormessage = "Target user not found.",
                        message = "Target user not found."
                    };

                await _matchRepository.BlockUser(userid, targetUserId);

                // blocking changes both sides' candidate pool immediately - drop any cached recommendations
                // so neither party sees the other again before the cache would naturally expire
                await _cache.Delete(RecommendationCacheKey(userid), CacheRegion.MATCHRECOMMENDATION);
                await _cache.Delete(RecommendationCacheKey(targetUserId), CacheRegion.MATCHRECOMMENDATION);

                _logger.LogInformation($"Userid '{userid}' blocked userid '{targetUserId}'.");

                return new ProConnectResponse<string>
                {
                    body = "Blocked",
                    message = "User has been blocked. You will no longer see each other in search or recommendations."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"BlockUser failed for userid '{userid}' -> targetUserId '{targetUserId}'.");
                throw;
            }
        }

        // Not cached, unlike GetRecommendations - this list should reflect a brand-new incoming like right
        // away rather than sitting behind the 15-minute MatchRecommendation TTL. Age hard-filtered and
        // DB-level offset/limit paged (most-recent-like-first) directly by MatchRepository - see
        // IMatchRepository.GetIncomingLikes - rather than materializing every pending like just to slice one
        // page, since a popular profile's incoming-likes pool can't be assumed small at scale.
        // request.refresh is a no-op here since there's no cache to bypass in the first place.
        public async Task<ProConnectResponse<MatchCandidatePageResponse>> GetIncomingLikes(Guid userid, MatchCandidatePageRequest request)
        {
            try
            {
                request ??= new MatchCandidatePageRequest();

                // "Who liked me" is a paid-tier feature (see Const.FeatureKey.WhoLikedMeVisible) - a
                // Free user gets an empty, zero-count page back rather than an error, so the client can
                // render an upsell state instead of having to special-case a failure response.
                if (!await _featureGateService.IsEnabled(userid, FeatureKey.WhoLikedMeVisible))
                    return new ProConnectResponse<MatchCandidatePageResponse>
                    {
                        body = new MatchCandidatePageResponse
                        {
                            results = new List<MatchCandidateResponse>(),
                            total_count = 0,
                            page = request.page,
                            pagesize = request.pagesize,
                            has_more = false
                        },
                        message = "Seeing who liked you is a premium feature. Upgrade your plan to view this list."
                    };

                var criteria = await ResolveEffectiveCriteria(userid);

                var offset = (request.page - 1) * request.pagesize;
                var likers = await _matchRepository.GetIncomingLikes(userid, criteria, offset, request.pagesize);
                var totalCount = await _matchRepository.CountIncomingLikes(userid, criteria);

                return new ProConnectResponse<MatchCandidatePageResponse>
                {
                    body = BuildPageFromScoredItems(likers, criteria, totalCount, request),
                    message = "Pending likes have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetIncomingLikes failed for userid '{userid}'.");
                throw;
            }
        }

        // Not cached, same reasoning as GetIncomingLikes - this is only ever called on-demand once a user
        // hits the end of their regular deck (or explicitly wants to revisit passes), not part of the hot
        // swiping path, so there's no TTL to justify caching against and request.refresh is a no-op. Age
        // hard-filtered and DB-level offset/limit paged (most-recent-pass-first), same reasoning as
        // GetIncomingLikes - see IMatchRepository.GetPassedCandidates.
        public async Task<ProConnectResponse<MatchCandidatePageResponse>> GetSeenProfiles(Guid userid, MatchCandidatePageRequest request)
        {
            try
            {
                request ??= new MatchCandidatePageRequest();
                var criteria = await ResolveEffectiveCriteria(userid);

                var offset = (request.page - 1) * request.pagesize;
                var passed = await _matchRepository.GetPassedCandidates(userid, criteria, offset, request.pagesize);
                var totalCount = await _matchRepository.CountPassedCandidates(userid, criteria);

                return new ProConnectResponse<MatchCandidatePageResponse>
                {
                    body = BuildPageFromScoredItems(passed, criteria, totalCount, request),
                    message = "Previously passed profiles have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetSeenProfiles failed for userid '{userid}'.");
                throw;
            }
        }

        // Shared by GetIncomingLikes and GetSeenProfiles - both already come back from the repository
        // pre-filtered (self/age/deactivated/blocked) and DB-ordered/paged, so unlike the old
        // MapToCandidateResponses+BuildPage pair, this only scores the page actually being returned
        // (city/state/country/industry/interest match against criteria - same weights as ScoreCandidate),
        // never the whole pool. total_count/has_more come from the repository's own CountIncomingLikes/
        // CountPassedCandidates call, not from the page size, so paging stays accurate.
        private static MatchCandidatePageResponse BuildPageFromScoredItems(
            IEnumerable<UserRequest> pageItems, MatchCriteria criteria, int totalCount, MatchCandidatePageRequest request)
        {
            var items = (pageItems ?? Enumerable.Empty<UserRequest>())
                .Select(c => new MatchCandidateResponse
                {
                    userid = c.userid,
                    public_identifier = c.public_identifier,
                    full_name = c.full_name,
                    headline = c.headline,
                    profile_pic_url = c.profile_pic_url,
                    occupation = c.occupation,
                    company = c.company,
                    city = c.city,
                    state = c.state,
                    country = c.country,
                    score = ScoreCandidate(criteria, c)
                })
                .ToList();

            return new MatchCandidatePageResponse
            {
                results = items,
                total_count = totalCount,
                page = request.page,
                pagesize = request.pagesize,
                has_more = (long)request.page * request.pagesize < totalCount
            };
        }

        private static double ScoreCandidate(MatchCriteria criteria, UserRequest candidate)
        {
            double score = 0;

            if (!string.IsNullOrEmpty(criteria.city) && string.Equals(criteria.city, candidate.city, StringComparison.OrdinalIgnoreCase))
                score += CityMatchScore;

            if (!string.IsNullOrEmpty(criteria.state) && string.Equals(criteria.state, candidate.state, StringComparison.OrdinalIgnoreCase))
                score += StateMatchScore;

            if (!string.IsNullOrEmpty(criteria.country) && string.Equals(criteria.country, candidate.country, StringComparison.OrdinalIgnoreCase))
                score += CountryMatchScore;

            if (!string.IsNullOrEmpty(criteria.industry) && !string.IsNullOrEmpty(candidate.industry)
                && string.Equals(criteria.industry, candidate.industry, StringComparison.OrdinalIgnoreCase))
                score += IndustryMatchScore;

            if (criteria.interests != null && candidate.interests != null)
            {
                var sharedInterestCount = criteria.interests
                    .Where(i => !string.IsNullOrEmpty(i))
                    .Count(i => candidate.interests.Any(ci => string.Equals(ci, i, StringComparison.OrdinalIgnoreCase)));

                score += Math.Min(sharedInterestCount * InterestMatchScore, MaxInterestScore);
            }

            return score;
        }

        // Age filtering for GetIncomingLikes/GetSeenProfiles moved into SQL (MatchRepository.ApplyAgeFilter)
        // as part of this session's scale pass, same as GetRecommendations' candidate pool - the old
        // in-memory IsWithinAgeRange/CalculateAge pair (birth_date-string parsing per candidate) is gone,
        // not just unused, since every caller of this file now gets already-age-filtered rows straight from
        // the repository.

        // See IMatchService.WarmRecommendations for the full rationale. Deliberately checks for an
        // existing, non-empty batch first rather than always calling GrowBatch - a user who already has a
        // warm/partially-consumed window shouldn't have it clobbered (and Redis shouldn't pay a write) just
        // because another session-establishing event fired again (e.g. token refresh right after sign-in).
        public async Task WarmRecommendations(Guid userid)
        {
            try
            {
                var existing = await _cache.Get<MatchRecommendationBatch>(RecommendationCacheKey(userid), CacheRegion.MATCHRECOMMENDATION);
                if (existing != null && existing.Items.Count > 0)
                    return;

                var criteria = await ResolveEffectiveCriteria(userid);
                await GrowBatch(userid, criteria, new MatchRecommendationBatch());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"WarmRecommendations failed for userid '{userid}'.");
            }
        }

        private static string RecommendationCacheKey(Guid userid) => $"recommendations:{userid}";

        private static MatchCriteriaResponse MapToResponse(MatchCriteria criteria)
        {
            return new MatchCriteriaResponse
            {
                city = criteria.city,
                state = criteria.state,
                country = criteria.country,
                industry = criteria.industry,
                min_age = criteria.min_age,
                max_age = criteria.max_age,
                interests = criteria.interests,
                radius_km = criteria.radius_km
            };
        }
    }
}
