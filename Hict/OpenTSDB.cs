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

        public string Host;
        public int Port;

        public OpenTSDB(string host, int port)
        {
            Host = host;
            Port = port;
        }

        public string AddData(ICollection<TSDBData> data)
        {
            int trycount = 3;
            while (true)
            {
                string postdata = string.Empty;
                try
                {
                    var lst = data.ToList();

                    postdata = JsonConvert.SerializeObject(lst);

                    var request = WebRequest.Create(string.Format("http://{0}:{1}/api/put", Host, Port)) as HttpWebRequest;
                    request.SendChunked = false;
                    request.ServicePoint.Expect100Continue = false;
                    request.Method = "POST";

                    logger.Debug("posting " + postdata + " to opentsdb.");

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
                    logger.Fatal("post " + postdata + " raise " + ex.ToString());

                    var wex = ex as WebException;
                    if (wex != null)
                    {
                        var stream = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8);
                        logger.Fatal("error web result is :" + stream.ReadToEnd());
                    }
                    trycount--;

                    if (trycount == 0)
                        throw;
                    else
                        Thread.Sleep(TimeSpan.FromSeconds(5));
                }
            }
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
