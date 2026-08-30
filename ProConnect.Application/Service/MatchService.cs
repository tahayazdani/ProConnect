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
        private readonly Cache _cache;
        private readonly ILogger<MatchService> _logger;

        public MatchService(
            IMatchCriteriaRepository matchCriteriaRepository,
            IMatchRepository matchRepository,
            IUserRepository userRepository,
            Cache cache,
            ILogger<MatchService> logger)
        {
            _matchCriteriaRepository = matchCriteriaRepository;
            _matchRepository = matchRepository;
            _userRepository = userRepository;
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
                max_age = stored?.max_age
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

                var criteria = new MatchCriteria
                {
                    userid = userid,
                    city = request.city,
                    state = request.state,
                    country = request.country,
                    industry = request.industry,
                    min_age = request.min_age,
                    max_age = request.max_age,
                    interests = request.interests
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

        public async Task<ProConnectResponse<List<MatchCandidateResponse>>> GetRecommendations(Guid userid)
        {
            try
            {
                var criteria = await ResolveEffectiveCriteria(userid);

                var recommendations = await _cache.Get(RecommendationCacheKey(userid), async () =>
                {
                    var candidates = await _matchRepository.GetCandidates(userid);

                    return (candidates ?? Enumerable.Empty<UserRequest>())
                        .Where(c => c != null && c.userid != userid)
                        .Where(c => IsWithinAgeRange(criteria, c))
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
                        .OrderByDescending(c => c.score)
                        .ToList();
                }, CacheRegion.MATCHRECOMMENDATION, CacheExpiry.MatchRecommendation);

                return new ProConnectResponse<List<MatchCandidateResponse>>
                {
                    body = recommendations ?? new List<MatchCandidateResponse>(),
                    message = "Recommendations have been fetched successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetRecommendations failed for userid '{userid}'.");
                throw;
            }
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

                await _matchRepository.RecordSwipe(userid, targetUserId, action);

                if (action != SwipeAction.Like)
                    return new ProConnectResponse<SwipeResponse>
                    {
                        body = new SwipeResponse { ismatch = false },
                        message = "Swipe recorded."
                    };

                var targetAlreadyLikedUser = await _matchRepository.HasLiked(targetUserId, userid);
                if (!targetAlreadyLikedUser)
                    return new ProConnectResponse<SwipeResponse>
                    {
                        body = new SwipeResponse { ismatch = false },
                        message = "Like recorded."
                    };

                var matchId = await _matchRepository.CreateMatch(userid, targetUserId);

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

        private static bool IsWithinAgeRange(MatchCriteria criteria, UserRequest candidate)
        {
            if (criteria.min_age is null && criteria.max_age is null)
                return true;

            var age = CalculateAge(candidate.birth_date);
            if (age is null)
                return false;

            if (criteria.min_age.HasValue && age < criteria.min_age.Value)
                return false;

            if (criteria.max_age.HasValue && age > criteria.max_age.Value)
                return false;

            return true;
        }

        private static int? CalculateAge(date birthdate)
        {
            if (birthdate is null)
                return null;

            var str = $"{birthdate.year}-{birthdate.month}-{birthdate.day}";
            if (!DateTime.TryParse(str, out var birth))
                return null;

            var today = DateTime.UtcNow;
            var age = today.Year - birth.Year;
            if (birth.Date > today.AddYears(-age))
                age--;

            return age;
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
                interests = criteria.interests
            };
        }
    }
}
