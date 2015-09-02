#r @"packages\FSharp.Data.2.2.5\lib\net40\FSharp.Data.dll"
#r @"packages\FSharp.Data.SqlClient.1.7.5\lib\net40\FSharp.Data.SqlClient.dll"
#r @"packages\MathNet.Numerics.3.7.0\lib\net40\MathNet.Numerics.dll"
#r @"packages\MathNet.Numerics.FSharp.3.7.0\lib\net40\MathNet.Numerics.FSharp.dll"

open FSharp.Data
open FSharp.Data.SqlClient // very nice intro here: http://blogs.msdn.com/b/fsharpteam/archive/2014/05/23/fsharp-data-sqlclient-seamlessly-integrating-sql-and-f-in-the-same-code-base-guest-post.aspx

[<Literal>]
let cn = @"Data Source=xxxxx;Initial Catalog=OperationsManagerDW;Integrated Security=True"

[<Literal>]
let perfQry = """
select [DateTime], AverageValue, MinValue, MaxValue, StandardDeviation, CounterName
from perf.vperfdaily pvp
    inner join vManagedEntity vme on pvp.ManagedEntityRowId = vme.ManagedEntityRowId 
    inner join vPerformanceRuleInstance vpri on pvp.PerformanceRuleInstanceRowId = vpri.PerformanceRuleInstanceRowId 
    inner join vPerformanceRule vpr on vpr.RuleRowId = vpri.RuleRowId 
  WHERE 
    Path = @path  
	and CounterName = @counter
"""
type midweb01 = SqlCommandProvider<perfQry, cn>
let cmd = new midweb01()

let reqs = 
    cmd.AsyncExecute(path = "someserver", counter="Requests/Sec")
    |> Async.RunSynchronously
    |> Array.ofSeq

(*
    for histogram binning I could use mathnet.numerics.statistics.histogram but...
    it spits the dummy when a boundary is 0.0
    and it doesn't automatically create nice intervals - R does this nicely when creating histograms by automatically
    choosing 'pretty' intervals eg https://stat.ethz.ch/R-manual/R-devel/library/base/html/pretty.html
    instead you have to think through an appropriate choice of intervals yourself
    so, just make our own binning function
*)
type Bucket = { LowerBound: float; UpperBound: float; Count: int}

let histogram data lowerBound upperBound intervals =
    seq{lowerBound .. (upperBound - lowerBound)/intervals .. upperBound }
    |> Seq.pairwise
    |> Seq.map (fun (l,u) -> 
                            let cnt = data |> Seq.filter (fun e -> e >= l && e < u) |> Seq.length
                            {LowerBound = l; UpperBound = u; Count = cnt}
                )

let avgReqs = reqs |> Array.map (fun r -> r.AverageValue)

let avgReqBins = histogram avgReqs 0.0 3.0 60.

let plotBuckets = 
    avgReqBins |> Seq.map (fun b -> 
                            (sprintf "%.3g" b.LowerBound, b.Count) 
                        )

avgReqs |> Array.filter (fun i -> i = 0.0) |> Array.length

#r "System.Windows.Forms.DataVisualization.dll"
open System
open System.Windows.Forms
open System.Windows.Forms.DataVisualization
open System.Windows.Forms.DataVisualization.Charting

type HistogramChart( bins:seq<string*int>, ?xAxisTitle, ?yAxisTitle) =
    // create my own histogram chart since we don't have a good option in FSharp.Charting's column chart
    // Load Microsoft Chart Controls for Windows Forms

   
    // Create an instance of chart control and add main area
    let xAxis =
            match xAxisTitle with
            | Some title -> title
            | None -> ""
    let yAxis =
            match yAxisTitle with
            | Some title -> title
            | None -> ""    

    do
        let chart = new Chart(Dock = DockStyle.Fill)
        let area = new ChartArea("Main")

        area.AxisX.Title <- xAxis
        area.AxisX.Title <- yAxis
        chart.ChartAreas.Add(area)

        // Show the chart control on a top-most form
        let mainForm = new Form(Visible = true, TopMost = true, 
                                Width = 700, Height = 500)
        mainForm.Controls.Add(chart)

        // Create series and add it to the chart
        let series = new Series("bins")
        series.ChartType <- SeriesChartType.Column
        series.CustomProperties <- "PointWidth=.9"

        chart.Series.Add(series)
        chart.Series

        bins
        |> Seq.iter (fun (label, value) ->
            let dp = new DataPoint()
            dp.AxisLabel <- label
            dp.YValues <- [|(float)value|]
            series.Points.Add dp
            )



HistogramChart(plotBuckets, "xaxis", "yaxis")

