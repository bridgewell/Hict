using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hict
{
    public class node
    {
        public int id { get; set; }
        public string name { get; set; }
        public string MachineType { get; set; }
        public DateTime LastSync { get; set; }
        public int status { get; set; }
        public DateTime LastBoot { get; set; }
        public int CPULoad { get; set; }
        public Single TotalMemory { get; set; }
        public Single MemoryUsed { get; set; }
        public string Ip { get; set; }
        public int PoolIntervalSeconds { get; set; }
        public int VMHostID { get; set; }
        public int IsVMHost { get; set; }
        public int IsUnwatched { get; set; }
        public DateTime UnwatchedFrom { get; set; }
        public DateTime UnwatchedUntil { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string ServiceTag { get; set; }

        public enum NodeStatus
        {
            None,
            Active,
            Unreachable,
        }
    }
}
