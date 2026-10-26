using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProConnect.Domain.Config;
using ProConnect.Domain.Response;
using ProConnect.Infrastructure.Interface;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ProConnect.Infrastructure.Provider
{
    public class HarvestApiProfileProvider : IProfileProvider
    {
        private readonly IHttpClientUtilities _httpClientUtilities;
        private readonly HarvestApiConfig _config;
        private readonly ILogger<HarvestApiProfileProvider> _logger;

        public HarvestApiProfileProvider(
            IHttpClientUtilities httpClientUtilities,
            IOptions<ProConnectConfig> proConnectConfig,
            ILogger<HarvestApiProfileProvider> logger)
        {
            _httpClientUtilities = httpClientUtilities;
            _config = proConnectConfig.Value?.harvestApiConfig
                ?? throw new InvalidOperationException("ProConnectConfig:HarvestApiConfig configuration section is missing.");
            _logger = logger;
        }

        public async Task<PersonProfileResponse> GetProfile(string publicIdentifierOrUrl)
        {
            if (string.IsNullOrEmpty(publicIdentifierOrUrl))
                return null;

            _logger.LogDebug($"Fetching LinkedIn profile via harvestapi for: '{publicIdentifierOrUrl}'");

            // run-sync-get-dataset-items runs the Actor and returns its dataset directly in the response,
            // instead of the usual start-a-run-then-poll flow - fits our synchronous signup path.
            var path = $"acts/{_config.actorid}/run-sync-get-dataset-items";
            var request = new
            {
                queries = new[] { publicIdentifierOrUrl },
                profileScraperMode = _config.profilescrapermode
            };

            var results = await _httpClientUtilities.PostAsync<List<HarvestApiProfileResult>>(
                _config.baseurl,
                path,
                request,
                TimeSpan.FromSeconds(_config.timeoutinseconds),
                _config.apitoken);

            var profile = results?.FirstOrDefault();

            if (profile is null)
                return null;

            // BUG-09 fix: the actor can return a dataset item that isn't actually a hit for what was
            // asked - either a "not found" placeholder/stub with every identity field empty, or (observed
            // live) a completely unrelated real profile. Accepting either silently used to let a signup
            // report success while persisting an empty or misattributed account - see BUGS_FOUND.md
            // BUG-09. Treating a rejected item the same as a null FirstOrDefault (return null) also means
            // it's never handed to Cache.Get as a value to cache (see Cache.Get remarks - only non-null
            // results get cached), so a transient scraper miss can't permanently poison
            // CacheRegion.PERSONPROFILE for this identifier.
            if (!IsPlausibleMatch(profile, publicIdentifierOrUrl))
            {
                _logger.LogWarning(
                    "HarvestApiProfileProvider.GetProfile rejected a mismatched/empty result for requested identifier '{Requested}' (returned PublicIdentifier '{Returned}', FirstName '{FirstName}', LastName '{LastName}').",
                    publicIdentifierOrUrl, profile.PublicIdentifier, profile.FirstName, profile.LastName);
                return null;
            }

            return MapToPersonProfile(profile);
        }

        // A valid hit needs (a) *some* core identity data at all - a not-found stub comes back with
        // FirstName/LastName/PublicIdentifier all empty - and (b), when the actor did echo a
        // PublicIdentifier, that it's actually related to what was requested. requestedIdentifierOrUrl may
        // be a bare identifier ("sherylsandberg") or a full profile URL
        // ("https://www.linkedin.com/in/sherylsandberg/"), so a case-insensitive substring check in either
        // direction covers both without needing to parse URLs.
        private static bool IsPlausibleMatch(HarvestApiProfileResult profile, string requestedIdentifierOrUrl)
        {
            var hasCoreIdentity = !string.IsNullOrWhiteSpace(profile.FirstName)
                || !string.IsNullOrWhiteSpace(profile.LastName)
                || !string.IsNullOrWhiteSpace(profile.PublicIdentifier);

            if (!hasCoreIdentity)
                return false;

            if (string.IsNullOrWhiteSpace(profile.PublicIdentifier) || string.IsNullOrWhiteSpace(requestedIdentifierOrUrl))
                return true;

            return requestedIdentifierOrUrl.Contains(profile.PublicIdentifier, StringComparison.OrdinalIgnoreCase)
                || profile.PublicIdentifier.Contains(requestedIdentifierOrUrl, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Public (not private) so it's independently verifiable against a captured real API response.
        /// </summary>
        public static PersonProfileResponse MapToPersonProfile(HarvestApiProfileResult r)
        {
            return new PersonProfileResponse
            {
                emailaddress = r.Emails?.FirstOrDefault(),
                public_identifier = r.PublicIdentifier,
                profile_pic_url = r.ProfilePicture?.Url,
                background_cover_image_url = r.CoverPicture?.Url,
                first_name = r.FirstName,
                last_name = r.LastName,
                full_name = $"{r.FirstName} {r.LastName}".Trim(),
                follower_count = r.FollowerCount,
                // no combined "occupation" field in this provider's payload - headline is the closest analog
                occupation = r.Headline,
                headline = r.Headline,
                summary = r.About,
                country = r.Location?.Parsed?.Country,
                country_full_name = r.Location?.Parsed?.CountryFull,
                city = r.Location?.Parsed?.City,
                state = r.Location?.Parsed?.State,
                experiences = r.Experience?.Select(ToExperience).ToList(),
                education = r.Education?.Select(ToEducation).ToList(),
                languages = r.Languages?.Select(l => l.Name).ToArray(),
                certifications = r.Certifications?.Select(ToCertification).ToList(),
                connections = r.ConnectionsCount,
                people_also_viewed = r.MoreProfiles?.Select(ToPeopleAlsoViewed).ToList(),
                skills = r.Skills?.Select(s => s.Name).ToArray(),
                // LinkedIn exposes neither of these - UserService.SignUp fills them in from the signup request
                gender = null,
                birth_date = null,
                // no industry signal anywhere in this provider's payload - industry-based match scoring
                // (MatchService.ScoreCandidate) is inert for profiles sourced this way until/unless a better
                // source is found. Deliberately left null rather than guessed at.
                industry = null,
                // topSkills is the closest analog to "shared interests" for a professional-matching signal -
                // harvestapi's own "interests" field means something different (companies/people/groups
                // followed on LinkedIn), not personal interest topics, so it isn't used for this.
                interests = r.TopSkills,
                personal_emails = r.Emails,
                personal_numbers = null
            };
        }

        private static experiences ToExperience(HarvestApiPosition p) => new experiences
        {
            starts_at = ToStartsAt(p.StartDate),
            ends_at = ToEndsAt(p.EndDate),
            company = p.CompanyName,
            company_linkedin_profile_url = p.CompanyLinkedinUrl,
            title = p.Position,
            description = p.Description,
            location = p.Location
        };

        private static education ToEducation(HarvestApiEducation e) => new education
        {
            starts_at = ToStartsAt(e.StartDate),
            ends_at = ToEndsAt(e.EndDate),
            field_of_study = e.FieldOfStudy,
            degree_name = e.Degree,
            school = e.SchoolName,
            school_linkedin_profile_url = e.SchoolLinkedinUrl
        };

        private static certifications ToCertification(HarvestApiCertification c) => new certifications
        {
            name = c.Title,
            authority = c.IssuedBy
        };

        private static people_also_viewed ToPeopleAlsoViewed(HarvestApiRelatedProfile p) => new people_also_viewed
        {
            link = p.LinkedinUrl,
            name = $"{p.FirstName} {p.LastName}".Trim(),
            summary = p.Position
        };

        private static starts_at ToStartsAt(HarvestApiDate d)
        {
            if (d?.Year is null)
                return null;

            return new starts_at { year = d.Year.Value, month = ParseMonth(d.Month), day = 1 };
        }

        private static ends_at ToEndsAt(HarvestApiDate d)
        {
            if (d?.Year is null)
                return null;

            return new ends_at { year = d.Year.Value, month = ParseMonth(d.Month), day = 1 };
        }

        private static int ParseMonth(string monthText)
        {
            if (!string.IsNullOrEmpty(monthText)
                && DateTime.TryParseExact(monthText, "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.Month;

            return 1;
        }
    }
}
