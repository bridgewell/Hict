using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hict
{
    public class OpenTSDB
    {
        public static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static DateTime unixtimebegin = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public List<Tuple<string, int>> TSDBPorts;

        public OpenTSDB(IEnumerable<Tuple<string, int>> tsdb)
        {
            TSDBPorts = new List<Tuple<string, int>>(tsdb);
        }

        public string AddData(ICollection<TSDBData> data)
        {
            for (int i=0;i<TSDBPorts.Count;i++)
            {
                var tsdb = TSDBPorts[i];
                string postdata = string.Empty;
                try
                {
                    var lst = data.ToList();
                    if (lst.Count == 0)
                        return string.Empty;

                    postdata = JsonConvert.SerializeObject(lst);

                    var request = WebRequest.Create(string.Format("http://{0}:{1}/api/put", tsdb.Item1, tsdb.Item2)) as HttpWebRequest;
                    request.SendChunked = false;
                    request.ServicePoint.Expect100Continue = false;
                    request.Method = "POST";

                    using (var reqstream = new StreamWriter(request.GetRequestStream(), Encoding.ASCII))
                    {
                        reqstream.Write(postdata);
                    }

                    using (var respstream = new StreamReader(request.GetResponse().GetResponseStream(), Encoding.UTF8))
                    {
                        return respstream.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    logger.Fatal("post " + postdata + " to "+tsdb.Item1+":"+tsdb.Item2+" raise " + ex.ToString());

                    var wex = ex as WebException;
                    if ((wex != null) && (wex.Response != null))
                    {
                        var stream = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8);
                        logger.Fatal("error web result is :" + stream.ReadToEnd());
                    }
                    if (i == TSDBPorts.Count - 1)
                        throw;
                }
            }
            return null;
        }

        public static long GetUnixTime(DateTime dt)
        {
            return (long)((dt.ToUniversalTime() - unixtimebegin).TotalSeconds);
        }
    }

    public class TSDBData
    {
        public string metric;
        public long timestamp;
        public double value;
        public Dictionary<string, string> tags;
    }
}
