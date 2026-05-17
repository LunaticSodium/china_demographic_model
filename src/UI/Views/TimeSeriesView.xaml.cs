using System.ComponentModel;
using System.Windows.Controls;
using ChinaDemographicModel.UI.ViewModels;
using ScottPlot;

namespace ChinaDemographicModel.UI.Views;

public partial class TimeSeriesView : UserControl
{
    private MainViewModel? _vm;

    public TimeSeriesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = e.NewValue as MainViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
            Redraw();
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ActiveScenario)
            or nameof(MainViewModel.CurrentYear)
            or nameof(MainViewModel.ProjectionStamp))
        {
            Dispatcher.BeginInvoke(new Action(Redraw));
        }
    }

    private void Redraw()
    {
        if (_vm == null) return;

        StylePlot(BirthsPlot.Plot, "年出生人口（万人）");
        StylePlot(DeathsPlot.Plot, "年死亡人口（万人）");
        StylePlot(MarriagePlot.Plot, "粗结婚率（‰）");

        var hist = _vm.Historical;
        if (hist != null)
        {
            // births
            DrawObs(BirthsPlot.Plot, hist.BirthsByYear, scale: 1.0 / 10000.0);
            // deaths
            DrawObs(DeathsPlot.Plot, hist.DeathsByYear, scale: 1.0 / 10000.0);
            // marriage
            DrawObs(MarriagePlot.Plot, hist.CrudeMarriageRateByYear, scale: 1.0);
        }

        var scen = _vm.ActiveScenario;
        if (scen != null && scen.InputsByYear.Count > 0)
        {
            var years = scen.InputsByYear.Keys.OrderBy(k => k).ToArray();
            var bxs = years.Select(y => (double)y).ToArray();

            // births (from inputs)
            var byVals = years.Select(y => scen.InputsByYear[y].TotalBirths / 10000.0).ToArray();
            DrawScen(BirthsPlot.Plot, bxs, byVals, scen.Name);

            // deaths (model-predicted from projection × mortality schedule)
            var dxs = new List<double>(); var dys = new List<double>();
            foreach (var y in years)
            {
                if (!scen.ProjectedByYear.TryGetValue(y, out var p)) continue;
                var inp = scen.InputsByYear[y];
                double deaths = 0;
                for (int a = 0; a < ChinaDemographicModel.Core.Models.PopulationPyramid.MaxAge; a++)
                {
                    deaths += p.Male[a] * inp.MortalityMale[a];
                    deaths += p.Female[a] * inp.MortalityFemale[a];
                }
                dxs.Add(y);
                dys.Add(deaths / 10000.0);
            }
            DrawScen(DeathsPlot.Plot, dxs.ToArray(), dys.ToArray(), scen.Name + " (模型)");

            // marriage (from inputs)
            var myVals = years.Select(y => scen.InputsByYear[y].CrudeMarriageRate).ToArray();
            DrawScen(MarriagePlot.Plot, bxs, myVals, scen.Name);
        }

        // 当前年份的竖线
        if (_vm.CurrentYear > 0)
        {
            AddYearMarker(BirthsPlot.Plot, _vm.CurrentYear);
            AddYearMarker(DeathsPlot.Plot, _vm.CurrentYear);
            AddYearMarker(MarriagePlot.Plot, _vm.CurrentYear);
        }

        BirthsPlot.Plot.ShowLegend(Alignment.UpperRight);
        DeathsPlot.Plot.ShowLegend(Alignment.UpperLeft);
        MarriagePlot.Plot.ShowLegend(Alignment.UpperRight);
        BirthsPlot.Refresh();
        DeathsPlot.Refresh();
        MarriagePlot.Refresh();
    }

    private static void DrawObs(Plot plot, IReadOnlyDictionary<int, double> dict, double scale)
    {
        if (dict.Count == 0) return;
        var xs = dict.Keys.OrderBy(k => k).Select(k => (double)k).ToArray();
        var ys = xs.Select(x => dict[(int)x] * scale).ToArray();
        var s = plot.Add.Scatter(xs, ys);
        s.LineStyle.Pattern = LinePattern.Dashed;
        s.LineStyle.Color = Colors.LightSteelBlue.WithAlpha(0.6);
        s.MarkerStyle.Size = 4;
        s.MarkerStyle.Shape = MarkerShape.OpenCircle;
        s.MarkerStyle.LineColor = Colors.LightSteelBlue;
        s.LegendText = "历史观测";
    }

    private static void DrawScen(Plot plot, double[] xs, double[] ys, string label)
    {
        if (xs.Length == 0) return;
        var s = plot.Add.Scatter(xs, ys);
        s.LineStyle.Color = Colors.LightSkyBlue;
        s.LineStyle.Width = 2;
        s.MarkerStyle.Size = 0;
        s.LegendText = label;
    }

    private static void AddYearMarker(Plot plot, int year)
    {
        var vl = plot.Add.VerticalLine(year);
        vl.LineStyle.Color = Colors.Salmon.WithAlpha(0.6);
        vl.LineStyle.Width = 1;
        vl.LineStyle.Pattern = LinePattern.Dotted;
    }

    private static void StylePlot(Plot plot, string title)
    {
        plot.Clear();
        var bg = ScottPlot.Color.FromHex("#111827");
        var fg = ScottPlot.Color.FromHex("#94A3B8");
        var grid = ScottPlot.Color.FromHex("#334155");
        plot.FigureBackground.Color = bg;
        plot.DataBackground.Color = bg;
        plot.Axes.Color(fg);
        foreach (var ax in plot.Axes.GetAxes()) ax.MajorTickStyle.Color = fg;
        plot.Grid.MajorLineColor = grid.WithAlpha(0.3);
        plot.Title(title, size: 13);
        plot.Axes.Title.Label.ForeColor = fg;
        plot.Axes.Title.Label.FontSize = 11;
    }
}
