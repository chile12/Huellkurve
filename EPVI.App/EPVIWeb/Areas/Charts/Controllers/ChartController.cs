using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Data.Entity;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using CCCSoftware.Produktion.EPVI.Charts.Services.Data;
using CCCSoftware.Produktion.EPVI.Common.Grids;
using CCCSoftware.Produktion.EPVI.Common.Logging;
using CCCSoftware.Produktion.EPVI.Common.Mvc;
using CCCSoftware.Produktion.EPVI.Common.Security;
using CCCSoftware.Produktion.EPVI.Common.Tools.Data;
using CCCSoftware.Produktion.EPVI.Data.Codes;
using CCCSoftware.Produktion.EPVI.Data.DataAccess;
using CCCSoftware.Produktion.EPVI.Data.Entities;
using EPVIWeb.PredictiveFunctions;
using EPVIWeb.Areas.Charts.Models;
using EPVIWeb.Controllers;
using EPVIWeb.Properties;
using MvcContrib.UI.Grid;
using System.Xml.Linq;
using System.Xml;
using System.IO;
using System.Text;
using System.Drawing;
using System.Net;
using System.Web.Providers.Entities;
using System.Reflection;
using Newtonsoft.Json;

namespace EPVIWeb.Areas.Charts.Controllers
{
    /// <summary>
    /// experimental mostly static! Controller
    /// </summary>
	public class ChartController : DbController
	{
        private static PredictiveProcessCollection DataContainers = new PredictiveProcessCollection(new List<PredictiveProcessData>());     //base data for controller
        private static bool loadedAll = false;                                                                                              //~

        /// <summary>
        /// if not already loaded, this will download a process from DB and generate a PredictiveProcessData
        /// </summary>
        /// <param name="processId">id</param>
        /// <returns></returns>
        public static PredictiveProcessData GetProcessData(int processId)
        {
            if (processId == 0)
                return null;
            PredictiveProcessData zw = DataContainers.Where(x => x.ProcessDataId == processId.ToString()).FirstOrDefault();
            if (zw == null)
            {
                zw = WSConnection.GetProcessFromWS(new int[] { processId }).FirstOrDefault();
                DataContainers.Add(zw);
            }
            return zw;
        }

        /// <summary>
        /// provides Map: processid -> process name 
        /// </summary>
        public static Dictionary<string, string> AvailableIds
        {
            get
            {
                return DataContainers.ToDictionary(x => x.ProcessDataId, x => x.Name);
            }
        }

        /// <summary>
        /// publishes available ParameterGroups based on Datacontainer Collection
        /// </summary>
        public static Dictionary<ParamType, Dictionary<string, List<Tuple<object, string>>>> ParameterGroups
        {
            get
            {
                if (!loadedAll)
                    LoadParams();
                return DataContainers.GetParameters();
            }
        }
		#region Actions

        /// <summary>
        /// loads a collection pf ProcessData from DB, generates the Envelopes based on precision and/or links and saves results to DB
        /// </summary>
        /// <param name="processids">given list of process ids</param>
        /// <param name="precision">level of precision (compared to normalized View - [0,1]!)</param>
        /// <param name="links">number of links('Glieder') we want our Envelope to have</param>
        /// <returns></returns>
        public ActionResult SaveEnvelopes(int[] processids = null, decimal precision = 0m, int links = 5)
        {
            if (processids == null)
                processids = WSConnection.GetProcessesWhere(new List<Tuple<string, string, string, string>>());
            PredictiveProcessCollection coll = new PredictiveProcessCollection(processids, precision, links);
            PredictiveProcessCollection.SaveEnvelopes(coll);
            return Content("collection was saved", "text/html");
        }

