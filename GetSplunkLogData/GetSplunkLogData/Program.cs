using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Web;
using System.Xml;

namespace GetSplunkLogData
{
    class Program
    {
        static XmlDocument executeRequest(string url, string sessionKey, string args, string httpMethod)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) =>
            {
                return true;
            };

            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(url);
            req.Method = httpMethod;
            if (!string.IsNullOrEmpty(sessionKey))
                req.Headers.Add("Authorization", "Splunk " + sessionKey);
            if (httpMethod == "POST")
            {
                if (!string.IsNullOrEmpty(args)) req.ContentLength = args.Length;
                req.ContentType = "application/x-www-form-urlencoded";
                Stream reqStream = req.GetRequestStream();
                StreamWriter sw = new StreamWriter(reqStream);
                sw.Write(args);
                sw.Close();
            }
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            StreamReader sr = new StreamReader(res.GetResponseStream());
            if (sr.EndOfStream) return null;
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(sr);
            sr.Close();
            return xmlDoc;
        }
        static void Main(string[] args)
        {
            string baseurl = "https://localhost:8089";
            string user = "admin";
            string password = "Ghouse01*";
            //string searchQuery = "source=\"\\\\\\\\YOGA.psr.rd.hpicorp.net\\\\StaplesMiddleWare\\\\log.txt\" ERROR&earliest=-7d&latest=now";
            string searchQuery = "source=\"F:\\\\Services\\\\StaplesMiddleWare\\\\log.txt\" earliest=-7d latest=now"; // ERROR;

            // Authenticate on the server and get the session key            
            string url = baseurl + "/services/auth/login";
            string reqArgs = string.Format("username={0}&password={1}", user, password);
            XmlDocument doc = executeRequest(url, null, reqArgs, "POST");
            string sessionKey = doc.SelectSingleNode("/response/sessionKey").InnerText;
            Console.WriteLine("Session key: " + sessionKey);

            // Dispatch a new search and return search id            
            url = baseurl + "/services/search/jobs";
            reqArgs = string.Format("search={0}", HttpUtility.UrlPathEncode("search " + searchQuery));
            doc = executeRequest(url, sessionKey, reqArgs, "POST");
            string sid = doc.SelectSingleNode("/response/sid").InnerText;
            Console.WriteLine("Search id: " + sid);

            // Wait for search to finish
            //
            url = baseurl + "/services/search/jobs/" + sid;
            bool isDone = false;
            int eventCount = 0;
            do
            {
                doc = executeRequest(url, sessionKey, null, "GET");
                if (doc == null)
                {
                    System.Threading.Thread.Sleep(200);
                    continue;
                }
                XmlNamespaceManager context = new XmlNamespaceManager(doc.NameTable);
                context.AddNamespace("s", "http://dev.splunk.com/ns/rest");
                context.AddNamespace("feed", "http://www.w3.org/2005/Atom");
                XmlNode ecNode = doc.SelectSingleNode("//feed:entry/feed:content/s:dict/s:key[@name='eventCount'][1]", context);
                if (null != ecNode)
                    eventCount = int.Parse(ecNode.InnerText);
                XmlNode idNode = doc.SelectSingleNode("//feed:entry/feed:content/s:dict/s:key[@name='isDone'][1]", context);
                isDone = idNode.InnerText == "1" ? true : false;
            } while (!isDone);


            Console.WriteLine(string.Format("Search job is done, {0} results found", eventCount));

            // Get search results
            //
            if (eventCount > 0)
            {
                url = string.Format("{0}/services/search/jobs/{1}/results", baseurl, sid);
                doc = executeRequest(url, sessionKey, null, "GET");
                //Console.WriteLine("Results: \n" + doc.InnerXml);

                string json = doc.InnerXml.Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "");
                json = JsonConvert.SerializeXmlNode(doc);
                RootObject root = JsonConvert.DeserializeObject<RootObject>(json.Remove(1, 46));

                System.Data.DataTable table = new System.Data.DataTable();
                table.Columns.Add("Date Time", typeof(DateTime));
                table.Columns.Add("Log Message", typeof(string));

                foreach (Result rs in root.results.result)
                {
                    foreach (var f in rs.field)
                    {
                        if (null != f.v)
                        {
                            int count = 0;
                            string finalString = string.Empty;
                            DateTime dt = DateTime.MinValue;

                            foreach (string str in f.v.__text)
                            {
                                if (count == 0)
                                {
                                    DateTime.TryParse(str.Substring(0, 18), out dt);
                                    finalString = finalString + str.Substring(19).Trim();
                                    count = 1;
                                }
                                else
                                {
                                    finalString = finalString + str;
                                }

                            }

                            table.Rows.Add(dt, finalString);
                        }

                    }
                }

            }
            else
            {
                Console.WriteLine("Search returned zero results");
            }


        }
    }

    public class FieldOrder
    {
        public List<string> field { get; set; }
    }

    public class Meta
    {
        public FieldOrder fieldOrder { get; set; }
    }

    public class Sg
    {
        [JsonProperty("@h")]
        public string _h { get; set; }
        [JsonProperty("#text")]
        public string _text { get; set; }
    }

    public class V
    {
        [JsonProperty("@xml:space")]
        public string _space { get; set; }
        [JsonProperty("@trunc")]
        public string _trunc { get; set; }
        [JsonProperty("#text")]
        public List<string> __text { get; set; }
        public Sg sg { get; set; }
    }

    public class Field
    {
        [JsonProperty("@k")]
        public string _k { get; set; }
        public object value { get; set; }
        public V v { get; set; }
    }

    public class Result
    {
        [JsonProperty("@offset")]
        public string __offset { get; set; }
        public List<Field> field { get; set; }
    }

    public class Results
    {
        [JsonProperty("@preview")]
        public string __preview { get; set; }
        public Meta meta { get; set; }
        public List<Result> result { get; set; }
    }

    public class RootObject
    {
        public Results results { get; set; }
    }
}
