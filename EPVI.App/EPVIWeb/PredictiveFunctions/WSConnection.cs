using CCCSoftware.Produktion.EPVI.Data.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Xml.Linq;

namespace EPVIWeb.PredictiveFunctions
{
    /// <summary>
    /// DB connection needs to be redone, just adding general descriptions
    /// </summary>
    public static class WSConnection
    {

        static string cccxml_get = "<Envelope xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" >\n" +
                                    "<Body xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" >\n" +
                                    "<CCCXML_GET xmlns=\"services.wsdl\" >\n" +
                                    "<json xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:string\" >{0}</json>\n" +
                                    "</CCCXML_GET>\n </Body>\n </Envelope>";

        static string cccxml_getwhere = "<Envelope xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                            "<Body xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                            "<CCCXML_GETWHERE xmlns=\"services.wsdl\" > " +
                            "<json xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:string\">" +
                            "{0}" +
                            "</json></CCCXML_GETWHERE></Body></Envelope>";

        /// <summary>
        /// general GET method for process data
        /// </summary>
        /// <param name="processIds"></param>
        /// <param name="precision"></param>
        /// <param name="links"></param>
        /// <returns></returns>
        public static IEnumerable<PredictiveProcessData> GetProcessFromWS(int[] processIds, decimal precision = 0m, int links = 5)
        {
            List<PredictiveProcessData> ret = new List<PredictiveProcessData>();

            foreach (XElement ent in PredictiveProcessData.LoadEnergyProcessDataFromXml(GetCccXmlHttpResponse("GET", String.Format(cccxml_get, JsonConvert.SerializeObject(processIds)))))
            {
                ret.Add(PredictiveProcessData.ParseProcessData(ent, links, precision));
            }
            return ret;
        }

        /// <summary>
        /// generic Response Handler
        /// </summary>
        /// <param name="method"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string GetCccXmlHttpResponse(string method, string data)
        {
            var httpRequest = WebRequest.Create("http://vmdbpedia.informatik.uni-leipzig.de:8890/cccxml");
            httpRequest.Method = "POST";
            httpRequest.ContentType = "text/xml; charset=utf-8";
            httpRequest.Headers.Add("SOAPAction: CCCXML_" + method);
            var stringWriter = new StreamWriter(httpRequest.GetRequestStream(), Encoding.UTF8);
            stringWriter.Write(data);
            stringWriter.Close();
            StreamReader srd = null;
            try
            {
                var wr = (HttpWebResponse)httpRequest.GetResponse();
                srd = new StreamReader(wr.GetResponseStream());
                var envelope = XDocument.Load(new StringReader(srd.ReadToEnd()));
                return envelope.Descendants(XName.Get("CallReturn")).ElementAt(0).Value;
            }
            catch (WebException e)
            {
                srd = new StreamReader(e.Response.GetResponseStream());
                var envelope = XDocument.Load(new StringReader(srd.ReadToEnd()));
                return envelope.Descendants(XName.Get("faultstring")).ElementAt(0).Value;
            }
        }

        /// <summary>
        /// list available processids of dataset
        /// </summary>
        /// <returns></returns>
        public static string GetAvailableIds()
        {
            string env = "<Envelope xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\"> \n" +
                            "<Body xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > \n" +
                            "<CCCXML_PROCESSES xmlns=\"services.wsdl\"></CCCXML_PROCESSES> \n" +
                            "</Body></Envelope>";
            return GetCccXmlHttpResponse("PROCESSES", env);
        }

        /// <summary>
        /// save result of ParameterEvaluation (based on the (sub) dataset) to the DB (
        /// </summary>
        /// <param name="name">param name</param>
        /// <param name="type">param type (PredictiveFunctions.ParamType)</param>
        /// <param name="count">count of distinct values</param>
        /// <param name="values">list of all distinct values</param>
        /// <param name="ignore">ignore this param for this dataset</param>
        public static void InsertParamStatistic(string name, string type, int count, IEnumerable<object> values, bool ignore = false)
        {
            string env = "<Envelope xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                "<Body xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" >" +
                "<CCCXML_INSERTPARAM xmlns=\"services.wsdl\" >" +
                "<name xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:string\" >{0}</name>" +
                "<type xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:string\" >{1}</type>" +
                "<counti xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:int\" >{2}</counti>" +
                "<valuesi xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:string\" >{3}</valuesi>" +
                "<ignore xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:int\" >{4}</ignore>" +
                "</CCCXML_INSERTPARAM></Body></Envelope>";

            env = String.Format(env, name, type, count, String.Join(";", values.ToArray()), ignore ? 1 : 0);

            GetCccXmlHttpResponse("INSERTPARAM", env);
        }

