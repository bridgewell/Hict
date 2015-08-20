using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hict
{
    public class windowshostinfo : hostinfo
    {
        public static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public enum Win32_OperatingSystem
        {
            Caption,
            Description,
            Manufacturer,
            Name,
            LastBootUpTime,
            FreePhysicalMemory,
            TotalVisibleMemorySize,
        }

        public enum Win32_ComputerSystem
        {
            Manufacturer,
            TotalPhysicalMemory,
            Model,
        }

        public enum Win32_LogicalDisk
        {
            DeviceID,
            Size,
            FreeSpace,
            MediaType,
        }

        public enum Win32_NetworkAdapterConfiguration
        {
            Caption,
            IPAddress,
            MACAddress,
            MTU,
        }

        public enum Win32_NetworkAdapter
        {
            Caption,
            MACAddress,
            Speed,
            NetworkAddresses,
        }

        public enum Win32_PerfFormattedData_Tcpip_NetworkInterface 
        {
            Name,
            BytesReceivedPerSec,
            BytesSentPerSec,
        }

        /// <summary>
        /// some windows 2003 machine doest not contains this, could use Win32_PerfRawData_PerfOS_Memory to replace it.
        /// </summary>
        public enum Win32_PerfFormattedData_PerfOS_Memory
        {
            AvailableBytes,
            CommittedBytes,
        }

        public enum Win32_PerfRawData_PerfOS_Memory
        {
            AvailableBytes,
            CommittedBytes,
        }

        public enum Win32_Processor
        {
            DeviceID,
            LoadPercentage,
        }

        public static Type[] WMI_SingleInstances = new Type[] { 
            typeof(Win32_OperatingSystem), 
            typeof(Win32_ComputerSystem), 
            typeof(Win32_PerfFormattedData_PerfOS_Memory),
            typeof(Win32_PerfRawData_PerfOS_Memory)
        };

        public static Tuple<Type, Enum>[] WMI_MultipleInstances = new Tuple<Type, Enum>[] { 
            new Tuple<Type, Enum>(typeof(Win32_LogicalDisk), Win32_LogicalDisk.DeviceID), 
            new Tuple<Type, Enum>(typeof(Win32_PerfFormattedData_Tcpip_NetworkInterface), Win32_PerfFormattedData_Tcpip_NetworkInterface.Name),
            new Tuple<Type, Enum>(typeof(Win32_Processor), Win32_Processor.DeviceID),
            new Tuple<Type, Enum>(typeof(Win32_NetworkAdapterConfiguration), Win32_NetworkAdapterConfiguration.Caption),
            new Tuple<Type, Enum>(typeof(Win32_NetworkAdapter), Win32_NetworkAdapter.Caption),
        };


        private static HashSet<Type> IgnoreableWMITypes = new HashSet<Type>(new Type[] { typeof(Win32_PerfFormattedData_PerfOS_Memory) });

        private IEnumerable<Tuple<T, ManagementBaseObject>> GetManagementBaseObjects<T>(Type type, T obj)
        {
            var scope = new ManagementScope(@"\\" + host + @"\root\cimv2", new ConnectionOptions() { Username = host + @"\" + userid, Password = pass });
            try
            {
                scope.Connect();
            }
            catch (Exception ex)
            {
                scope = new ManagementScope(@"\\" + host + @"\root\cimv2");
                try
                {
                    // re connect for current user credentials (bcz windows only support credential at network ><)
                    scope.Connect();
                }
                catch (Exception ex2)
                {
                    logger.Fatal(@"connect to \\" + host + @"\root\cimv2 failed by " + ex2.ToString());
                    throw ex;
                }
            }

            var r = new List<Tuple<T, ManagementBaseObject>>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM " + type.Name))
                {
                    searcher.Scope = scope;
                    foreach (var mo in searcher.Get())
                        r.Add(Tuple.Create(obj, mo));
                }
            }
            catch (Exception ex)
            {
                if (IgnoreableWMITypes.Contains(type))
                    return new Tuple<T, ManagementBaseObject>[] { };

                logger.Fatal("query " + type.Name + " at host " + host + " failed by " + ex.ToString());
                throw;
            }
            return r;
        }

        protected override IEnumerable<statistics> DoSystemUniquePool()
        {
            foreach (var mo in WMI_SingleInstances.Select(w => GetManagementBaseObjects(w, w)).SelectMany(x => x))
            {
                var properties = mo.Item2.Properties.Cast<PropertyData>();
                var pdict = new Dictionary<string, PropertyData>();
                foreach (var p in properties)
                    pdict[p.Name] = p;

                foreach (var v in Enum.GetValues(mo.Item1))
                {
                    var vname = v.ToString();
                    if (pdict.ContainsKey(vname))
                    {
                        yield return new statistics()
                        {
                            category = mo.Item1.Name,
                            type = v.ToString(),
                            val = pdict[vname].Value.ToString(),
                            begintime = DateTime.Now,
                            endtime = DateTime.Now,
                        };
                    }
                }
            }

            foreach (var mo in WMI_MultipleInstances.Select(w => GetManagementBaseObjects(w.Item1, w)).SelectMany(x => x))
            {
                var id = mo.Item2[mo.Item1.Item2.ToString()];
                foreach (var v in Enum.GetValues(mo.Item1.Item1))
                {
                    if (v.Equals(mo.Item1.Item2))
                        continue;
                    var obj = mo.Item2[v.ToString()];
                    if (obj == null)
                        continue;

                    if (obj is Array)
                    {
                        yield return new statistics()
                        {
                            category = mo.Item1.Item1.Name + "::" + id.ToString(),
                            type = v.ToString(),
                            val = String.Join(",", ((Array)obj).Cast<string>()),
                            begintime = DateTime.Now,
                            endtime = DateTime.Now,
                        };
                    }
                    else
                    {
                        yield return new statistics()
                        {
                            category = mo.Item1.Item1.Name + "::" + id.ToString(),
                            type = v.ToString(),
                            val = obj.ToString(),
                            begintime = DateTime.Now,
                            endtime = DateTime.Now,
                        };
                    }
                }
            }
        }

        protected HashSet<string> NeedMonitorNetWorkAdapter = new HashSet<string>();

        protected override IEnumerable<statistics> TranslateToStats(IEnumerable<statistics> systemstats, node n, List<networkinterface> nics, List<volume> volumes)
        {
            nics.RemoveAll(ni => NeedMonitorNetWorkAdapter.Contains(ni.caption) == false);

            var r = new Dictionary<string, statistics>();
            foreach (var s in systemstats)
            {
                var cats = s.category.Split(new string[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                switch(cats[0])
                {
                    case "Win32_LogicalDisk":
                        if (cats.Length != 2)
                            continue;
                        var diskid = cats[1];
                        if (diskid.EndsWith(":"))
                            diskid = diskid.TrimEnd(':');
                        var statid = "DISK:" + diskid;
                        statistics diskstat;
                        if (r.TryGetValue(statid, out diskstat) == false)
                        {
                            diskstat = new statistics() { nodeid = n.id, begintime = s.begintime, endtime = s.endtime, category = "DISK", type = diskid };
                            r[statid] = diskstat;
                        }
                        switch (s.type)
                        {
                            case "Size":
                                diskstat.cap = Convert.ToDouble(s.val);
                                break;
                            case "FreeSpace":
                                diskstat.avg = Convert.ToDouble(s.val);
                                break;
                        }
                        break;
                    case "Win32_PerfFormattedData_Tcpip_NetworkInterface":
                        if (cats.Length != 2)
                            continue;

                        var adaptername = cats[1];
                        if (NeedMonitorNetWorkAdapter.Contains(adaptername))
                        {
                            var foundnic = nics.FirstOrDefault(ni => ni.caption == adaptername);
                            string networktype = string.Empty;
                            switch (s.type)
                            {
                                case "BytesReceivedPerSec":
                                    networktype = "INBPS";
                                    if (foundnic != null)
                                        foundnic.InBps = Convert.ToSingle(s.val);
                                    break;
                                case "BytesSentPerSec":
                                    networktype = "OUTBPS";
                                    if (foundnic != null)
                                        foundnic.OutBps = Convert.ToSingle(s.val);
                                    break;
                                default:
                                    continue;
                            }
                            var netstatid = "NETWORK:" + adaptername + ":" + networktype;
                            yield return new statistics() { nodeid = n.id, begintime = s.begintime, endtime = s.endtime, category = "NETWORK", type = networktype, avg = Convert.ToDouble(s.val), val = adaptername };
                        }
                        break;
                    case "Win32_PerfFormattedData_PerfOS_Memory":
                        switch (s.type)
                        {
                            case "AvailableBytes":
                                yield return new statistics() { nodeid = n.id, begintime = s.begintime, endtime = s.endtime, category = "MEM", type = "FREE", cap = n.TotalMemory , avg = Convert.ToDouble(s.val)};
                                break;
                            case "CommittedBytes":
                                yield return new statistics() { nodeid = n.id, begintime = s.begintime, endtime = s.endtime, category = "MEM", type = "USE", cap = n.TotalMemory, avg = Convert.ToDouble(s.val)};
                                break;
                        }
                        break;
                    case "Win32_Processor":
                        if (cats.Length != 2)
                            continue;

                        var cpudevice = cats[1];

                        switch (s.type)
                        {
                            case "LoadPercentage":
                                yield return new statistics() { nodeid = n.id, begintime = s.begintime, endtime = s.endtime, category = "CPU", type = cpudevice, avg = Convert.ToDouble(s.val) };
                                break;
                        }
                        break;
                }
            }

            //var time = DateTime.Now;

            //yield return new statistics() { nodeid = n.id, begintime = time, endtime = time, category = "MEM", type = "FREE", cap = n.TotalMemory, avg = n.TotalMemory - n.MemoryUsed };
            //yield return new statistics() { nodeid = n.id, begintime = time, endtime = time, category = "MEM", type = "USE", cap = n.TotalMemory, avg = n.MemoryUsed };


            foreach (var v in r.Values)
            {
                switch(v.category)
                {
                    case "DISK":
                        v.avg = v.cap - v.avg;
                        yield return v;
                        break;
                    default:
                        yield return v;
                        break;
                }
            }
        }

        protected Regex NetworkAdapterRegex = new Regex(@"\s*\[\d*\]\s*(?<name>.*)", RegexOptions.Compiled);

        protected string FixNetworkAdapterCaption(string caption)
        {
            var m = NetworkAdapterRegex.Match(caption);
            if (m.Success)
                return m.Groups["name"].Value;
            else
                return caption;
        }

        protected override IEnumerable<statistics> FitToNodeInfo(IEnumerable<statistics> systemstats, node n, List<networkinterface> nics, List<volume> volumes)
        {
            float freememory = 0;
            var cpuloads = new List<int>();
            var volumesneedtofix = new List<volume>();
            var shoudMonitorDisk = new HashSet<string>();
            foreach (var s in systemstats)
            {
                var catinfo = s.category.Split(new string[] {"::"}, StringSplitOptions.RemoveEmptyEntries);
                switch (catinfo[0])
                {
                    case "Win32_OperatingSystem":
                        windowshostinfo.Win32_OperatingSystem osi;
                        if (Enum.TryParse<windowshostinfo.Win32_OperatingSystem>(s.type, out osi))
                        {
                            switch (osi)
                            {
                                case Win32_OperatingSystem.LastBootUpTime:
                                    n.LastBoot = ManagementDateTimeConverter.ToDateTime(s.val);
                                    break;
                                case Win32_OperatingSystem.Caption:
                                    n.MachineType = s.val;
                                    break;
                                case Win32_OperatingSystem.FreePhysicalMemory:
                                    freememory = Convert.ToSingle(s.val);
                                    break;
                            }
                        }
                        break;
                    case "Win32_ComputerSystem":
                        windowshostinfo.Win32_ComputerSystem cs;
                        if (Enum.TryParse<windowshostinfo.Win32_ComputerSystem>(s.type, out cs))
                        {
                            switch (cs)
                            {
                                case Win32_ComputerSystem.Manufacturer:
                                    n.Manufacturer = s.val;
                                    break;
                                case Win32_ComputerSystem.Model:
                                    n.Model = s.val;
                                    break;
                                case Win32_ComputerSystem.TotalPhysicalMemory:
                                    n.TotalMemory = Convert.ToSingle(s.val);
                                    break;
                            }
                        }
                        break;
                    case "Win32_NetworkAdapterConfiguration":
                        if (catinfo.Length != 2)
                            continue;
                                                    var cap = FixNetworkAdapterCaption(catinfo[1]);

                            var fnic = nics.Where(ni => ni.caption == cap).FirstOrDefault();
                            if (fnic == null)
                            {
                                fnic = new networkinterface() { caption = cap };
                                nics.Add(fnic);
                            }

                        windowshostinfo.Win32_NetworkAdapterConfiguration nac;
                        if (Enum.TryParse<windowshostinfo.Win32_NetworkAdapterConfiguration>(s.type, out nac))
                        {
                            switch(nac)
                            {
                                case Win32_NetworkAdapterConfiguration.IPAddress:
                                    var ips = s.val.Split(',');
                                    var ipv4s = ips.Where(i =>
                                    {
                                        IPAddress ip;
                                        if (IPAddress.TryParse(i, out ip))
                                        {
                                            // if this is ipv4
                                            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                            {
                                                if (i == "0.0.0.0")
                                                    return false;
                                                NeedMonitorNetWorkAdapter.Add(cap);
                                                return true;
                                            }
                                        }
                                        return false;
                                    }).ToArray();
                                    var ipset = new HashSet<string>();
                                    if (string.IsNullOrWhiteSpace(n.Ip) == false)
                                    {
                                        foreach (var oldip in n.Ip.Split(','))
                                            ipset.Add(oldip);
                                    }
                                    foreach (var ip in ipv4s)
                                        ipset.Add(ip);
                                    n.Ip = string.Join(",", ipset.ToArray());
                                    fnic.NetworkAddress = string.Join(",", ipv4s);
                                    break;
                                case Win32_NetworkAdapterConfiguration.MTU:
                                    fnic.MTU = Convert.ToInt32(s.val);
                                    break;
                            }
                        }
                        break;
                    case "Win32_PerfFormattedData_PerfOS_Memory":
                        windowshostinfo.Win32_PerfFormattedData_PerfOS_Memory ppm;
                        if (Enum.TryParse<windowshostinfo.Win32_PerfFormattedData_PerfOS_Memory>(s.type, out ppm))
                        {
                            switch (ppm)
                            {
                                case Win32_PerfFormattedData_PerfOS_Memory.AvailableBytes:
                                    n.MemoryUsed = n.TotalMemory - Convert.ToSingle(s.val);
                                    break;
                            }
                        }
                        yield return s;
                        break;
                    case "Win32_PerfRawData_PerfOS_Memory":
                        windowshostinfo.Win32_PerfRawData_PerfOS_Memory rppm;
                        if (Enum.TryParse<windowshostinfo.Win32_PerfRawData_PerfOS_Memory>(s.type, out rppm))
                        {
                            switch (rppm)
                            {
                                case Win32_PerfRawData_PerfOS_Memory.AvailableBytes:
                                    n.MemoryUsed = n.TotalMemory - Convert.ToSingle(s.val);
                                    break;
                            }
                        }
                        yield return s;
                        break;
                    case "Win32_Processor":
                        windowshostinfo.Win32_Processor wp;
                        if (Enum.TryParse<windowshostinfo.Win32_Processor>(s.type, out wp))
                        {
                            switch (wp)
                            {
                                case Win32_Processor.LoadPercentage:
                                    cpuloads.Add(Convert.ToInt32(s.val));
                                    break;
                            }
                        }
                        yield return s;
                        break;
                    case "Win32_NetworkAdapter":
                        if (catinfo.Length != 2)
                            break;

                        windowshostinfo.Win32_NetworkAdapter na;
                        if (Enum.TryParse<windowshostinfo.Win32_NetworkAdapter>(s.type, out na))
                        {
                            var nacap = FixNetworkAdapterCaption(catinfo[1]);

                            var nafic = nics.Where(ni => ni.caption == nacap).FirstOrDefault();
                            if (nafic == null)
                            {
                                nafic = new networkinterface() { caption = nacap };
                                nics.Add(nafic);
                            }
                            switch(na)
                            {
                                case Win32_NetworkAdapter.MACAddress:
                                    nafic.MacAddress = s.val;
                                    break;
                                case Win32_NetworkAdapter.Speed:
                                    nafic.Speed = Convert.ToDouble(s.val);
                                    break;
                            }
                        }
                        break;

                    case "Win32_LogicalDisk":
                        yield return s;

                        if (catinfo.Length != 2)
                            continue;
                        var diskid = catinfo[1];
                        if (diskid.EndsWith(":"))
                            diskid = diskid.TrimEnd(':');

                        var foundvolume = volumes.FirstOrDefault(v => v.name == diskid);
                        if (foundvolume == null)
                        {
                            foundvolume = new volume() { name = diskid };
                            volumes.Add(foundvolume);
                        }
                        foundvolume.LastSync = DateTime.UtcNow;
                        switch (s.type)
                        {
                            case "Size":
                                foundvolume.capacity = Convert.ToSingle(s.val);
                                break;
                            case "FreeSpace":
                                foundvolume.usage = Convert.ToSingle(s.val);
                                volumesneedtofix.Add(foundvolume);
                                break;
                            case "MediaType":
                                // only monitor MediaType = 12 (fixed disk)
                                if (s.val == "12")
                                {
                                    shoudMonitorDisk.Add(diskid);
                                }
                                break;
                        }
                        break;
                    default:
                        yield return s;
                        break;
                }
            }

            if (cpuloads.Count > 0)
                n.CPULoad = (int)cpuloads.Average();

            foreach (var v in volumesneedtofix)
                v.usage = v.capacity - v.usage;

            n.status = (int)Hict.node.NodeStatus.Active;
            //n.MemoryUsed = n.TotalMemory - freememory;

            // remove volumns which is not fixed disk.
            volumes.RemoveAll(v => !shoudMonitorDisk.Contains(v.name));
        }
    }

}
