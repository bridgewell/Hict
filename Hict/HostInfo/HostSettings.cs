using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hict
{
    public class HostSettings
    {
        public string TSDBHost { get; set; }
        public int TSDBPort { get; set; }
        public string MetricHeader { get; set; }
        /// <summary>
        /// write to multiple tsdbs.
        /// </summary>
        public string MultipleTSDB { get; set; }
        public hostinfo[] Windows { get; set; }
        public hostinfo[] Linux { get; set; }
    }
}
