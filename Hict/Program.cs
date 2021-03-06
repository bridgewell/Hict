﻿using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hict
{
    class Program
    {
        public static Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static TimeSpan Pollinterval = TimeSpan.FromMinutes(5);
        public static TimeSpan PollTimedout = TimeSpan.FromMinutes(5);

        static void Main(string[] args)
        {
            var settingfile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

            while (true)
            {
                logger.Info("Start monitor.");
                Task MonitorTask = null;
                if (File.Exists(settingfile))
                {
                    var setting = JsonConvert.DeserializeObject<HostSettings>(File.ReadAllText(settingfile));
                    var tsdbServers = new List<List<Tuple<string, int>>>();
                    if (string.IsNullOrWhiteSpace(setting.TSDBHost) == false)
                    {
                        tsdbServers.Add(new List<Tuple<string, int>>() { Tuple.Create(setting.TSDBHost, setting.TSDBPort) });
                    }

                    if (string.IsNullOrWhiteSpace(setting.MultipleTSDB) == false)
                    {
                        tsdbServers.AddRange(setting.MultipleTSDB.Split(new char[] { ',' })
                            .Select(t => new List<Tuple<string, int>>(t.Split(new char[] { ';' }).Select(x =>
                            {
                                var data = x.Split(new char[] { ':' });
                                return Tuple.Create(data[0], Convert.ToInt32(data[1]));
                            }))));
                    }

                    var tsdbs = tsdbServers.Select(t => new OpenTSDB(t));

                    MonitorTask = Task.Factory.StartNew(() =>
                    {
                        Parallel.ForEach(CreateHostInfoBySettings(setting), h =>
                        {
                            var poolthread = new Thread(() =>
                            {
                                List<TSDBData> data = null;
                                try
                                {
                                    data = HostInfoToTSDB(h, setting.MetricHeader).ToList();
                                }
                                catch (Exception ex)
                                {
                                    logger.Error("collect data from {0} raise {1}", h.host, ex.ToString());
                                    foreach (var tsdb in tsdbs)
                                    {
                                        try
                                        {
                                            tsdb.AddData(new TSDBData[] {new TSDBData()
                                        {
                                        metric = setting.MetricHeader + ".Status",
                                        tags = new Dictionary<string, string>() { 
                                            {"Host",h.host},
                                        },
                                        timestamp = OpenTSDB.GetUnixTime(DateTime.UtcNow),
                                        value = -1,
                                        }});
                                        }
                                        catch (Exception ex2)
                                        {
                                            logger.Fatal(ex2.ToString());
                                        }
                                    }
                                    return;
                                }

                                if ((data == null) || (data.Count == 0))
                                {
                                    logger.Warn("cloud not get data from {0}", h.host);
                                    return;
                                }

                                foreach (var tsdb in tsdbs)
                                {
                                    try
                                    {
                                        tsdb.AddData(data);
                                    }
                                    catch (Exception ex3)
                                    {
                                        logger.Fatal(ex3.ToString());
                                    }
                                }

                                logger.Info("{0} data collected.", h.host);
                            });
                            poolthread.Start();
                            if (poolthread.Join(PollTimedout) == false)
                            {
                                poolthread.Abort();
                            }
                        });
                        logger.Info("All Data pulled.");
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                Thread.Sleep(Pollinterval);

                if (MonitorTask != null)
                {
                    MonitorTask.Wait();
                    MonitorTask.Dispose();
                }
            }
        }

        static IEnumerable<hostinfo> CreateHostInfoBySettings(HostSettings settings)
        {
            if (settings == null)
                yield break;
            if (settings.Windows != null)
            {
                foreach (var h in settings.Windows)
                    yield return new windowshostinfo()
                    {
                        host = h.host,
                        pass = h.pass,
                        userid = h.userid,
                        collectstats = h.collectstats,
                    };
            }
            if (settings.Linux != null)
            {
                foreach (var h in settings.Linux)
                    yield return new linuxhostinfo()
                    {
                        host = h.host,
                        pass = h.pass,
                        userid = h.userid,
                        collectstats = h.collectstats,
                    };
            }
        }

        static string getstatvalue(IEnumerable<statistics> stats, string typename)
        {
            var foundstat = stats.FirstOrDefault(f => f.type == typename);
            if (foundstat == null)
                return string.Empty;
            else
                return foundstat.val;
        }

        static Regex invalidateRegex = new Regex(@"[\\/\(\)\[\]]+", RegexOptions.Compiled);

        static string FixMetricName(string name)
        {
            return invalidateRegex.Replace(name, m => "_").Replace(" ", string.Empty);
        }

        static IEnumerable<TSDBData> HostInfoToTSDB(hostinfo info, string metricHeader)
        {
            Tuple<List<statistics>, List<networkinterface>, List<volume>, node> pooledinfo = null;
            var nodeinfo = new node() { name = info.host, status = (int)Hict.node.NodeStatus.Active, };
            var nics = new List<networkinterface>();
            var volumes = new List<volume>();
            var timestamp = OpenTSDB.GetUnixTime(DateTime.UtcNow);
            var couldcontinue = true;
            try
            {
                pooledinfo = info.DoPool(nodeinfo, nics, volumes);
            }
            catch (Exception ex)
            {
                logger.Fatal("collect data from {0} raise {1}", info.host, ex.ToString());
                if (nodeinfo != null)
                    nodeinfo.status = (int)node.NodeStatus.Unreachable;
                couldcontinue = false;
            }

            if (couldcontinue == false)
            {
                yield return new TSDBData()
                {
                    metric = metricHeader + ".Status",
                    tags = new Dictionary<string, string>() { 
                        {"Host",nodeinfo.name},
                    },
                    timestamp = timestamp,
                    value = -1,
                };
                yield break;
            }

            nodeinfo.PoolIntervalSeconds = (int)Pollinterval.TotalSeconds;

            nodeinfo.LastSync = DateTime.UtcNow;

            // update statictics
            foreach (var s in pooledinfo.Item1)
            {
                switch (s.category)
                {
                    case "MEM":
                        switch (s.type)
                        {
                            case "FREE":
                                yield return new TSDBData()
                                {
                                    metric = metricHeader + ".Memory.Free",
                                    tags = new Dictionary<string, string>() { 
                                       {"Host",nodeinfo.name},
                                    },
                                    timestamp = timestamp,
                                    value = s.avg,
                                };
                                break;
                            case "USE":
                                yield return new TSDBData()
                                {
                                    metric = metricHeader + ".Memory.Usage",
                                    tags = new Dictionary<string, string>() { 
                                       {"Host",nodeinfo.name},
                                    },
                                    timestamp = timestamp,
                                    value = s.avg,
                                };
                                break;
                        }
                        break;
                    case "CPU":
                        yield return new TSDBData()
                        {
                            metric = metricHeader + ".CPU",
                            tags = new Dictionary<string, string>() { 
                                       {"Host",nodeinfo.name},
                                    },
                            timestamp = timestamp,
                            value = s.avg,
                        };
                        break;
                }
            }
            // update volumes
            foreach (var v in volumes)
            {
                var volMetric = metricHeader + ".Volume";
                yield return new TSDBData()
                {
                    metric = volMetric + ".Usage",
                    tags = new Dictionary<string, string>() { 
                        {"Host",nodeinfo.name},
                        {"Volume",FixMetricName(v.name)},
                    },
                    timestamp = timestamp,
                    value = v.usage,
                };

                yield return new TSDBData()
                {
                    metric = volMetric + ".Capacity",
                    tags = new Dictionary<string, string>() { 
                        {"Host",nodeinfo.name},
                        {"Volume",FixMetricName(v.name)},
                    },
                    timestamp = timestamp,
                    value = v.capacity,
                };
            }

            // update network interfaces.

            foreach (var n in nics)
            {
                var nicMetric = metricHeader + ".NIC";
                if (n.IsTxRxMode)
                {
                    yield return new TSDBData()
                    {
                        metric = nicMetric + ".RxBytes",
                        tags = new Dictionary<string, string>() {
                        {"Host",nodeinfo.name},
                        {"NIC", FixMetricName(n.caption)},
                    },
                        timestamp = timestamp,
                        value = n.RxBytes
                    };

                    yield return new TSDBData()
                    {
                        metric = nicMetric + ".TxBytes",
                        tags = new Dictionary<string, string>() {
                        {"Host",nodeinfo.name},
                        {"NIC", FixMetricName(n.caption)},
                    },
                        timestamp = timestamp,
                        value = n.TxBytes
                    };
                }
                else
                {
                    yield return new TSDBData()
                    {
                        metric = nicMetric + ".InBps",
                        tags = new Dictionary<string, string>() {
                        {"Host",nodeinfo.name},
                        {"NIC", FixMetricName(n.caption)},
                    },
                        timestamp = timestamp,
                        value = n.InBps
                    };

                    yield return new TSDBData()
                    {
                        metric = nicMetric + ".OutBps",
                        tags = new Dictionary<string, string>() {
                        {"Host",nodeinfo.name},
                        {"NIC", FixMetricName(n.caption)},
                    },
                        timestamp = timestamp,
                        value = n.OutBps
                    };
                }
            }

            yield return new TSDBData()
            {
                metric = metricHeader + ".Status",
                tags = new Dictionary<string, string>() { 
                        {"Host",nodeinfo.name},
                    },
                timestamp = timestamp,
                value = 1,
            };
        }
    }
}