        /// <summary>
        /// get precalculated result of a ParameterEvaluation
        /// </summary>
        /// <returns></returns>
        public static string GetParamStatistics()
        {
            string env = "<Envelope xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                "<Body xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                "<CCCXML_GETPARAMS xmlns=\"services.wsdl\" ></CCCXML_GETPARAMS> " +
                "</Body></Envelope>";

            return GetCccXmlHttpResponse("GETPARAMS", env);
        }

        /// <summary>
        /// returns json object of all available params with all distinct values
        /// </summary>
        /// <returns></returns>
        public static string GetAvailableParams()
        {
            string ret = "{ ";

            var zz = PredictiveProcessData.ParseProcessData(PredictiveProcessData.LoadEnergyProcessDataFromXml(GetCccXmlHttpResponse("GET", String.Format(cccxml_get, JsonConvert.SerializeObject(new int[] { 33000 })))).First(), 5, 0);
            
            foreach (ProcessDataParam param in zz.Params)
            {
                string val = "";
                if (param.NumValue != null)
                    val = param.NumValue.ToString();
                else if (param.DateValue != null)
                    val = param.DateValue.ToString();
                else
                    val = param.StrValue;
                ret += "\"" + param.Name + "\" : \"" + val + "\", ";
            }
            return ret.Substring(0, ret.Length - 2) + " }";
        }

        /// <summary>
        /// save new process directly from xml
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        public static bool InsertProcess(FileStream xml)
        {
            string env = "<Envelope xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                            "<Body xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                            "<CCCXML_INSERT xmlns=\"services.wsdl\" > " +
                            "<process_xml xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:string\">" +
                            "<![CDATA[<Data>{0}</Data>]]>" +
                            "</process_xml></CCCXML_INSERT></Body></Envelope>";
            foreach (XElement ent in PredictiveProcessData.LoadEnergyProcessDataFromXml(xml))
            {
                string result = GetCccXmlHttpResponse("INSERT", String.Format(env, ent.ToString()));
                if (!result.Contains("Non unique primary key") && !result.Contains("inserted"))
                {
                    xml.Close();
                    return false;
                }
            }
            xml.Close();
            return true;
        }

        /// <summary>
        /// saves (part) of a PredictiveProcessCollection to the DB
        /// </summary>
        /// <param name="data">PredictiveProcessCollection as json obj</param>
        /// <returns></returns>
        public static bool InsertPredictiveProcessCollections(string data)
        {
            string env = "<Envelope xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " + 
                "<Body xmlns=\"http://schemas.xmlsoap.org/soap/envelope/\" > " +
                "<CCCXML_INSERTENVELOPE xmlns=\"services.wsdl\" > " +
                "<json xmlns=\"services.wsdl\" type=\"http://www.w3.org/2001/XMLSchema:string\" >{0}</json> " +
                "</CCCXML_INSERTENVELOPE></Body></Envelope>";
            if(bool.Parse(GetCccXmlHttpResponse("INSERTENVELOPE", String.Format(env, data))))
                return true;
            return false;
        }

        /// <summary>
        /// generates a json query for processes which match the intersection of certain parameter scopes (connected with 'AND')
        /// a stored procedure executes the query on the DB
        /// </summary>
        /// <param name="paramms">Item1: ParamName, Item2: ParamType, Item3: fromValue, Item4: toValue</param>
        /// <returns>array of processids</returns>
        public static int[] GetProcessesWhere(List<Tuple<string, string, string, string>> paramms)
        {


            string queryJson = "{ ";
            foreach (var param in paramms)
            {
                string val1 = param.Item3;
                string val2 = param.Item4;
                if (param.Item2 == "decimal")
                {
                    val1 = val1.Replace(",", ".");
                    if (!val1.Contains(".")) //not!
                    {
                        val1 += ".00000";
                    }
                    else
                    {
                        val1 = val1.PadRight(5 - (val1.Length - 1 - val1.IndexOf(".")), '0');
                    }
                    if (!val2.Contains(".")) //not!
                    {
                        val2 += ".00000";
                    }
                    else
                    {
                        val2 = val2.PadRight(5 - (val2.Length - 1 - val2.IndexOf(".")), '0');
                    }
                }
                queryJson += "\"" + param.Item1 + "\" : \"" + val1 + "\",";
            }
            queryJson = queryJson.Substring(0, queryJson.Length - 1) + "}";
            string result = GetCccXmlHttpResponse("GETWHERE", String.Format(cccxml_getwhere, queryJson));

            return JsonConvert.DeserializeObject<int[]>(result);
        }

        /// <summary>
        /// s.o.
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static int[] GetProcessesWhere(string json)
        {
            string result = GetCccXmlHttpResponse("GETWHERE", String.Format(cccxml_getwhere, json));
            return JsonConvert.DeserializeObject<int[]>(result);
        }
    }
}