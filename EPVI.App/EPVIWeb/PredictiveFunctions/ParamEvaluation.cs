using CCCSoftware.Produktion.EPVI.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EPVIWeb.PredictiveFunctions
{
    /// <summary>
    /// defines a parameter as:
    /// </summary>
    public enum ParamType
    {
        Enum,       //parameter is a limited amount of values 
        Decimal,    //param has a nondenumerable amount of values
        Countable,  //values of param are countable (Integer)
        Trivial     //param has less than 2 values or is deemed unimportant
    }

    /// <summary>
    /// This class is used to evaluate a specific parameter (mainly by its numbers of different values)
    /// </summary>
    public class ParamEvaluation
    {
        //please change this´(make it dependend on sample size) - more than this is no enum
        public static int EnumThreashold = 20;

        public string ParamName { get; private set; }
        private IEnumerable<ProcessDataParam> paramss;
        private IEnumerable<Tuple<object, string>> distinctValues;
        /// <summary>
        /// number of distinct values
        /// </summary>
        public int ValueCount
        {
            get
            {
                return distinctValues.Count();
            }
        }
        /// <summary>
        /// defines the enumerability of a parameter
        /// 
        /// </summary>
        public ParamType ParamType { 
            //please review this - here a very basic determination of the enumerability
            get
            {
                if (ValueCount <= 1 || Ignore)
                    return ParamType.Trivial;
                if (ValueCount <= EnumThreashold)
                    return ParamType.Enum;
                if (distinctValues.ElementAt(0).Item2.Trim().ToLower() == "decimal")
                    return ParamType.Decimal;
                return PredictiveFunctions.ParamType.Enum;
            }
        }

        public List<Tuple<object, string>> DistinctValues
        {
            get
            {
                return distinctValues.ToList();
            }
        }

        public bool Ignore { get; set; }

        /// <summary>
        /// Constructor...
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="type"></param>
        /// <param name="values"></param>
        /// <param name="ignore"></param>
        public ParamEvaluation(string Name, string type, IEnumerable<string> values, int ignore)
        {
            this.ParamName = Name;
            this.distinctValues = new List<Tuple<object, string>>();
            Type t = type.GetType(); //string
            if (type == "Decimal")
                t = Type.GetType("System.Decimal");
            foreach (string val in values)
            {
                (distinctValues as List<Tuple<object, string>>).Add(new Tuple<object, string>(Convert.ChangeType(val, t), type));
            }
            this.Ignore = ignore == 1 ? true : false;
        }

        /// <summary>
        /// Constructor...
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="paramss"></param>
        public ParamEvaluation(string Name, IEnumerable<ProcessDataParam> paramss)
        {
            ParamName = Name;
            this.paramss = paramss.Where(x=> x.Name == Name);
            this.distinctValues = this.paramss.Select(x => PredictiveProcessData.getValueAndType(x)).Distinct();
            Ignore = false;
        }

        public override string ToString()
        {
            return ParamName + " - " + ParamType.ToString() + " - " + ValueCount;
        }
    }
}