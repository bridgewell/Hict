﻿using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hict
{
    public class linuxhostinfo : hostinfo
    {
        protected enum StatisticsType
        {
            None,

            CPUUsage,

            MEMUsage,

            CPUInfo,

            MemInfoTotal,

            MemInfoFree,

            VolumeUsage,

            VolumeTotal,

            MachineType,

            UpSeconds,

            DMI,

            InterfaceIp,

            CPUCores,
        }


        protected override IEnumerable<statistics> DoSystemUniquePool()
        {
            using (var client = new SshClient(host, userid, pass))
            {
                client.HostKeyReceived += (sender, e) => {
                    e.CanTrust = true;
                };

                client.Connect();

                foreach (var stats in new IEnumerable<statistics>[] { 
                    GetMemInfo(client),
                    GetCpuMemUsage(client),
                    GetVolInfo(client),
                    GetMachineType(client),
                    GetUpSeconds(client),
                    GetInterfaceIP(client),
                    GetDMI(client),
                    GetCPUCores(client),
                })
                {
                    foreach (var s in stats)
                    {
                        s.begintime = s.endtime = DateTime.Now;
                        yield return s;
                    }
                }

                client.Disconnect();
            }
            yield break;
        }

        private IEnumerable<statistics> GetCPUCores(SshClient client)
        {
            var cmdresult = client.CreateCommand("cat /proc/cpuinfo | grep processor").Execute();
            var lines = cmdresult.Split(new char[] { '\r', '\n' }).Where(l => l.Trim().Length > 0);
            yield return new statistics()
            {
                type = StatisticsType.CPUCores.ToString(),
                val = lines.Count().ToString(),
            };
        }



        private static Regex ipregex = new Regex(@"inet addr:(?<ip>\d+\.\d+\.\d+\.\d+)", RegexOptions.Compiled);

        private IEnumerable<statistics> GetInterfaceIP(SshClient client)
        {
            var cmdresult = client.CreateCommand("ifconfig").Execute();
            var lines = cmdresult.Split(new char[] { '\r', '\n' });

            string currenteth = string.Empty;
            foreach (var l in lines)
            {
                if (l.Contains("Link encap:"))
                {
                    currenteth = l.Split(' ')[0];
                    continue;
                }

                var m = ipregex.Match(l);
                if (m.Success)
                {
                    if (currenteth.Length == 0)
                        continue;
                    var ip = m.Groups["ip"].Value;
                    if (ip == "127.0.0.1")
                        continue;

                    yield return new statistics()
                    {
                        type = StatisticsType.InterfaceIp.ToString(),
                        val = ip,
                        category = currenteth,
                    };
                }
            }
        }

        private IEnumerable<statistics> GetMachineType(SshClient client)
        {
            var cmdresult = client.CreateCommand("uname -sr").Execute();

            yield return new statistics() { 
                type = StatisticsType.MachineType.ToString(),
                val = cmdresult,            
            };
        }


        private IEnumerable<statistics> GetUpSeconds(SshClient client)
        {
            var cmdresult = client.CreateCommand("cat /proc/uptime").Execute().Split(new char[] {' '});
            double sec;
            if (double.TryParse(cmdresult[0], out sec))
            {
                yield return new statistics()
                {
                    type = StatisticsType.UpSeconds.ToString(),
                    val = sec.ToString(),
                };
            }
        }

        private IEnumerable<statistics> GetDMI(SshClient client)
        {
            var cmdresult = client.CreateCommand("dmesg |grep DMI:").Execute().Split(new char[] { ':' });
            if (cmdresult.Length == 2)
            {
                yield return new statistics()
                {
                    type = StatisticsType.DMI.ToString(),
                    val = cmdresult[1],
                };
            }
        }

        private IEnumerable<statistics> GetVolInfo(SshClient client)
        {
            var cmdresult = client.CreateCommand("df -P").Execute();
            var lines = cmdresult.Split(new char[] { '\r', '\n' });
            var infos = lines
                .Where(l => l.Trim().Length > 0)
                .Select(l => l.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Skip(1);

            foreach (var l in infos)
            {
                if (l[0].StartsWith("/dev") == false)
                    continue;

                yield return new statistics()
                {
                    type = StatisticsType.VolumeTotal.ToString(),
                    category = l[0],
                    val = l[1],
                };

                yield return new statistics()
                {
                    type = StatisticsType.VolumeUsage.ToString(),
                    category = l[0],
                    val = l[2],
                };
            }
        }

        private IEnumerable<statistics> GetMemInfo(SshClient client)
        {
            var cmdresult = client.CreateCommand("cat /proc/meminfo").Execute();
            var lines = cmdresult.Split(new char[] { '\r', '\n' });
            var infos = lines.Select(l =>
            {
                var data = l.Split(new char[] { ':' });
                if (data.Length == 2)
                    return new Tuple<string, string>(data[0], data[1]);
                else
                    return new Tuple<string, string>(data[0], string.Empty);
            }).ToDictionary(k => k.Item1);

            Func<string, double> convertToDouble = str =>
            {
                if (str.EndsWith(" kB"))
                    return Convert.ToDouble(str.Substring(0, str.Length - 3))*1000;
                else
                    return 0;
            };

            Tuple<string,string> d;
            if (infos.TryGetValue("MemTotal", out d))
                yield return new statistics()
                {
                    type = StatisticsType.MemInfoTotal.ToString(),
                    val = convertToDouble(d.Item2).ToString(),
                };
            if (infos.TryGetValue("MemFree", out d))
                yield return new statistics()
                {
                    type = StatisticsType.MemInfoFree.ToString(),
                    val = convertToDouble(d.Item2).ToString(),
                };
        }


        private IEnumerable<statistics> GetCpuMemUsage(SshClient client)
        {
            var pscmd = client.CreateCommand("ps ax -o %cpu,%mem,command |grep -v migration");
            var cmdresult = pscmd.Execute();
            var lines = cmdresult.Split(new char[] { '\r', '\n' }).Skip(1).ToList();
            var infos = lines.Select(l =>
            {
                var data = l.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => s.Trim() != string.Empty)
                    .Select(s => { double d = 0; double.TryParse(s, out d); return d; }).ToList();
                if (data.Count >= 2)
                    return new Tuple<double, double>(data[0], data[1]);
                else
                    return new Tuple<double, double>(0, 0);
            }).ToList();

            var cpu = infos.Sum(d => d.Item1);
            var mem = infos.Sum(d => d.Item2);

            yield return new statistics()
            {
                type = StatisticsType.CPUUsage.ToString(),
                val = cpu.ToString(),
            };

            yield return new statistics()
            {
                type = StatisticsType.MEMUsage.ToString(),
                val = mem.ToString(),
            };
        }

        protected override IEnumerable<statistics> FitToNodeInfo(IEnumerable<statistics> systemstats, node n, List<networkinterface> nics, List<volume> volumes)
        {
            var stats = systemstats.ToList();
            int cpucores = 1;
            StatisticsType st;
            double dvalue;

            // first round, init some values
            foreach (var s in stats)
            {
                if (Enum.TryParse(s.type, out st))
                {
                    switch (st)
                    {
                        case StatisticsType.MachineType:
                            n.MachineType = s.val;
                            yield return s;
                            break;
                        case StatisticsType.DMI:
                            n.Manufacturer = s.val;
                            if (s.val.Contains("VirtualBox"))
                                n.IsVMHost = 1;
                            yield return s;
                            break;
                        case StatisticsType.UpSeconds:
                            n.LastBoot = DateTime.UtcNow.AddSeconds(Convert.ToDouble(s.val) * -1);
                            yield return s;
                            break;
                        case StatisticsType.MemInfoTotal:
                            if (double.TryParse(s.val, out dvalue))
                            {
                                n.TotalMemory = (float)dvalue;
                                yield return s;
                            }
                            break;
                        case StatisticsType.CPUCores:
                            if (double.TryParse(s.val, out dvalue))
                            {
                                cpucores = (int)dvalue;
                                yield return s;
                            }
                            break;
                        case StatisticsType.VolumeTotal:
                            if (double.TryParse(s.val, out dvalue))
                            {
                                var foundv = volumes.FirstOrDefault(v => v.name == s.category);
                                if (foundv == null)
                                {
                                    foundv = new volume() { name = s.category };
                                    volumes.Add(foundv);
                                }
                                foundv.capacity = (float)dvalue * (float)1024;
                                foundv.LastSync = DateTime.UtcNow;
                                yield return s;
                            }
                            break;
                        case StatisticsType.InterfaceIp:
                            var foundnic = nics.FirstOrDefault(nic => nic.caption == s.category);
                            if (foundnic == null)
                            {
                                foundnic = new networkinterface() { caption = s.category };                             
                                nics.Add(foundnic);
                            }
                            foundnic.LastSync = DateTime.UtcNow;
                            foundnic.NetworkAddress = s.val;
                            yield return s;
                            break;
                    }
                }
            }

            // second round.
            foreach (var s in stats)
            {
                if (Enum.TryParse(s.type, out st))
                {
                    switch (st)
                    {
                        case StatisticsType.CPUUsage:
                            double d;
                            if (double.TryParse(s.val, out d))
                            {
                                n.CPULoad = (int)(d / (double)cpucores);
                                yield return s;
                            }
                            break;
                        case StatisticsType.MEMUsage:
                            if (double.TryParse(s.val, out dvalue))
                            {
                                n.MemoryUsed = (float)(dvalue * n.TotalMemory /100);
                                yield return s;
                            }
                            break;

                        case StatisticsType.VolumeUsage:
                            if (double.TryParse(s.val, out dvalue))
                            {
                                var foundv = volumes.FirstOrDefault(v => v.name == s.category);
                                if (foundv != null)
                                {
                                    foundv.LastSync = DateTime.UtcNow;
                                    foundv.usage = (float)dvalue * (float)1024;
                                    yield return s;
                                }
                            }
                            break;
                    }
                }

            }

            n.status = (int)Hict.node.NodeStatus.Active;
            n.Ip = string.Join(",", nics.Select(nic => nic.NetworkAddress).ToArray());
        }

        protected override IEnumerable<statistics> TranslateToStats(IEnumerable<statistics> systemstats, node n, List<networkinterface> nics, List<volume> volumes)
        {
            var time = DateTime.Now;
            yield return new statistics() { nodeid = n.id, begintime = time, endtime = time, category = "MEM", type = "FREE", cap = n.TotalMemory, avg = Convert.ToDouble(n.TotalMemory - n.MemoryUsed) };
            yield return new statistics() { nodeid = n.id, begintime = time, endtime = time, category = "MEM", type = "USE", cap = n.TotalMemory, avg = Convert.ToDouble(n.MemoryUsed) };
            yield return new statistics() { nodeid = n.id, begintime = time, endtime = time, category = "CPU", type = "CPU", avg = Convert.ToDouble(n.CPULoad) };
        }
    }
}
