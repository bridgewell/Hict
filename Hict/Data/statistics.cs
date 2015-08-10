using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hict
{
    public class statistics
    {
        public int nodeid { get; set; }
        public string type { get; set; }
        public string category { get; set; }
        public string val { get; set; }
        public DateTime begintime { get; set; }
        public DateTime endtime { get; set; }
        public double avg { get; set; }
        public double max { get; set; }
        public double min { get; set; }
        public double cap { get; set; }
    }
}
