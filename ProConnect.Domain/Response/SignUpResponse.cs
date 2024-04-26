using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Response
{
    public class SignUpResponse
    {
        public SignUpResponse()
        {

        }
        public Guid userid { get; set; }
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
        public string gender { get; set; }
        public date birth_date { get; set; }
    }
}