        /// <summary>
        /// loads precalculated param evaluations of a dataset from DB
        /// </summary>
        public static void LoadParams()
        {
            string json = WSConnection.GetParamStatistics();
            string [][] ret = Newtonsoft.Json.JsonConvert.DeserializeObject<string[][]>(json);
            if (ret.Length == 0)
            {
                var processids = WSConnection.GetProcessesWhere(new List<Tuple<string, string, string, string>>());
                DataContainers = new PredictiveProcessCollection(processids);
                foreach (ParamEvaluation eva in DataContainers.ParamEvaluations.Values)
                    WSConnection.InsertParamStatistic(eva.ParamName, eva.ParamType.ToString(), eva.ValueCount, eva.DistinctValues.Select(x => x.Item1));
            }
            else
            {
                DataContainers = new PredictiveProcessCollection(new List<PredictiveProcessData>());
                for (int i = 0; i < ret.Length; i++)
                {
                    DataContainers.ParamEvaluations.Add(ret[i][0], new ParamEvaluation(ret[i][0], ret[i][1], ret[i][2].Split(';').ToList(), int.Parse(ret[i][3])));
                }
            }
            loadedAll = true;
        }

        /// <summary>
        /// saves the content of a process xml file to the DB
        /// </summary>
        /// <param name="path">~</param>
        /// <returns></returns>
        public ActionResult InsertProcessFileConetent(string path)
        {
            FileInfo inf = new FileInfo(path);
            if (inf.Exists)
            {
                FileStream stream = new FileStream(inf.FullName, FileMode.Open);
                if (WSConnection.InsertProcess(stream))
                    return Json("Prozesse wurden eingepflegt", JsonRequestBehavior.AllowGet);
            }
            return Json("Ein Fehler ist aufgetreten", JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// given a json object (generated by Chart.js) of a fromat like {someParameterName:["fromValue", "toValue"], someOtherName:...}, 
        /// this method redirects to the page resulting from processes matching the intersection of the given parameter scopes -> all processes which match the given parameters
        /// </summary>
        /// <param name="json">the parameter scope object</param>
        /// <param name="options">additional view options</param>
        /// <returns></returns>
        public ActionResult FindProcessesFromParams(string json, string options = null)
        {
            int[] treffer = WSConnection.GetProcessesWhere(json);
            if (treffer.Length == 0)
                return null;
            string url = HttpContext.Request.Url.Scheme + "://" + HttpContext.Request.Url.Authority + HttpContext.Request.UrlReferrer.AbsolutePath + "?";
            for (int id = 0; id < Math.Min(20, treffer.Length); id++)
                url += "processids=" + treffer[id] + "&";
            return Redirect(url + "graphsep=0&sepiterations=5" + (options == null ? "" : "&options=" + options));
        }

        /// <summary>
        /// Index function differentiates between showing results for just on process |processids| == 1,
        /// and showing multiple processes at a time (always without regard to the actual occurrence time of the process)
        /// </summary>
        /// <param name="processids">list of processes to show</param>
        /// <param name="precision">level of precision (compared to normalized View - [0,1]!)</param>
        /// <param name="links">number of links('Glieder') we want our Envelope to have</param>
        /// <param name="options">view options (which graphs should we draw?)</param>
        /// <param name="par">dont know, never used this</param>
        /// <returns></returns>
		public ActionResult Index(int[] processids, decimal precision = 1m, int links = 5, string options = null, ChartParams par = null)
		{
            DataContainers.Clear();
            DataContainers.EnvelopeLinks = links;
            DataContainers.EnvelopePrecision = precision;

			// Parameter
			if (par == null)
                par = new ChartParams(new List<ChartBlockParams>() { });

            //view options
            var viewOptions = new List<SwhkViewOptions>();
            if (options != null)
            {
                string[] zw = options.Split('@');
                for(int i =0; i < zw.Length; i++)
                {
                        viewOptions.Add((SwhkViewOptions) Enum.Parse(typeof(SwhkViewOptions), zw[i]));
                }
            }
            if (viewOptions.Count == 0)
                viewOptions.Add(SwhkViewOptions.Original);

            if (processids == null && TempData["processids"] != null)
            {
                processids = (int[])TempData["processids"];
            }

#if DEBUG
            if (processids.Length > 5)
                processids = processids.Take(5).ToArray(); 
#endif

			//Chart-Config
			ChartConfig chartConfig = new ChartConfig()
			{
				Name = "Test"
			};

			ChartArea area = new ChartArea()
			{
				Name = "Sollwerthüllkurve",
				ChartType = (int)ChartType.Polyline,
				Seq = 1
			};
			chartConfig.Areas.Add(area);

            ChartBlockParams cbpar = new ChartBlockParams();
            cbpar.Start = DateTime.Now - new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);
            TimeSpan maxTime = new TimeSpan(0L);

            Random rand = new Random();
                     
            foreach (int processid in processids)
            {
                PredictiveProcessData process = GetProcessData(processid);
                process.ViewOptions.AddRange(viewOptions);

                if (process == null)
                    throw new ArgumentException("process-id not found!");
                
                if((viewOptions.Contains(SwhkViewOptions.Original)))
                    area.Functions.Add(new PredictiveChartFunction()
                    {
                        ChartFunctionId = processid,
                        PrecictiveProcessData = process,
                        Name = process.Name,
                        Seq = 1,
                        ChartDataSourceId = 1,
                        FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                    });

                //if just one process id -> show some specifig graphs
                if (processids.Length == 1)
                {
                    //'Sollhüllwertkurve'
                    if (viewOptions.Contains(SwhkViewOptions.Envelope))
                        area.Functions.Add(new PredictiveChartFunction()
                        {
                            ChartFunctionId = processid,
                            PrecictiveProcessData = process,
                            Name = process.Name + "-envelope",
                            Seq = 2,
                            FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                        });
                    //nomalized graph
                    if (viewOptions.Contains(SwhkViewOptions.Normalized))
                        area.Functions.Add(new PredictiveChartFunction()
                        {
                            ChartFunctionId = processid,
                            PrecictiveProcessData = process,
                            Name = process.Name + "-normalized",
                            Seq = 3,
                            FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                        });
                    //lower bound
                    if (viewOptions.Contains(SwhkViewOptions.LowerBound))
                        area.Functions.Add(new PredictiveChartFunction()
                        {
                            ChartFunctionId = processid,
                            PrecictiveProcessData = process,
                            Name = process.Name + "-lower",
                            Seq = 4,
                            FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                        });
                    //upper bound
                    if (viewOptions.Contains(SwhkViewOptions.UpperBound))
                        area.Functions.Add(new PredictiveChartFunction()
                        {
                            ChartFunctionId = processid,
                            PrecictiveProcessData = process,
                            Name = process.Name + "-upper",
                            Seq = 5,
                            FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                        });
                }

                if (maxTime < process.UtcTo - process.UtcFrom)
                    maxTime = (TimeSpan)(process.UtcTo - process.UtcFrom);
            }

            //if mor than one id -> add avg graph and stuff
            if (processids.Length > 1)
            {
                if (viewOptions.Contains(SwhkViewOptions.AverageGraph))
                    area.Functions.Add(new PredictiveChartFunction()
                    {
                        ChartFunctionId = -1,
                        PrecictiveProcessData = DataContainers,
                        Name = "Average Graph Normalized",
                        Seq = 6,
                        ChartDataSourceId = 1,
                        FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                    });
                //Sollhüllwert based on AVG Graph
                if (viewOptions.Contains(SwhkViewOptions.AvgEnvelope))
                    area.Functions.Add(new PredictiveChartFunction()
                    {
                        ChartFunctionId = -2,
                        PrecictiveProcessData = DataContainers,
                        Name = "Average Swhk",
                        Seq = 7,
                        ChartDataSourceId = 1,
                        FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                    });
                //lower bound of avg
                if (viewOptions.Contains(SwhkViewOptions.LowerAverage))
                    area.Functions.Add(new PredictiveChartFunction()
                    {
                        ChartFunctionId = -3,
                        PrecictiveProcessData = DataContainers,
                        Name = "Average Graph Lower",
                        Seq = 8,
                        ChartDataSourceId = 1,
                        FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                    });
                //upper bound of avg
                if (viewOptions.Contains(SwhkViewOptions.UpperAverage))
                    area.Functions.Add(new PredictiveChartFunction()
                    {
                        ChartFunctionId = -4,
                        PrecictiveProcessData = DataContainers,
                        Name = "Average Graph Upper",
                        Seq = 9,
                        ChartDataSourceId = 1,
                        FillColor = "#" + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2") + ((byte)rand.Next(256)).ToString("X2")
                    });

            }

            cbpar.Stop = cbpar.Start + maxTime;
            par.Blocks.Add(cbpar);

			//ViewModel
			var viewModel = new ChartViewModel();
			viewModel.Load(chartConfig);
			viewModel.Timelines.AddRange(par.Blocks.Select(b => b.ToTimelineViewModel()));
            viewModel.PredictiveFunctions.AddRange(DataContainers);

			if (viewModel.Timelines.Count != 0)
				SaveDisplayInterval(viewModel.Timelines[0].Start, viewModel.Timelines[0].Stop);

			return View(viewModel);
		}


		public ActionResult DataToXml()
		{
			SaveEnergyProcessDataToXml("c:\\temp\\ChartDataEpvi.xml", new DateTime(2014, 1, 1), new DateTime(2014, 1, 2));

			return null;
		}

		// Ajax: Bei Bedarf immer das letzte angezeigte Intervall merken
		// Fuer interne Praesentationszwecke, nicht im Produktivbetrieb
		// Rueckgabe false, wenn keine weiteren Aufrufe noetig, weil kein Speichern erfolgt
		[HttpPost]
		public bool SaveDisplayInterval(DateTimeOffset? start, DateTimeOffset? stop)
		{
			return ChartIntervalConfig.Instance.SaveConfigInterval(start, stop);
		}

		[NoCache]
		public ActionResult LoadInfo(ChartInfoViewModel viewModel)
		{
			if (!Request.IsAjaxRequest())
				throw new NotSupportedException();

			return PartialView("_Info", viewModel);
		}

		string GetInfoCellClass(GridColumn<DataRow> column, DataRow row)
		{
			return null;
		}

		#endregion

		#region Data

		// Mit HttpGet klappt es nicht, ein array von Objekten zu uebertragen, daher HttpPost
		[HttpPost]
		public ActionResult LoadMultiChartData(ICollection<LoadChartDataParams> chartDataParams)
		{
			ConcurrentBag<LoadChartDataResult> results = new ConcurrentBag<LoadChartDataResult>();
#if DEBUG			
			var options = new ParallelOptions { MaxDegreeOfParallelism = 1 };
#else
			var options = new ParallelOptions { MaxDegreeOfParallelism = -1 };
#endif
			Parallel.ForEach(chartDataParams, options, p =>
			{
                try
                {
                    PredictiveProcessData cont = GetProcessData(p.ChartFunctionId);
                    if (p.ChartFunctionId > 0 && cont == null)
                        throw new Exception("no such graph identified");
                    List<ChartDataDto> chartData = null;

                    //draw differnt graphs, since LoadChartDataParams is missing in propertie like 'Tag' its hard to bind additional information....
                    //detect Avg Function
                    if (p.ChartFunctionId == -2)
                        chartData = DataContainers.ProminentEdges;
                    else if (p.ChartFunctionId == -1)
                        chartData = DataContainers.NormalizedData;
                    else if (p.ChartFunctionId == -3)
                        chartData = DataContainers.LowerBound;
                    else if (p.ChartFunctionId == -4)
                        chartData = DataContainers.UpperBound;

                    if (cont != null)
                    { 
                    if (cont.ViewOptions.Remove(SwhkViewOptions.Original))
                        chartData = cont.ChartData;
                    else if (cont.ViewOptions.Remove(SwhkViewOptions.Envelope))
                        chartData = cont.Envelope;
                    else if (cont.ViewOptions.Remove(SwhkViewOptions.Normalized))
                        chartData = cont.NormalizedData;
                    else if (cont.ViewOptions.Remove(SwhkViewOptions.LowerBound))
                        chartData = cont.LowerBound;
                    else if (cont.ViewOptions.Remove(SwhkViewOptions.UpperBound))
                        chartData = cont.UpperBound;
                    }

                    if (chartData != null)
                    {
                        results.Add(new LoadChartDataResult
                        {
                            CorrelationId = p.CorrelationId,
                            Data = chartData,
                            InteractionData = new List<ChartInteractionDataDto>()
                        });
                    }
                }
                catch (Exception e)
                {
                    e.Message.ToString();
                }
			});
			return Json(results.ToArray(), JsonRequestBehavior.AllowGet);
		}
		#endregion

		#region Search

		public ActionResult LoadProcessDataParams(int processid)
		{
            PredictiveProcessData data = GetProcessData(processid);
			return Json(data.Params, JsonRequestBehavior.AllowGet);
		}

		[HttpPost]
		public ActionResult Search(ChartSearchFilterDto filter)
		{
			return Json(null, JsonRequestBehavior.AllowGet);
		}

		#endregion

		#region XML

		public static void SaveEnergyProcessDataToXml(string filename, DateTime utcFrom, DateTime utcTo)
		{
			using (EPVIDbContext db = new EPVIDbContext())
			{
				//Prozessdaten
				List<ProcessData> processDatas = db.ProcessDatas
					.Where(cur => cur.ProcessDataSpecId == 1 && cur.UtcTo >= utcFrom && cur.UtcTo < utcTo)
					.Include(cur => cur.Params)
					.ToList();

				//Energiedaten: Minutenwerte
				List<Tuple<DateTime, decimal>> energyData = new List<Tuple<DateTime, decimal>>();
				int countMinutes = (int)utcTo.Subtract(utcFrom).TotalMinutes;
				using (IDbCommand cmd = db.Database.Connection.CreateCommand())
				{
					cmd.CommandText = String.Format(
						"SELECT '{0}' + INTERVAL idx MINUTE AS UtcDt, EnergyMgr_GetEnergyConsumptionGaugeValue({1}, '{0}' + INTERVAL idx MINUTE) AS Val " +
						"FROM QueryLoop " +
						"WHERE Idx <= {2}",
						utcFrom.ToString("dddd-MM-yy HH:mm"),
						31);
					
					using (IDataReader reader = cmd.ExecuteReader())
						energyData.Add(new Tuple<DateTime, decimal>(reader.GetDateTime(0), reader.GetDecimal(1)));
				}
				
				//Als XML
				XElement eltData = new XElement("Data");	
				foreach (ProcessData processData in processDatas)
				{
					//Processdatensatz
					XElement eltProcessData = new XElement("ProcessData",
						new XAttribute("Id", processData.ProcessDataSpecId),
						new XAttribute("Name", processData.Name),
						new XAttribute("UtcFrom", processData.UtcFrom),
						new XAttribute("UtcTo", processData.UtcTo));
					eltData.Add(eltProcessData);
					
					//Parameter
					XElement eltParams = new XElement("Params");
					eltProcessData.Add(eltParams);
					foreach (ProcessDataParam processDataParam in processData.Params)
					{
                        Tuple<object, string> er = PredictiveProcessData.getValueAndType(processDataParam);

						eltParams.Add(new XElement("Param", 
							new XAttribute("Name", processDataParam.Name),
							new XAttribute("Value", er.Item1),
                            new XAttribute("DataType", er.Item2)));
					}

					//Energiedaten
					XElement eltEnergy = new XElement("EnergyData");
					eltProcessData.Add(eltEnergy);
					Tuple<DateTime, decimal> energyLast = null;
					foreach (Tuple<DateTime, decimal> energy in energyData.Where(cur => cur.Item1 >= processData.UtcFrom && cur.Item1 < processData.UtcTo))
					{
						if (energyLast != null)
						{
							decimal val = energy.Item2 - energyLast.Item2;

							eltParams.Add(new XElement("Val",
								new XAttribute("UtcFrom", energy.Item1),
								new XAttribute("UtcTo", energy.Item1.AddMinutes(1)),
								new XAttribute("Value", val)));
						}

						energyLast = energy;
					}
				}				
				
				eltData.Save(filename);
			}
		}

		#endregion
	}
}
