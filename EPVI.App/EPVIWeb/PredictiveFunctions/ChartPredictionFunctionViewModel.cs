using EPVIWeb.Areas.Charts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EPVIWeb.PredictiveFunctions
{
    /// <summary>
    /// extends ChartFunctionViewModel with predictive process data
    /// </summary>
    public class ChartPredictiveFunctionViewModel : ChartFunctionViewModel
    {
        public PredictiveProcessData PredictiveProcessData { get; set; }
    }
}