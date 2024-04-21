using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Response
{
    public class PersonProfileResponse
    {
        public PersonProfileResponse()
        {
            
        }
        public string emailaddress { get; set; }
        public string public_identifier { get; set; } // linkedin username
        public string profile_pic_url { get; set; }
        public string background_cover_image_url { get; set; }
        public string first_name { get; set; }
        public string last_name { get; set; }
        public string full_name { get; set; }
        public int follower_count { get; set; }
        public string occupation { get; set; }
        public string headline { get; set; }
        public string summary { get; set; }
        public string country { get; set; }
        public string country_full_name { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public List<experiences> experiences { get; set; }
        public List<education> education { get; set; }
        public string[] languages { get; set; }
        //public string[] accomplishment_organisations { get; set; }
        //public string[] accomplishment_publications { get; set; }
        //public string[] accomplishment_honors_awards { get; set; }
        //public string[] accomplishment_patents { get; set; }
        //public string[] accomplishment_courses { get; set; }
        //public string[] accomplishment_projects { get; set; }
        //public string[] accomplishment_test_scores { get; set; }
        //public string[] volunteer_work { get; set; }
        public List<certifications> certifications { get; set; }
        public int connections { get; set; }
        public List<people_also_viewed> people_also_viewed { get; set; }
        //public string[] recommendations { get; set; }
        //public string[] activities { get; set; }
        //public string[] similarly_named_profiles { get; set; }
        //public string[] articles { get; set; }
        //public string[] groups { get; set; }
        public string[] skills { get; set; }
        //public int inferred_salary { get; set; }
        public string gender { get; set; }
        public date birth_date { get; set; }
        public string industry { get; set; }
        //public string[] extra { get; set; }
        public string[] interests { get; set; }
        public string[] personal_emails { get; set; }
        public string[] personal_numbers { get; set; }
    }

    public class experiences
    {
        public experiences()
        {
        }
        public starts_at starts_at { get; set; }
        public ends_at ends_at { get; set; }
        public string company { get; set; }
        public string company_linkedin_profile_url { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string location { get; set; }
        public string logo_url { get; set; }

    }

    public class education
    {
        public education()
        {

        }
        public starts_at starts_at { get; set; }
        public ends_at ends_at { get; set; }
        public string field_of_study { get; set; }
        public string degree_name { get; set; }
        public string school { get; set; }
        public string school_linkedin_profile_url { get; set; }
        public string description { get; set; }
        public string logo_url { get; set; }
        public string grade { get; set; }
        public string activities_and_societies { get; set; }

    }

    public class certifications
    {
        public certifications()
        {
            
        }
        public starts_at starts_at { get; set; }
        public ends_at ends_at { get; set; }
        public string name { get; set; }
        public string license_number { get; set; }
        public string display_source { get; set; }
        public string authority { get; set; }
        public string url { get; set; }


    }

    public class people_also_viewed
    {
        public people_also_viewed()
        {
            
        }
        public string link { get; set; }
        public string name { get; set; }
        public string summary { get; set; }
        public string location { get; set; }

    }

    public class date
    {
        public int day { get; set; }
        public int month { get; set; }
        public int year { get; set; }
    }
    public class starts_at : date { }
    public class ends_at : date { }
}
