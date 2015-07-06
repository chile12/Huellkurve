using CCCSoftware.Produktion.EPVI.Charts.Services.Data;
using CCCSoftware.Produktion.EPVI.Data.Entities;
using EPVIWeb.Areas.Charts.Controllers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Xml.Linq;

namespace EPVIWeb.PredictiveFunctions
{
    /// <summary>
    /// enum specifies all possible graph views for PredictiveProcessData(Collections)
    /// </summary>
    public enum SwhkViewOptions
    {
        Original = 0,
        Envelope = 1,
        Normalized = 2,
        UpperBound = 4,
        LowerBound = 8,
        AverageGraph = 16,
        AvgEnvelope = 32,
        LowerAverage = 64,
        UpperAverage = 128
    }

    /// <summary>
    /// extends ProcessData to store EnergyData and Properties calculating differnt (predictive) views of this data
    /// </summary>
    public class PredictiveProcessData : ProcessData, ICloneable
    {
        private static TimeSpan normalizationWindow { get; set; }       //used to define a sliding window over which to do a median normalization
        private List<EnergyLevel> energyData;                           //the EnergyData of a process
        private List<ChartDataDto> chartData;                           //raw EnergyData as ChartDataDto
        private List<ChartDataDto> normalizedData;                      //normalized data from chartData
        private List<GraphEdge> chartEdges;                             //represents the change in slope of the original graph at copared between levels before and after the current point in time
        private List<ChartDataDto> envelope;                            //this is the 'Sollwerthüllkurve'
        public List<SwhkViewOptions> ViewOptions {get; private set;}    //list of current ViewOptions
        List<ChartDataDto> prominentEdges;                              //calculates edges in slope used for the Envelope (aka 'Stützpunkte')
        public int EnvelopeLinks { get; set; }                          //mumber of links to build an Envelope with (if given)
        public decimal EnvelopePrecision { get; set; }                  //given precentage of accuracy of an Envelope to the normalized data

        public PredictiveProcessData() { normalizationWindow = new TimeSpan(0, 5, 0); ViewOptions = new List<SwhkViewOptions>(); }

        public PredictiveProcessData(int links, decimal precision) : this()
        {
            this.EnvelopeLinks = links;
            this.EnvelopePrecision = precision;
        }

        public PredictiveProcessData(int links, decimal precision, List<EnergyLevel> energyData) : this()
        {
            this.EnvelopeLinks = links;
            this.EnvelopePrecision = precision;
            this.energyData = energyData;
        }

        /// <summary>
        /// the source data of a particular graph
        /// </summary>
        public virtual List<EnergyLevel> EnergyData
        {
            get
            {
                if (energyData == null)
                    energyData = new List<EnergyLevel>();
                return energyData;
            }
        }

        /// <summary>
        /// EnergyData as ChartDataDto
        /// </summary>
        public virtual List<ChartDataDto> ChartData
        {
            get
            {
                if (this.chartData == null)
                    this.chartData = getChartData(DateTime.Now - new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second)).ToList();
                return this.chartData;
            }
        }

