using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Request
{
    public class UpdateProfileRequest
    {
        public UpdateProfileRequest()
        {

        }
        public string first_name { get; set; }
        public string last_name { get; set; }
        public string headline { get; set; }
        public string summary { get; set; }
        public string occupation { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public string profile_pic_url { get; set; }
        public string background_cover_image_url { get; set; }
    }
}
