using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ChinaDemographicModel.App.Controls;
using ChinaDemographicModel.App.Themes;
using ChinaDemographicModel.App.ViewModels;
using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.App.Views;

public partial class TimeSeriesView : UserControl
{
    private MainViewModel? _vm;

    public TimeSeriesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as MainViewModel;
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
            Dispatcher.UIThread.Post(Redraw);
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
        PeopleGroup.IsVisible = _vm.SelectedSeriesGroup == SeriesGroup.TenThousandPeople;
        PairsGroup.IsVisible = _vm.SelectedSeriesGroup == SeriesGroup.TenThousandPairs;
        RatiosGroup.IsVisible = _vm.SelectedSeriesGroup == SeriesGroup.Ratios;
    }

    private void RedrawPeople()
    {
        if (_vm?.Historical == null) return;
        BirthsPlot.Clear(); BirthsPlot.SetTitle("年出生人口（万人）");
        DeathsPlot.Clear(); DeathsPlot.SetTitle("年死亡人口（万人）");

        AddObs(BirthsPlot, _vm.Historical.BirthsByYear, 1.0 / 10000.0);
        AddObs(DeathsPlot, _vm.Historical.DeathsByYear, 1.0 / 10000.0);

        var scen = _vm.ActiveScenario;
        if (scen != null && scen.InputsByYear.Count > 0)
        {
            var years = scen.InputsByYear.Keys.OrderBy(k => k).ToArray();
            var xs = years.Select(y => (double)y).ToArray();
            var bys = years.Select(y => scen.InputsByYear[y].TotalBirths / 10000.0).ToArray();
            AddScen(BirthsPlot, xs, bys, scen.Name);

            var dxs = new List<double>(); var dys = new List<double>();
            foreach (var y in years)
            {
                if (!scen.ProjectedByYear.TryGetValue(y, out var p)) continue;
                var inp = scen.InputsByYear[y];
                double deaths = 0;
                for (int a = 0; a < PopulationPyramid.MaxAge; a++)
                {
                    deaths += p.Male[a] * inp.MortalityMale[a];
                    deaths += p.Female[a] * inp.MortalityFemale[a];
                }
                dxs.Add(y); dys.Add(deaths / 10000.0);
            }
            AddScen(DeathsPlot, dxs.ToArray(), dys.ToArray(), $"{scen.Name} (模型)");
        }

        BirthsPlot.SetMarker(_vm.CurrentYear); DeathsPlot.SetMarker(_vm.CurrentYear);
        BirthsPlot.Commit(); DeathsPlot.Commit();
    }

    private void RedrawPairs()
    {
        if (_vm?.Historical == null) return;
        MarriagesPlot.Clear(); MarriagesPlot.SetTitle("年结婚登记数（万对）");
        DivorcesPlot.Clear(); DivorcesPlot.SetTitle("年离婚登记数（万对）");

        AddObs(MarriagesPlot, _vm.Historical.MarriagesByYear, 1.0 / 10000.0);
        AddObs(DivorcesPlot, _vm.Historical.DivorcesByYear, 1.0 / 10000.0);

        MarriagesPlot.SetMarker(_vm.CurrentYear); DivorcesPlot.SetMarker(_vm.CurrentYear);
        MarriagesPlot.Commit(); DivorcesPlot.Commit();
    }

    private void RedrawRatios()
    {
        if (_vm?.Historical == null) return;
        SrbPlot.Clear(); SrbPlot.SetTitle("出生性别比 (M/100F)");
        TfrPlot.Clear(); TfrPlot.SetTitle("总和生育率 TFR (模型)");
        MarriageRatePlot.Clear(); MarriageRatePlot.SetTitle("粗结婚率（‰）");

        AddObs(SrbPlot, _vm.Historical.SexRatioAtBirthByYear, 1.0);
        AddObs(MarriageRatePlot, _vm.Historical.CrudeMarriageRateByYear, 1.0);

        var scen = _vm.ActiveScenario;
        if (scen != null && scen.InputsByYear.Count > 0)
        {
            var years = scen.InputsByYear.Keys.OrderBy(k => k).ToArray();
            var xs = years.Select(y => (double)y).ToArray();
            AddScen(SrbPlot, xs, years.Select(y => scen.InputsByYear[y].SexRatioAtBirth).ToArray(), scen.Name);
            AddScen(TfrPlot, xs, years.Select(y => scen.InputsByYear[y].TotalFertilityRate).ToArray(), scen.Name);
            AddScen(MarriageRatePlot, xs, years.Select(y => scen.InputsByYear[y].CrudeMarriageRate).ToArray(), scen.Name);
        }

        SrbPlot.SetMarker(_vm.CurrentYear); TfrPlot.SetMarker(_vm.CurrentYear); MarriageRatePlot.SetMarker(_vm.CurrentYear);
        SrbPlot.Commit(); TfrPlot.Commit(); MarriageRatePlot.Commit();
    }

    private static void AddObs(MiniLineChart chart, IReadOnlyDictionary<int, double> dict, double scale)
    {
        if (dict.Count == 0) return;
        var xs = dict.Keys.OrderBy(k => k).Select(k => (double)k).ToArray();
        var ys = xs.Select(x => dict[(int)x] * scale).ToArray();
        chart.Add(new MiniLineChart.Series { Xs = xs, Ys = ys, Color = Palette.LightSteelBlue, Dashed = true, Markers = true, Label = "历史观测" });
    }

    private static void AddScen(MiniLineChart chart, double[] xs, double[] ys, string label)
    {
        if (xs.Length == 0) return;
        chart.Add(new MiniLineChart.Series { Xs = xs, Ys = ys, Color = Palette.LightSkyBlue, Dashed = false, Markers = false, Label = label });
    }
}