        /// <summary>
        /// median normalized View
        /// </summary>
        public virtual List<ChartDataDto> NormalizedData
        {
            get
            {
                if (this.normalizedData == null && EnergyData.Count > 0)
                {
                    int dev = (EnergyData[0].Time.Minutes * 60) + EnergyData[0].Time.Seconds;
                    if (dev < 1)
                        dev = 1;
                    int window = ((normalizationWindow.Minutes*60) + normalizationWindow.Seconds) / dev;
                    this.normalizedData = normalizeData(window, DateTime.Now - new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second)).ToList();
                }
                    return this.normalizedData;
            }
        }

        /// <summary>
        /// represents the change in slope of the original graph compared between levels before and after the current point in time + (other considerations)
        /// </summary>
        public virtual List<GraphEdge> ChartEdges
        {
            get
            {
                if (this.chartEdges == null)
                    this.chartEdges = getChartEdges(NormalizedData).ToList();
                return this.chartEdges;
            }
        }

        /// <summary>
        /// 'Stützstellen'
        /// </summary>
        public virtual List<ChartDataDto> ProminentEdges
        {
            get
            {
                if (prominentEdges == null)
                    prominentEdges = akkumulateEdges(ChartEdges, NormalizedData, EnvelopeLinks, EnvelopePrecision).Where(x => x != null).OrderBy(x => x.From).ToList();
                return prominentEdges;
            }
        }

        /// <summary>
        /// 'Sollwerthüllkurve' = ProminentEdges + interpolated points
        /// </summary>
        public virtual List<ChartDataDto> Envelope
        {
            get
            {
                if (envelope == null)
                    envelope = interpolateMissingValues(ProminentEdges, ChartEdges.Select(x => x.Dto)).ToList();
                return envelope;
            }
        }

        /// <summary>
        /// UpperBound = foreach(var z in Envelope) => z = z + Math.Max(ChartData(i) - Envelope(i))
        /// </summary>
        public virtual List<ChartDataDto> UpperBound
        {
            get
            {
                decimal add = getBoundDiff(Envelope, new List<IEnumerable<ChartDataDto>>() { NormalizedData }, 100);
                List<ChartDataDto> output = ProminentEdges.Select(item => cloneChartDataDto(item)).ToList();
                output.ForEach(x => x.Value = x.Value + add);
                return output;
            }
        }

        /// <summary>
        /// UpperBound = foreach(var z in Envelope) => z = z - Math.Min(ChartData(i) - Envelope(i))
        /// </summary>
        public virtual List<ChartDataDto> LowerBound
        {
            get
            {
                decimal add = getBoundDiff(Envelope, new List<IEnumerable<ChartDataDto>>() { NormalizedData }, 0);
                List<ChartDataDto> output = ProminentEdges.Select(item => cloneChartDataDto(item)).ToList();
                output.ForEach(x => x.Value = x.Value + add);
                return output;
            }
        }

        /// <summary>
        /// used to add new points afer creation
        /// </summary>
        /// <param name="start">beginning of Energy point</param>
        /// <param name="val">value</param>
        public void AddEnergyPoint(DateTime start, decimal val)
        {
            if (EnergyData.Count > 0 && EnergyData[EnergyData.Count - 1].From > start)
                throw new ArgumentException("new EnergyPoint has to occur later then the last entry");
            EnergyLevel level = new EnergyLevel();
            level.From = start;
            level.Value = val;
            EnergyData.Add(level);

            if (EnergyData.Count > 1)
                EnergyData[EnergyData.Count - 2].Time = start - EnergyData[EnergyData.Count - 2].From;
        }

        /// <summary>
        /// used to convert List<GraphEdge> to List<ChartDataDta> (used for Debugging)
        /// </summary>
        /// <param name="edges"></param>
        /// <returns></returns>
        public IEnumerable<ChartDataDto> ConvertEdgesToDataDtos(IEnumerable<GraphEdge> edges)
        {
            foreach (GraphEdge edge in edges)
            {
                ChartDataDto dto = null;
                dto = new ChartDataDto()
                {
                    From = edge.Dto.From,
                    To = edge.Dto.To,
                    Value = edge.GetEdgeMeassure()
                };
                yield return dto;
            }
        }

        /// <summary>
        /// calculates ProminentEdges (and therefore the Envelope)
        /// </summary>
        /// <param name="edges">precalculatet changes in slope</param>
        /// <param name="baseGraph">the graph pertaining to edges</param>
        /// <param name="links">number of links('Glieder') we want our Envelope to have</param>
        /// <param name="precision">level of precision (compared to normalized View - [0,1]!)</param>
        /// <returns></returns>
        public static IEnumerable<ChartDataDto> akkumulateEdges(IEnumerable<GraphEdge> edges, IEnumerable<ChartDataDto> baseGraph, int links, decimal precision)
        {
            if (edges.Count() == 0)
                return null;
            //result view (without interpolated points)
            IEnumerable<ChartDataDto> minimalView = new List<ChartDataDto>() { edges.ElementAt(0).Dto, edges.ElementAt(edges.Count() - 1).Dto };
            //for the calculation we need also the same view als minimalview with interpolated points
            IEnumerable<ChartDataDto> currentView = interpolateMissingValues(minimalView, edges.Select(x => x.Dto));

            //while we do not yet fullfill the given requirements...
            while (minimalView.Count() <= links && !compareGraphDistance(baseGraph, currentView, precision))  //not!
            {
                var sec = new List<GraphEdge>();
                decimal maxDist = -1;
                //get the edges with the highest absolute values
                var tt = edges.Where(y => y.GetAbsEdgeMeassure() > 0).OrderByDescending(x => (getMinDistance(minimalView, x.Dto)));
                if (tt.Count() > 0)
                    maxDist = getMinDistance(minimalView, tt.First().Dto);
                try
                {
                    for (int i = 0; i < currentView.Count(); i++)
                    {
                        //get current edge
                        GraphEdge e = edges.ElementAt(i);
                        //calculate additional weight from distance to already added values
                        decimal weight = (maxDist <= 0 ? 1 : (getMinDistance(minimalView, currentView.ElementAt(i)) / maxDist));
                        weight = weight + (0.5m * (1 - weight));
                        decimal addVal = (e.GetAbsEdgeMeassure() + Math.Abs(e.Dto.Value - currentView.ElementAt(i).Value)) * weight;
                        //add weight to edge value
                        sec.Add(new GraphEdge(0, addVal, e.Dto));
                    }
                }
                catch (Exception e)
                {
                    e.StackTrace.ToString();
                }
                //get edgeswithe hight slope changes from new edge values
                var zw = getSharpEdges(sec).Except(minimalView);
                //add the the edge with the highest value to result
                minimalView = minimalView.Union(zw.Take(1));
                //recalculate current view
                currentView = interpolateMissingValues(minimalView, sec.Select(x => x.Dto));
            }
            return minimalView;
        }

        /// <summary>
        /// calculates the min distance between a ChartDataDto and a given List of Dtos
        /// </summary>
        /// <param name="currentView">the List of Dtos</param>
        /// <param name="edge">the single point</param>
        /// <returns>returns time in Ticks</returns>
        public static long getMinDistance(IEnumerable<ChartDataDto> currentView, ChartDataDto edge)
        {
            long ret = long.MaxValue;
            foreach (ChartDataDto dto in currentView)
            {
                if (ret > Math.Abs(dto.From.Ticks - edge.From.Ticks))
                    ret = Math.Abs(dto.From.Ticks - edge.From.Ticks);
            }
            return ret;
        }

        /// <summary>
        /// checks if 'distance' of two graphs is lass than a given precentage value
        /// </summary>
        /// <param name="real">the reference graph</param>
        /// <param name="approx">the approximated graph</param>
        /// <param name="percent">the precentage</param>
        /// <returns></returns>
        public static bool compareGraphDistance(IEnumerable<ChartDataDto> real, IEnumerable<ChartDataDto> approx, decimal percent)
        {
            if(real.Count() != approx.Count())
                return false;
            decimal diff = 0m;
            decimal realAbs = 0m;
            for (int i = 0; i < real.Count(); i++)
            {
                diff += Math.Abs(Math.Abs(real.ElementAt(i).Value) - Math.Abs(approx.ElementAt(i).Value));
                realAbs += Math.Abs(real.ElementAt(i).Value);
            }
            diff = diff / real.Count();
            realAbs = realAbs / real.Count();
            if (diff <= realAbs * percent * 0.01m)
                return true;
            else
                return false;
        }

        /// <summary>
        /// returns in decending order all (local) maxima of a edge-function
        /// </summary>
        /// <param name="edges">edge function</param>
        /// <returns>list of (local) maxima</returns>
        public static IEnumerable<ChartDataDto> getSharpEdges(IEnumerable<GraphEdge> edges)
        {
            List<GraphEdge> res = new List<GraphEdge>();
            decimal max = 0;
            //get all maxima -> element i > i-1 && i > i+1
            for (int i = 1; i < edges.Count() - 1; i++)
            {
                decimal absLevel = edges.ElementAt(i).GetAbsEdgeMeassure();

                if (absLevel >= edges.ElementAt(i - 1).GetAbsEdgeMeassure() && absLevel >= edges.ElementAt(i + 1).GetAbsEdgeMeassure())
                {
                    res.Add(edges.ElementAt(i));
                    if (edges.ElementAt(i).GetAbsEdgeMeassure() > max)
                        max = absLevel;
                }
            }
            IEnumerable<ChartDataDto> output = res
                .OrderByDescending(x => x.GetAbsEdgeMeassure())
                .Select(x => x.Dto);

                return output.Union(new List<ChartDataDto> { edges.First().Dto, edges.Last().Dto });
        }

        /// <summary>
        /// adds interpolated values to a value-list according to the original function
        /// </summary>
        /// <param name="input">partial valuelist, to interpolate over</param>
        /// <param name="original">the original function we interpolate to</param>
        /// <returns>returns interpolated function</returns>
        public static IEnumerable<ChartDataDto> interpolateMissingValues(IEnumerable<ChartDataDto> input, IEnumerable<ChartDataDto> original)
        {
            int skip = 1;
            input = input.OrderBy(x => x.From);
            List<ChartDataDto> additionalPoint = new List<ChartDataDto>() { input.ElementAt(0) };
            for (int i = 0; i < input.Count() - 1; i++)
            {
                ChartDataDto target = input.ElementAt(i + 1);
                decimal valueDiff = target.Value - input.ElementAt(i).Value;
                IEnumerable<ChartDataDto> subRange = original.Skip(skip).TakeWhile(x => x.From < target.From);
                skip = skip + subRange.Count() +1;
                decimal additionalValue = valueDiff / (subRange.Count() + 1);

                for (int j = subRange.Count() - 1; j >= 0; j--)
                {
                    ChartDataDto dto = null;
                    dto = new ChartDataDto()
                    {
                        From = subRange.ElementAt(j).From,
                        To = subRange.ElementAt(j).To,
                        Value = target.Value - (subRange.Count() - j) * additionalValue
                    };
                    additionalPoint.Add(dto);
                }
                additionalPoint.Add(target);
            }
            return additionalPoint.OrderBy(x => x.From);
        }

        /// <summary>
        /// calculates the difference between multiple (upper-) lowerbound functions and base function
        /// </summary>
        /// <param name="baseGraph">the base function</param>
        /// <param name="graphList">list of comparing graphs</param>
        /// <param name="precision">defines the precision of this calculation</param>
        /// <returns></returns>
        public static decimal getBoundDiff(IEnumerable<ChartDataDto> baseGraph, IEnumerable<IEnumerable<ChartDataDto>> graphList, int precision)
        {
            if (precision < 0 || precision > 100)
                throw new ArgumentOutOfRangeException("precision has to be between 0 and 100");
            foreach (var g in graphList)
                if (g.Count() > baseGraph.Count())
                    throw new ArgumentException("baseGraph should at least have as many data points, as comparing graphs");
            List<decimal> diffList = new List<decimal>();
            for(int i =0; i < baseGraph.Count(); i++)
            {
                ChartDataDto bgdto = baseGraph.ElementAt(i);
                foreach (var g in graphList)
                    if (i < g.Count())
                        diffList.Add(g.ElementAt(i).Value - bgdto.Value);
            }
            diffList = diffList.OrderBy(x => x).ToList();
            return diffList.ElementAt((int)((decimal)(diffList.Count()-1) / 100m * precision));
        }

        /// <summary>
        /// returns the slope levels for a given function
        /// </summary>
        /// <param name="graph"></param>
        /// <returns></returns>
        public static IEnumerable<GraphEdge> getChartEdges(IEnumerable<ChartDataDto> graph)
        {
            if (graph.Count() > 2)
            {
                for (int i = 0; i < graph.Count(); i++)
                {
                    yield return new GraphEdge(i, graph);
                }
            }
        }

        /// <summary>
        /// calculates a List<ChartDataDto> from the EnergyData
        /// </summary>
        /// <param name="start">defines a point in time, from where a given function is </param>
        /// <returns></returns>
        private IEnumerable<ChartDataDto> getChartData(DateTime start)
        {
            for (int i = 0; i < EnergyData.Count; i++)
            {
                ChartDataDto dto = null;
                DateTimeOffset from = start + (EnergyData[i].From - EnergyData[0].From);
                dto = new ChartDataDto()
                {
                    From = from,
                    To = from.Add(EnergyData[i].Time),
                    Value = EnergyData[i].Value
                };
                yield return dto;
            }
        }

        /// <summary>
        /// median normalizes the List<ChartData> graph
        /// </summary>
        /// <param name="normalizatonWindowSize">defines the max size of sliding window used for normalizing</param>
        /// <param name="start">defines the point in time the resulting function will start</param>
        /// <returns>normalized function</returns>
        private IEnumerable<ChartDataDto> normalizeData(int normalizatonWindowSize, DateTime start)
        {
            List<decimal> zw = EnergyData.Select(x => x.Value).ToList();
            for (int i = 0; i < EnergyData.Count; i++)
            {
                ChartDataDto dto = null;
                DateTimeOffset from = start + (EnergyData[i].From - EnergyData[0].From);
                dto = new ChartDataDto()
                {
                    From = from,
                    To = from.Add(EnergyData[i].Time),
                    Value = medianNormalize(zw, normalizatonWindowSize, i)
                };
                yield return dto;
            }
        }

        /// <summary>
        /// median normalizes a List of decimals
        /// </summary>
        /// <param name="list">the decimal list</param>
        /// <param name="normalizatonWindowSize">size of the sliding window</param>
        /// <param name="pos">sliding window center position</param>
        /// <returns></returns>
        private decimal medianNormalize(List<decimal> list, int normalizatonWindowSize, int pos)
        {
            if (pos < 0 || pos > list.Count)
                throw new IndexOutOfRangeException("position is out of range");

            int length = Math.Min(list.Count - pos, normalizatonWindowSize);
            int startpos = Math.Max(0, pos - length/2);

            return GetMedian(list.GetRange(startpos, length));
        }

        /// <summary>
        /// as named
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        public static ChartDataDto cloneChartDataDto(ChartDataDto dto)
        {
            ChartDataDto d = new ChartDataDto();
            d.Value = dto.Value;
            d.To = dto.To;
            d.Text = dto.Text;
            d.From = dto.From;
            d.DataIntervalId = dto.DataIntervalId;
            return d;
        }

        public object Clone()
        {
            return new PredictiveProcessData(this.EnvelopeLinks, this.EnvelopePrecision, this.energyData);
        }

        /// <summary>
        /// produces the median of a given list of decimals
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static decimal GetMedian(IEnumerable<decimal> source)
        {
            decimal[] temp = source.ToArray();
            Array.Sort(temp);

            int count = temp.Length;
            if (count == 0)
            {
                throw new InvalidOperationException("Empty collection");
            }
            else if (count % 2 == 0)
            {
                decimal a = temp[count / 2 - 1];
                decimal b = temp[count / 2];
                return (a + b) / 2m;
            }
            else
            {
                return temp[count / 2];
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="processDataParam"></param>
        /// <returns></returns>
        public static Tuple<object, string> getValueAndType(ProcessDataParam processDataParam)
        {
            if (processDataParam == null)
                return new Tuple<object, string>("null", "null"); ;
            object val = processDataParam.StrValue;
            string dataType = "string";
            if (processDataParam.NumValue.HasValue)
            {
                val = processDataParam.NumValue.Value;
                dataType = "decimal";
            }
            else if (processDataParam.DateValue.HasValue)
            {
                val = processDataParam.DateValue.Value;
                dataType = "date";
            }
            return new Tuple<object, string>(val, dataType);
        }

        /// <summary>
        /// ~
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static List<XElement> LoadEnergyProcessDataFromXml(XElement data)
        {
            List<XElement> result = new List<XElement>();
            if (data.Name == "ProcessData")
            {
                var zw = new XElement("Data");
                zw.AddFirst(data);
                data = zw;
            }
            try
            {
                foreach (XElement pd in data.Elements("ProcessData"))
                {
                    result.Add(pd);
                }
            }
            catch (Exception e)
            {
                e.Message.ToString();
            }
            return result;
        }

        public static List<XElement> LoadEnergyProcessDataFromXml(FileStream xml)
        {
            XElement data = XElement.Load(new StreamReader(xml));
            return LoadEnergyProcessDataFromXml(data);
        }

        public static List<XElement> LoadEnergyProcessDataFromXml(String xml)
        {
            XElement data = XElement.Load(new StringReader(xml));
            return LoadEnergyProcessDataFromXml(data);
        }

        public static PredictiveProcessData ParseProcessData(XElement pd, int links, decimal precision)
        {
            PredictiveProcessData res = new PredictiveProcessData(links, precision);
            res.Name = pd.Attribute("Name").Value;
            res.ProcessDataId = pd.Attribute("Id").Value;
            res.UtcFrom = DateTime.Parse(pd.Attribute("UtcFrom").Value);
            res.UtcTo = DateTime.Parse(pd.Attribute("UtcTo").Value);

            foreach (XElement parm in pd.Element("Params").Elements("Param"))
            {
                ProcessDataParam param = new ProcessDataParam();
                String type = parm.Attribute("DataType").Value;
                param.Name = parm.Attribute("Name").Value;
                if (type == "date")
                    param.DateValue = DateTime.Parse(parm.Attribute("Value").Value);
                else if (type == "decimal")
                {
                    string val = parm.Attribute("Value").Value;
                    if (val == "")
                        val = "0.0";
                    param.NumValue = decimal.Parse(val.Replace(".", ","));
                }
                else
                    param.StrValue = parm.Attribute("Value").Value;

                res.Params.Add(param);
            }

            foreach (XElement parm in pd.Element("EnergyData").Elements("Val"))
            {
                res.AddEnergyPoint(DateTime.Parse(parm.Attribute("UtcFrom").Value), Decimal.Parse(parm.Attribute("Value").Value));
            }
            return res;
        }
    }

    /// <summary>
    /// Container representing a span of time to which a value is attributed
    /// </summary>
    public class EnergyLevel
    {
        public DateTime From { get; set; }
        public decimal Value { get; set; }
        public TimeSpan Time { get; set; }
    }

    /// <summary>
    /// describes the level of difference (in value) between the values before and after a certain point in time (similar to the slope)
    /// </summary>
    public class GraphEdge
    {
        private static int varianceWindowSize = 3;                  //window of values to the left or right of an x-value
        private List<decimal> leftLevels = new List<decimal>();     //collection of y-values to the left
        private decimal leftVariance = -1;                  
        private List<decimal> reightLevels = new List<decimal>();   //collection of y-values to the right
        private decimal rightVariance = -1;
        public ChartDataDto Dto { get; set; }                       //pertaining point of data

        public GraphEdge(decimal rightVariance, decimal leftVariance, ChartDataDto d)
        {
            this.rightVariance = rightVariance;
            this.leftVariance = leftVariance;
            this.Dto = d;
        }

        public GraphEdge(int pos, IEnumerable<ChartDataDto> graph)
        {
            if (graph.Count() <= 3)
                throw new ArgumentException("graph is too short!");
            this.Dto = graph.ElementAt(pos);
            leftLevels.AddRange(graph.Select(x => x.Value).ToList().GetRange(Math.Max(0, pos - varianceWindowSize), varianceWindowSize));
            reightLevels.AddRange(graph.Select(x => x.Value).ToList().GetRange(pos + 1, Math.Min(varianceWindowSize, graph.Count() - pos - 1)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>the max value difference (variance) in the window to the left of a pertaining point in  time</returns>
        public decimal GetLeftVariance()
        {
            if (leftVariance == null || leftLevels.Count > 0 && leftVariance < 0)
                leftVariance = leftLevels.Max() - leftLevels.Min();
            return leftVariance;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>the max value difference (variance) in the window to the right of a pertaining point in time</returns>
        public decimal GetRightVariance()
        {
            if (rightVariance == null || reightLevels.Count > 0 && rightVariance < 0)
                rightVariance = reightLevels.Max() - reightLevels.Min();
            return rightVariance;
        }

        /// <summary>
        /// returns the difference between the max variance of left and right window
        /// </summary>
        /// <returns></returns>
        public decimal GetEdgeMeassure()
        {
            return Math.Abs(GetLeftVariance()) - Math.Abs(GetRightVariance());
        }

        public decimal GetAbsEdgeMeassure()
        {
            return Math.Abs(GetEdgeMeassure());
        }
    }
}