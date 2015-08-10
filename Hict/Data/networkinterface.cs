using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hict
{
    public class networkinterface
    {
        public int id { get; set; }
        public int nodeid { get; set; }
        public string caption { get; set; }
        public DateTime LastSync { get; set; }
        public float InBps { get; set; }
        public float OutBps { get; set; }
        public double Speed { get; set; }
        public int MTU { get; set; }
        public string MacAddress { get; set; }
        public string NetworkAddress { get; set; }
    }
}
