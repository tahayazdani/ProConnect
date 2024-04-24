using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProConnect.Domain.Config
{
    public struct CacheItem<T>
    {
        public string Key { get; set; }

        public string Region { get; set; }

        public T Value { get; set; }

        public ExpirationMode ExpirationMode { get; set; }

        public double Ttl { get; set; }

        public DateTime ExpiresOn { get; set; }

        public bool Exists { get; set; }
    }
}
