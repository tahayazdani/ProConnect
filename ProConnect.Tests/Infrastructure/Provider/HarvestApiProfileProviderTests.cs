using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProConnect.Domain.Config;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Provider;
using System;
using System.Collections.Generic;

namespace ProConnect.Tests.Infrastructure.Provider
{
    /// <summary>
    /// HarvestApiProfileProvider.GetProfile has no Cache dependency (unlike NominatimGeocodingProvider) -
    /// just IHttpClientUtilities.PostAsync. These tests cover: the blank-identifier guard, the BUG-09
    /// plausibility check (rejecting empty/mismatched results), and the full mapping pipeline via the
    /// public static MapToPersonProfile (exercised indirectly through GetProfile here, since that's the
    /// only way to also cover the plausibility gate in front of it).
    /// </summary>
    public class HarvestApiProfileProviderTests
    {
        private readonly Mock<IHttpClientUtilities> _httpClientUtilities = new();
        private readonly HarvestApiConfig _config = new HarvestApiConfig
        {
            apitoken = "token-123",
            baseurl = "https://harvestapi.example.com",
            actorid = "actor-1",
            profilescrapermode = "full",
            timeoutinseconds = 30
        };

        private HarvestApiProfileProvider CreateProvider()
        {
            var proConnectConfig = Options.Create(new ProConnectConfig { harvestApiConfig = _config });
            return new HarvestApiProfileProvider(
                _httpClientUtilities.Object,
                proConnectConfig,
                new Mock<ILogger<HarvestApiProfileProvider>>().Object);
        }

        private void SetupHttpResult(List<HarvestApiProfileResult> results)
        {
            _httpClientUtilities.Setup(h => h.PostAsync<List<HarvestApiProfileResult>>(
                    _config.baseurl, It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), _config.apitoken, null))
                .ReturnsAsync(results);
        }

