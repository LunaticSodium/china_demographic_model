using System.ComponentModel;
using System.Windows;
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
            or nameof(MainViewModel.ProjectionStamp)
            or nameof(MainViewModel.SelectedSeriesGroup))
        {
            Dispatcher.BeginInvoke(new Action(Redraw));
        }
    }

    private void Redraw()
    {
        if (_vm == null) return;

        SyncVisibility();

        switch (_vm.SelectedSeriesGroup)
        {
            case SeriesGroup.TenThousandPeople:
                RedrawPeople();
                FooterText.Text = "万人组：出生 / 死亡（万人，对齐 NBS 年末口径）";
                break;
            case SeriesGroup.TenThousandPairs:
                RedrawPairs();
                FooterText.Text = "万对组：结婚 / 离婚登记数（万对，民政部）";
                break;
            case SeriesGroup.Ratios:
                RedrawRatios();
                FooterText.Text = "比率组：出生性别比 / 总和生育率 / 粗结婚率（‰）";
                break;
        }
    }

    private void SyncVisibility()
    {
        if (_vm == null) return;
        PeopleGroup.Visibility = _vm.SelectedSeriesGroup == SeriesGroup.TenThousandPeople ? Visibility.Visible : Visibility.Collapsed;
        PairsGroup.Visibility = _vm.SelectedSeriesGroup == SeriesGroup.TenThousandPairs ? Visibility.Visible : Visibility.Collapsed;
        RatiosGroup.Visibility = _vm.SelectedSeriesGroup == SeriesGroup.Ratios ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RedrawPeople()
    {
        if (_vm?.Historical == null) return;
        StylePlot(BirthsPlot.Plot, "年出生人口（万人）");
        StylePlot(DeathsPlot.Plot, "年死亡人口（万人）");

        DrawObs(BirthsPlot.Plot, _vm.Historical.BirthsByYear, scale: 1.0 / 10000.0, "历史观测");
        DrawObs(DeathsPlot.Plot, _vm.Historical.DeathsByYear, scale: 1.0 / 10000.0, "历史观测");

        var scen = _vm.ActiveScenario;
        if (scen != null && scen.InputsByYear.Count > 0)
        {
            var years = scen.InputsByYear.Keys.OrderBy(k => k).ToArray();
            var xs = years.Select(y => (double)y).ToArray();
            var bys = years.Select(y => scen.InputsByYear[y].TotalBirths / 10000.0).ToArray();
            DrawScen(BirthsPlot.Plot, xs, bys, scen.Name);

            // 死亡：模型预测 = Σ p(a) · q(a)
            var dxs = new List<double>(); var dys = new List<double>();
            foreach (var y in years)
            {
                if (!scen.ProjectedByYear.TryGetValue(y, out var p)) continue;
                var inp = scen.InputsByYear[y];
                double deaths = 0;
                for (int a = 0; a < Core.Models.PopulationPyramid.MaxAge; a++)
                {
                    deaths += p.Male[a] * inp.MortalityMale[a];
                    deaths += p.Female[a] * inp.MortalityFemale[a];
                }
                dxs.Add(y); dys.Add(deaths / 10000.0);
            }
            DrawScen(DeathsPlot.Plot, dxs.ToArray(), dys.ToArray(), $"{scen.Name} (模型)");
        }

        AddYearMarker(BirthsPlot.Plot, _vm.CurrentYear);
        AddYearMarker(DeathsPlot.Plot, _vm.CurrentYear);
        BirthsPlot.Plot.ShowLegend(Alignment.UpperRight);
        DeathsPlot.Plot.ShowLegend(Alignment.UpperLeft);
        BirthsPlot.Refresh();
        DeathsPlot.Refresh();
    }

    private void RedrawPairs()
    {
        if (_vm?.Historical == null) return;
        StylePlot(MarriagesPlot.Plot, "年结婚登记数（万对）");
        StylePlot(DivorcesPlot.Plot, "年离婚登记数（万对）");

        DrawObs(MarriagesPlot.Plot, _vm.Historical.MarriagesByYear, scale: 1.0 / 10000.0, "历史观测");
        DrawObs(DivorcesPlot.Plot, _vm.Historical.DivorcesByYear, scale: 1.0 / 10000.0, "历史观测");

        AddYearMarker(MarriagesPlot.Plot, _vm.CurrentYear);
        AddYearMarker(DivorcesPlot.Plot, _vm.CurrentYear);
        MarriagesPlot.Plot.ShowLegend(Alignment.UpperRight);
        DivorcesPlot.Plot.ShowLegend(Alignment.UpperRight);
        MarriagesPlot.Refresh();
        DivorcesPlot.Refresh();
    }

    private void RedrawRatios()
    {
        if (_vm?.Historical == null) return;
        StylePlot(SrbPlot.Plot, "出生性别比 (M/100F)");
        StylePlot(TfrPlot.Plot, "总和生育率 TFR (模型)");
        StylePlot(MarriageRatePlot.Plot, "粗结婚率（‰）");

        DrawObs(SrbPlot.Plot, _vm.Historical.SexRatioAtBirthByYear, scale: 1.0, "历史观测");
        DrawObs(MarriageRatePlot.Plot, _vm.Historical.CrudeMarriageRateByYear, scale: 1.0, "历史观测");

        var scen = _vm.ActiveScenario;
        if (scen != null && scen.InputsByYear.Count > 0)
        {
            var years = scen.InputsByYear.Keys.OrderBy(k => k).ToArray();
            var xs = years.Select(y => (double)y).ToArray();
            DrawScen(SrbPlot.Plot, xs, years.Select(y => scen.InputsByYear[y].SexRatioAtBirth).ToArray(), scen.Name);
            DrawScen(TfrPlot.Plot, xs, years.Select(y => scen.InputsByYear[y].TotalFertilityRate).ToArray(), scen.Name);
            DrawScen(MarriageRatePlot.Plot, xs, years.Select(y => scen.InputsByYear[y].CrudeMarriageRate).ToArray(), scen.Name);
        }

        AddYearMarker(SrbPlot.Plot, _vm.CurrentYear);
        AddYearMarker(TfrPlot.Plot, _vm.CurrentYear);
        AddYearMarker(MarriageRatePlot.Plot, _vm.CurrentYear);
        SrbPlot.Plot.ShowLegend(Alignment.UpperRight);
        TfrPlot.Plot.ShowLegend(Alignment.UpperRight);
        MarriageRatePlot.Plot.ShowLegend(Alignment.UpperRight);
        SrbPlot.Refresh();
        TfrPlot.Refresh();
        MarriageRatePlot.Refresh();
    }

    private static void DrawObs(Plot plot, IReadOnlyDictionary<int, double> dict, double scale, string label)
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
        s.LegendText = label;
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
        plot.Title(title, size: 12);
        plot.Axes.Title.Label.ForeColor = fg;
        plot.Axes.Title.Label.FontSize = 11;
    }
}
