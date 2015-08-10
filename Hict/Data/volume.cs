using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hict
{
    public class volume
    {
        public int id { get; set; }
        public int nodeid { get; set; }
        public string name { get; set; }
        public Single capacity { get; set; }
        public Single usage { get; set; }
        public DateTime LastSync { get; set; }
    }
}
