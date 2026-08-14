using System.Collections.Generic;

namespace ProConnect.Infrastructure.Provider
{
    /// <summary>
    /// Wire format for harvestapi/linkedin-profile-scraper on Apify (run-sync-get-dataset-items response).
    /// Deliberately mirrors the vendor's exact JSON shape/field names (verified against a real captured
    /// response) - this is provider-private and never leaves HarvestApiProfileProvider. The rest of the app
    /// only ever sees the stable ProConnect.Domain.Response.PersonProfileResponse via IProfileProvider.
    /// Deserialization relies on HttpClientUtilities' case-insensitive JSON options, so PascalCase properties
    /// here bind correctly against the vendor's camelCase JSON without needing [JsonPropertyName] attributes.
    /// Only fields ProConnect actually maps are modeled - the raw response has more (moreProfiles, causes,
    /// organizations, publications, etc.) that aren't used anywhere.
    /// </summary>
    public class HarvestApiProfileResult
    {
        public string Id { get; set; }
        public string PublicIdentifier { get; set; }
        public string LinkedinUrl { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string[] Emails { get; set; }
        public string Headline { get; set; }
        public string About { get; set; }
        public int ConnectionsCount { get; set; }
        public int FollowerCount { get; set; }
        public string[] TopSkills { get; set; }
        public HarvestApiLocation Location { get; set; }
        public HarvestApiImage ProfilePicture { get; set; }
        public HarvestApiImage CoverPicture { get; set; }
        public List<HarvestApiPosition> Experience { get; set; }
        public List<HarvestApiEducation> Education { get; set; }
        public List<HarvestApiCertification> Certifications { get; set; }
        public List<HarvestApiSkill> Skills { get; set; }
        public List<HarvestApiLanguage> Languages { get; set; }
        public List<HarvestApiRelatedProfile> MoreProfiles { get; set; }
    }

    public class HarvestApiLocation
    {
        public string LinkedinText { get; set; }
        public string CountryCode { get; set; }
        public HarvestApiParsedLocation Parsed { get; set; }
    }

    public class HarvestApiParsedLocation
    {
        public string Text { get; set; }
        public string CountryCode { get; set; }
        public string RegionCode { get; set; }
        public string Country { get; set; }
        public string CountryFull { get; set; }
        public string State { get; set; }
        public string City { get; set; }
    }

    public class HarvestApiImage
    {
        public string Url { get; set; }
    }

    public class HarvestApiPosition
    {
        public string Position { get; set; }
        public string Location { get; set; }
        public string EmploymentType { get; set; }
        public string WorkplaceType { get; set; }
        public string CompanyName { get; set; }
        public string CompanyLinkedinUrl { get; set; }
        public string Duration { get; set; }
        public string Description { get; set; }
        public string[] Skills { get; set; }
        public HarvestApiDate StartDate { get; set; }
        public HarvestApiDate EndDate { get; set; }
    }

    public class HarvestApiEducation
    {
        public string SchoolName { get; set; }
        public string SchoolLinkedinUrl { get; set; }
        public string Degree { get; set; }
        public string FieldOfStudy { get; set; }
        public string Period { get; set; }
        public HarvestApiDate StartDate { get; set; }
        public HarvestApiDate EndDate { get; set; }
    }

    public class HarvestApiCertification
    {
        public string Title { get; set; }
        public string IssuedBy { get; set; }
        public string IssuedAt { get; set; }
    }

    public class HarvestApiSkill
    {
        public string Name { get; set; }
        public string Endorsements { get; set; }
    }

    public class HarvestApiLanguage
    {
        public string Name { get; set; }
        public string Proficiency { get; set; }
    }

    public class HarvestApiRelatedProfile
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Position { get; set; }
        public string PublicIdentifier { get; set; }
        public string LinkedinUrl { get; set; }
    }

    /// <summary>
    /// harvestapi only gives month/year (month as a 3-letter name, e.g. "Jun", or "Present" text with no
    /// year for ongoing roles) - unlike ProxyCurl's old day/month/year ints. Mapped to ProConnect's existing
    /// date/starts_at/ends_at with day defaulted to 1, since this data is only ever display-only (never fed
    /// through SignUpValidator's date parsing, which only validates the top-level birth_date).
    /// </summary>
    public class HarvestApiDate
    {
        public string Month { get; set; }
        public int? Year { get; set; }
        public string Text { get; set; }
    }
}
