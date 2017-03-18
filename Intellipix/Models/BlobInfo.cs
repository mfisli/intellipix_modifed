using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Intellipix.Models
{
    public class BlobInfo
    {
        public string ImageUri { get; set; }
        public string ThumbnailUri { get; set; }
        public string Caption { get; set; }
        public string Tags { get; set; }
        public string Gender { get; set; }
        public string Age { get; set; }
    }
}