        [Fact]
        public async Task GetProfile_WithBlankIdentifier_ReturnsNullWithoutCallingHttpClient()
        {
            var provider = CreateProvider();

            var result = await provider.GetProfile("");

            Assert.Null(result);
            _httpClientUtilities.Verify(h => h.PostAsync<List<HarvestApiProfileResult>>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()),
                Times.Never);
        }

        [Fact]
        public async Task GetProfile_WhenNoResultsReturned_ReturnsNull()
        {
            SetupHttpResult(new List<HarvestApiProfileResult>());

            var provider = CreateProvider();

            var result = await provider.GetProfile("sherylsandberg");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetProfile_WhenResultIsEmptyStub_ReturnsNull()
        {
            SetupHttpResult(new List<HarvestApiProfileResult>
            {
                new HarvestApiProfileResult { FirstName = "", LastName = "", PublicIdentifier = "" }
            });

            var provider = CreateProvider();

            var result = await provider.GetProfile("sherylsandberg");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetProfile_WhenPublicIdentifierMismatchesRequest_ReturnsNull()
        {
            SetupHttpResult(new List<HarvestApiProfileResult>
            {
                new HarvestApiProfileResult { FirstName = "Jane", LastName = "Doe", PublicIdentifier = "completely-unrelated-person" }
            });

            var provider = CreateProvider();

            var result = await provider.GetProfile("sherylsandberg");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetProfile_WithPlausibleMatch_MapsFullProfileIncludingNestedCollections()
        {
            var result = new HarvestApiProfileResult
            {
                PublicIdentifier = "sherylsandberg",
                FirstName = "Sheryl",
                LastName = "Sandberg",
                Emails = new[] { "sheryl@example.com" },
                Headline = "Executive",
                About = "Bio text",
                ConnectionsCount = 500,
                FollowerCount = 10000,
                TopSkills = new[] { "Leadership" },
                Location = new HarvestApiLocation
                {
                    Parsed = new HarvestApiParsedLocation { Country = "US", CountryFull = "United States", City = "Menlo Park", State = "California" }
                },
                ProfilePicture = new HarvestApiImage { Url = "https://img/pic.jpg" },
                CoverPicture = new HarvestApiImage { Url = "https://img/cover.jpg" },
                Experience = new List<HarvestApiPosition>
                {
                    new HarvestApiPosition
                    {
                        Position = "COO",
                        CompanyName = "Meta",
                        CompanyLinkedinUrl = "https://linkedin.com/company/meta",
                        Description = "Ran operations",
                        Location = "Menlo Park",
                        StartDate = new HarvestApiDate { Month = "Mar", Year = 2008 },
                        EndDate = new HarvestApiDate { Month = "Aug", Year = 2022 }
                    }
                },
                Education = new List<HarvestApiEducation>
                {
                    new HarvestApiEducation
                    {
                        SchoolName = "Harvard",
                        SchoolLinkedinUrl = "https://linkedin.com/school/harvard",
                        Degree = "MBA",
                        FieldOfStudy = "Business",
                        StartDate = new HarvestApiDate { Year = 1993 },
                        EndDate = new HarvestApiDate { Year = 1995 }
                    }
                },
                Certifications = new List<HarvestApiCertification>
                {
                    new HarvestApiCertification { Title = "Six Sigma", IssuedBy = "ASQ" }
                },
                Skills = new List<HarvestApiSkill> { new HarvestApiSkill { Name = "Management" } },
                Languages = new List<HarvestApiLanguage> { new HarvestApiLanguage { Name = "English" } },
                MoreProfiles = new List<HarvestApiRelatedProfile>
                {
                    new HarvestApiRelatedProfile { FirstName = "John", LastName = "Smith", Position = "CEO", LinkedinUrl = "https://linkedin.com/in/johnsmith" }
                }
            };
            SetupHttpResult(new List<HarvestApiProfileResult> { result });

            var provider = CreateProvider();

            var profile = await provider.GetProfile("https://www.linkedin.com/in/sherylsandberg/");

            Assert.NotNull(profile);
            Assert.Equal("sherylsandberg", profile.public_identifier);
            Assert.Equal("sheryl@example.com", profile.emailaddress);
            Assert.Equal("Sheryl Sandberg", profile.full_name);
            Assert.Equal("Executive", profile.headline);
            Assert.Equal("Menlo Park", profile.city);
            Assert.Equal("California", profile.state);
            Assert.Equal("US", profile.country);

            Assert.NotNull(profile.experiences);
            Assert.Single(profile.experiences);
            Assert.Equal("Meta", profile.experiences[0].company);
            Assert.Equal("COO", profile.experiences[0].title);
            Assert.Equal(2008, profile.experiences[0].starts_at.year);
            Assert.Equal(3, profile.experiences[0].starts_at.month);
            Assert.Equal(2022, profile.experiences[0].ends_at.year);
            Assert.Equal(8, profile.experiences[0].ends_at.month);

            Assert.NotNull(profile.education);
            Assert.Single(profile.education);
            Assert.Equal("Harvard", profile.education[0].school);
            Assert.Equal(1993, profile.education[0].starts_at.year);
            // StartDate.Month is null/blank for this entry - ParseMonth should default to 1
            Assert.Equal(1, profile.education[0].starts_at.month);

            Assert.NotNull(profile.certifications);
            Assert.Equal("Six Sigma", profile.certifications[0].name);
            Assert.Equal("ASQ", profile.certifications[0].authority);

            Assert.NotNull(profile.people_also_viewed);
            Assert.Equal("John Smith", profile.people_also_viewed[0].name);

            Assert.Equal(new[] { "Management" }, profile.skills);
            Assert.Equal(new[] { "English" }, profile.languages);
            Assert.Equal(new[] { "Leadership" }, profile.interests);
            Assert.Null(profile.gender);
            Assert.Null(profile.birth_date);
            Assert.Null(profile.industry);
        }
    }
}
