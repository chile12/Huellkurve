using CCCSoftware.Produktion.EPVI.Charts.Services.Data;
using CCCSoftware.Produktion.EPVI.Data.Entities;
using EPVIWeb.Areas.Charts.Controllers;
using Microsoft.Ajax.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EPVIWeb.PredictiveFunctions
{
    /// <summary>
    /// This Collection holds multiple PredictiveProcessData, providing parameter evaluation methods
    /// </summary>
    public class PredictiveProcessCollection : PredictiveProcessData, IEnumerable<PredictiveProcessData>
    {
        private List<PredictiveProcessData> data = new List<PredictiveProcessData>();   //the basic data
        private Dictionary<string, ParamEvaluation> paramevals;                         //Map processid -> Evaluation results of o process
        private List<ChartDataDto> avgCharData;                                         //´the avg graph from all stored PredictiveProcessData
        private List<IEnumerable<ChartDataDto>> strechedChartData;                      //result of stretching/compressing all graphs to fit a certain number of data points
        private List<IEnumerable<ChartDataDto>> strechedNormalizedData;                 //normalization of strechedChartData
        private List<ChartDataDto> normalizedData;                                      //the normalization of the basic data 
        public string Name { get; private set; }
        public object Tag { get; set; }


        public PredictiveProcessCollection(IEnumerable<PredictiveProcessData> data, string Name = null)
        {
            this.Name = Name;
            foreach (var pd in data)
            {
                this.Add(pd);
            }
        }

        private PredictiveProcessCollection(IEnumerable<PredictiveProcessData> data, string Name = null, Dictionary<string, ParamEvaluation> paramevals = null, object tag = null)
        {
            this.Name = Name;
            this.paramevals = paramevals;
            this.Tag = tag;
            foreach (var pd in data)
            {
                this.Add(pd);
            }
        }

        /// <summary>
        /// gets basic data from a database
        /// </summary>
        /// <param name="processids">collecton of processid of processes to load from the DB</param>
        /// <param name="precision">level of precision (compared to normalized View - [0,1]!)</param>
        /// <param name="links">number of links('Glieder') we want our Envelope to have</param>
        /// <param name="Name">~</param>
        public PredictiveProcessCollection(int[] processids, decimal precision = 0m, int links = 5, string Name = null)
        {
            for (int i = 0; i < processids.Length; i = i + 100)
            {
                foreach (var data in WSConnection.GetProcessFromWS(processids.Skip(i).Take(100).ToArray(), precision, links))
                {
                    this.Add(data);
                }
            }
            this.Name = Name;
        }

        /// <summary>
        /// reset collection
        /// </summary>
        public void Clear()
        {
            this.normalizedData = null;
            this.avgCharData = null;
            strechedNormalizedData = null;
            strechedChartData = null;
            this.data.Clear();
        }

        #region PredictiveProcessData overrides

        /// <summary>
        /// is empty for a collection of processes
        /// </summary>
        public override List<EnergyLevel> EnergyData
        {
            get
            {
                return new List<EnergyLevel>();
            }
        }

        public override List<ChartDataDto> ProminentEdges
        {
            get
            {
                return PredictiveProcessData.akkumulateEdges(ChartEdges, NormalizedData, 100, 3).Where(x => x != null).OrderBy(x => x.From).ToList();

            }
        }

        public override List<GraphEdge> ChartEdges
        {
            get
            {
                return getChartEdges(NormalizedData).ToList();
            }
        }

        public override List<ChartDataDto> ChartData
        {
            get
            {
                if (strechedChartData == null)
                    stretchChartData();
                decimal avgLevel = getAvgStartLevel();

                if (this.avgCharData == null)
                {
                    avgCharData = new List<ChartDataDto>();
                    for (int i = 0; i < strechedChartData.ElementAt(0).Count(); i++)
                    {
                        ChartDataDto dt = PredictiveProcessData.cloneChartDataDto(strechedChartData[0].ElementAt(i));
                        dt.Value = strechedChartData.Select(x => x.ElementAt(i)).Average(x => x.Value);
                        avgCharData.Add(dt);
                    }
                }
                return this.avgCharData;
            }
        }


        public override List<ChartDataDto> NormalizedData
        {
            get
            {
                if (strechedNormalizedData == null)
                    stretchNormalizedData();
                decimal avgLevel = getAvgStartLevel();
                if (this.normalizedData == null)
                {
                    normalizedData = new List<ChartDataDto>();
                    for (int i = 0; i < strechedNormalizedData.ElementAt(0).Count(); i++)
                    {
                        ChartDataDto dt = PredictiveProcessData.cloneChartDataDto(strechedNormalizedData[0].ElementAt(i));
                        var test = strechedNormalizedData.Select(x => x.ElementAt(i));
                        dt.Value = test.Average(x => x.Value);
                        normalizedData.Add(dt);
                    }
                }
                return this.normalizedData;
            }
        }

        public override List<ChartDataDto> Envelope
        {
            get
            {
                return interpolateMissingValues(ProminentEdges, ChartEdges.Select(x => x.Dto)).ToList();
            }
        }

        public override List<ChartDataDto> UpperBound
        {
            get
            {
                decimal add = PredictiveProcessData.getBoundDiff(Envelope, strechedNormalizedData, 80);
                List<ChartDataDto> output = ProminentEdges.Select(item => cloneChartDataDto(item)).ToList();
                output.ForEach(x => x.Value = x.Value + add);
                return output;
            }
        }

        public override List<ChartDataDto> LowerBound
        {
            get
            {
                decimal add = PredictiveProcessData.getBoundDiff(Envelope, strechedNormalizedData, 20);
                List<ChartDataDto> output = ProminentEdges.Select(item => cloneChartDataDto(item)).ToList();
                output.ForEach(x => x.Value = x.Value + add);
                return output;
            }
        } 
        #endregion

        #region IEnumerable implementation
        public virtual void Add(PredictiveProcessData t)
        {
            if (t != null)
                data.Add(t);
        }

        public IEnumerator<PredictiveProcessData> GetEnumerator()
        {
            return data.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return data.GetEnumerator();
        }
        #endregion

        /// <summary>
        /// stretches/compresses every graph in this collection
        /// </summary>
        private void stretchChartData()
        {
            strechedChartData = new List<IEnumerable<ChartDataDto>>();
            int avgSize = (int)(this.Max(x => x.ChartData.Count));
            IEnumerable<ChartDataDto> target = this.Where(x => x.ChartData.Count() == this.Max(y => y.ChartData.Count)).First().ChartData;
            DateTimeOffset startDate = target.ElementAt(0).From;
            TimeSpan stepDuration = new TimeSpan((long)(((TimeSpan)(target.ElementAt(0).To - target.ElementAt(0).From)).Ticks));

            foreach (PredictiveProcessData p in this)
            {
                var zw = stretchCompress(p.ChartData, avgSize, startDate, stepDuration);
                if (zw != null && zw.Count() > 0)
                    strechedChartData.Add(zw);
            }
        }

        /// <summary>
        /// stretches/compresses the normalized graph in this collection
        /// </summary>
        private void stretchNormalizedData()
        {
            strechedNormalizedData = new List<IEnumerable<ChartDataDto>>();
            int avgSize = (int)(this.Max(x => x.EnergyData.Count));
            IEnumerable<ChartDataDto> target = this.Where(x => x.EnergyData.Count() == avgSize).First().NormalizedData;
            DateTimeOffset startDate = target.ElementAt(0).From;
            TimeSpan stepDuration = new TimeSpan((long)(((TimeSpan)(target.ElementAt(0).To - target.ElementAt(0).From)).Ticks));

            foreach (PredictiveProcessData p in this)
            {
                var zz = stretchCompress(p.NormalizedData, avgSize, startDate, stepDuration);
                if (zz != null && zz.Count() > 0)
                    strechedNormalizedData.Add(zz);
            }
        }

        /// <summary>
        /// stretches/compresses a given list of data points to a new size
        /// </summary>
        /// <param name="list">the input list</param>
        /// <param name="targetSize">the expected size of the output list</param>
        /// <param name="startDate">first element of the output list starts with this date</param>
        /// <param name="period">timespan of a single datapoint of the output list</param>
        /// <returns>list of ChartDataDto streched/compressed to the target size</returns>
        private IEnumerable<ChartDataDto> stretchCompress(IEnumerable<ChartDataDto> list, int targetSize, DateTimeOffset startDate, TimeSpan period)
        {
            if (targetSize == 0 || list == null)
                return list;
            List<ChartDataDto> ret = new List<ChartDataDto>();
            decimal partSize = (decimal)list.Count() / (decimal)targetSize;
            decimal nextPart = Math.Min(partSize, 1m);

            decimal tTrack = partSize;
            decimal orgTrack = 1m;

            int listCount = 0;
            DateTimeOffset from = startDate;
            TimeSpan time = period;
            for (int i = 0; i < targetSize; i++)
            {
                ChartDataDto targetPoint = PredictiveProcessData.cloneChartDataDto(list.ElementAt(listCount));
                targetPoint.From = from;
                targetPoint.To = from + time;
                from = (DateTimeOffset) targetPoint.To;

                decimal val = 0;
                do
                {
                    val += list.ElementAt(listCount).Value * nextPart;
                    tTrack = tTrack - nextPart;
                    orgTrack = orgTrack - nextPart;
                    if (orgTrack <= 0)
                    {
                        orgTrack = 1m;
                        listCount++;
                    }
                    if (tTrack <= 0)
                        tTrack = partSize;
                    nextPart = Math.Min(tTrack, orgTrack);
                } while (tTrack != partSize && listCount < list.Count());
                targetPoint.Value = val / partSize;
                ret.Add(targetPoint);
            }
                return ret;
        }

        /// <summary>
        /// raises (y-value) a given list of datapoint by a given level
        /// </summary>
        /// <param name="list"></param>
        /// <param name="startValue"></param>
        /// <returns></returns>
        private IEnumerable<ChartDataDto> levelList(IEnumerable<ChartDataDto> list, decimal startValue)
        {
            decimal diff = list.ElementAt(0).Value - startValue;
            list.ForEach(x => x.Value = x.Value - diff);
            return list;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>the avg y-value of all first datapoints of this collection</returns>
        public decimal getAvgStartLevel()
        {
            return (decimal)(this.Where(x => x.ChartData.Count > 0).Select(x => x.ChartData.ElementAt(0)).Average(x => x.Value));
        }

        /// <summary>
        /// Map of processid -> Collection of pertaining ProcessDataParam
        /// </summary>
        public Dictionary<string, ICollection<ProcessDataParam>> Params
        {
            get
            {
                return this.ToDictionary(x => x.ProcessDataId, x => x.Params);
            }
        }

        /// <summary>
        /// Map of processid -> Collection of ParamEvaluation of pertaining params
        /// </summary>
        public Dictionary<string, ParamEvaluation> ParamEvaluations
        {
            get
            {
                if (paramevals == null)
                {
                    paramevals = new Dictionary<string,ParamEvaluation>();
                    var zw = Params.Values.SelectMany(x => x);
                    foreach(ProcessDataParam par in zw)
                    {
                        if(!paramevals.Keys.Contains(par.Name))  //not!
                            paramevals.Add(par.Name, new ParamEvaluation(par.Name, zw));
                    }
                }
                return paramevals;
            }
            set
            {
                paramevals = value;
            }
        }

        /// <summary>
        /// Map of processid -> the pertaining Envelopes
        /// </summary>
        public Dictionary<int, List<ChartDataDto>> Envelopes
        {
            get
            {
                var ret = new Dictionary<int, List<ChartDataDto>>();
                foreach (PredictiveProcessData chart in this)
                {
                    if (chart.EnergyData.Count == 0)
                        continue;
                    ret.Add(int.Parse(chart.ProcessDataId), chart.ProminentEdges);
                }
                return ret;
            }
        }

        /// <summary>
        /// serializes a sublist of 'this' as json (used for saving in DB)
        /// </summary>
        /// <param name="skip">sublist starts at</param>
        /// <param name="take">sublist has size</param>
        /// <returns>json</returns>
        public string asJson(int skip, int take)
        {
            var zw = new List<object>();
            for (int i = skip; i < Math.Min(skip + take, this.Count()); i++)
            {
                var ent = this.Envelopes.ElementAt(i);
                foreach (var val in ent.Value)
                    zw.Add(new
                    {
                        process_id = ent.Key,
                        from = val.From,
                        to = val.To,
                        val = val.Value,
                        upper = this.Where(x => int.Parse(x.ProcessDataId) == ent.Key).First().UpperBound.Where(y => y.From == val.From).First().Value,
                        lower = this.Where(x => int.Parse(x.ProcessDataId) == ent.Key).First().LowerBound.Where(y => y.From == val.From).First().Value
                    });
            }
            var timeconverter = new IsoDateTimeConverter();
            timeconverter.DateTimeFormat = "MM.dd.yyyy hh:mm:ss";
            return JsonConvert.SerializeObject(zw, timeconverter);
        }

        /// <summary>
        /// switches a parameter to 'ignore' if it is not relevant for the evaluaion of the given processes
        /// </summary>
        /// <param name="ParamName"></param>
        public void IgnoreParam(string ParamName)
        {
            ParamEvaluation zw = null;
            ParamEvaluations.TryGetValue(ParamName, out zw);
            if (zw != null)
                zw.Ignore = true;
        }

        /// <summary>
        /// s.o.
        /// </summary>
        /// <param name="ParamName"></param>
        public void UnIgnoreParam(string ParamName)
        {
            ParamEvaluation zw = null;
            ParamEvaluations.TryGetValue(ParamName, out zw);
            if (zw != null)
                zw.Ignore = false;
        }

        /// <summary>
        /// groups all paramers by the destict values existing in 'this'
        /// </summary>
        /// <returns></returns>
        public List<PredictiveProcessCollection> GetAllParamGroups()
        {
            List<PredictiveProcessCollection> ret = new List<PredictiveProcessCollection>();
            foreach (ParamEvaluation eva in ParamEvaluations.Values)
            {
                if (eva.ParamType == ParamType.Enum)
                {
                    foreach (object val in eva.DistinctValues)
                    {
                        var zw = this.Where(x => PredictiveProcessData.getValueAndType(x.Params.SingleOrDefault(y => y.Name == eva.ParamName)).Item1.ToString() == val.ToString());
                        ret.Add(new PredictiveProcessCollection(zw, eva.ParamName + ": " + val.ToString(), paramevals, eva.ParamName));
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// Map: ParamType -> Map: ParamName -> list of distinct values of this parameter
        /// </summary>
        /// <returns></returns>
        public Dictionary<ParamType, Dictionary<string, List<Tuple<object, string>>>> GetParameters()
        {
            Dictionary<ParamType, Dictionary<string, List<Tuple<object, string>>>> ret = new Dictionary<ParamType, Dictionary<string, List<Tuple<object, string>>>>();
            var decs = new Dictionary<string, List<Tuple<object, string>>>();
            var enums = new Dictionary<string, List<Tuple<object, string>>>();
            foreach (ParamEvaluation eva in ParamEvaluations.Values)
                if (eva.ParamType == ParamType.Decimal)
                    decs.Add(eva.ParamName, eva.DistinctValues);
                else if (eva.ParamType == ParamType.Enum)
                    enums.Add(eva.ParamName, eva.DistinctValues);
            ret.Add(ParamType.Enum, enums);
            ret.Add(ParamType.Decimal, decs);
            return ret;
        }

        /// <summary>
        /// save a PredictiveProcessCollection to the DB
        /// </summary>
        /// <param name="coll"></param>
        /// <returns></returns>
        public static bool SaveEnvelopes(PredictiveProcessCollection coll)
        {
            for (int i = 0; i < coll.Count(); i = i + 10)
                if (!WSConnection.InsertPredictiveProcessCollections(coll.asJson(i, 10)))
                    return false;

            return true;
        }

    }
}