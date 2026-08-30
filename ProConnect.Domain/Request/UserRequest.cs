using ProConnect.Domain.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Request
{
    public class UserRequest
    {
        public UserRequest()
        {
            
        }
        public Guid userid { get; set; }
        public string sub { get; set; }
        public string emailaddress { get; set; }
        public string public_identifier { get; set; }
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
        // Geocoded from city/state/country via IGeocodingProvider at signup - LinkedIn/harvestapi provides
        // no lat/long directly. Null when the location text couldn't be resolved to a coordinate.
        public double? latitude { get; set; }
        public double? longitude { get; set; }
        public string gender { get; set; }
        public date birth_date { get; set; }
        public string industry { get; set; }
        public string company { get; set; }
        public string[] interests { get; set; }
        // Soft-delete/deactivation flag. New signups always start active (set explicitly in
        // UserRepository regardless of whatever a caller passes in). Everywhere a user can be looked
        // up or listed (GetById, GetBySub, match candidates, search) filters this to true - an
        // inactive user should behave as if they don't exist to everyone, including themselves.
        public bool is_active { get; set; } = true;
    }
}
