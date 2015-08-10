using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hict
{
    public class hostinfo
    {
        public string host { get; set; }
        public string userid { get; set; }
        public string pass { get; set; }

        public string[] collectstats { get; set; }

        public Tuple<List<statistics>, List<networkinterface>, List<volume>, node> DoPool(node n = null, List<networkinterface> nics = null, List<volume> volumes = null)
        {
            if (n == null)
            {
                n = new node() { name = host };
            }
            if (nics == null)
            {
                nics = new List<networkinterface>();
            }
            if (volumes == null)
            {
                volumes = new List<volume>();
            }
            var performancestats = FitToNodeInfo(DoSystemUniquePool(), n, nics, volumes).ToList();
            nics.ForEach(ni => ni.nodeid = n.id);
            volumes.ForEach(v => v.nodeid = n.id);
            var translatedstats = TranslateToStats(performancestats, n, nics, volumes).ToList();
            return Tuple.Create(translatedstats, nics, volumes, n);
        }

        protected virtual IEnumerable<statistics> DoSystemUniquePool() { yield break; }

        /// <summary>
        /// fit pool stats to nodes info, and return none-node info stats
        /// </summary>
        /// <param name="stats"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        protected virtual IEnumerable<statistics> FitToNodeInfo(IEnumerable<statistics> systemstats, node n, List<networkinterface> nics, List<volume> volumes) { foreach (var s in systemstats) yield return s; }

        protected virtual IEnumerable<statistics> TranslateToStats(IEnumerable<statistics> systemstats, node n, List<networkinterface> nics, List<volume> volumes) { foreach (var s in systemstats) yield return s; }
    }
}
