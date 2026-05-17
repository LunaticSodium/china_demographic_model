using System.Collections.ObjectModel;
using System.Text;
using ChinaDemographicModel.Core.Data;
using ChinaDemographicModel.Core.Engine;
using ChinaDemographicModel.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChinaDemographicModel.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CohortComponentProjector _projector = new();
    private readonly Calibrator _calibrator = new();
    private readonly ScenarioBuilder _builder = new();

    public HistoricalSeries? Historical { get; }
    public ObservableCollection<Scenario> Scenarios { get; } = new();

    public int YearMin { get; } = 1978;
    public int YearMax { get; } = 2050;

    [ObservableProperty] private Scenario? activeScenario;
    [ObservableProperty] private int currentYear = 2020;
    [ObservableProperty] private bool lockToHistory = true;
    [ObservableProperty] private string statusLog = "";
    [ObservableProperty] private int projectionStamp;

    // 编辑字段（绑定到 InputsEditorView 的滑条）
    [ObservableProperty] private double editBirthsWan;
    [ObservableProperty] private double editSrb = 105;
    [ObservableProperty] private double editTfr = 1.6;
    [ObservableProperty] private double editMarriageRate = 7.0;
    [ObservableProperty] private double editMafmMale = 26;
    [ObservableProperty] private double editMafmFemale = 24;
    [ObservableProperty] private string editHint = "选择年份并修改滑条，点'应用到当前年'。";

    public MainViewModel()
    {
        try
        {
            var seedDir = SeedLoader.ResolveSeedDir();
            Historical = HistoricalSeries.LoadFromSeedDir(seedDir);
            AppendLog($"种子目录: {seedDir}");
            AppendLog($"出生年份: {Historical.BirthsByYear.Count} 年");
            AppendLog($"普查金字塔: {Historical.CensusPyramidByYear.Count} 个");
        }
        catch (Exception ex)
        {
            AppendLog($"种子加载失败: {ex.Message}");
            Historical = new HistoricalSeries();
        }

        var baseline = _builder.BuildBaseline(Historical, YearMin, YearMax);
        if (baseline.Initial != null)
        {
            // 从 initial 推到 YearMax
            var ranged = _projector.ProjectRange(baseline.Initial, baseline.InputsByYear, baseline.Initial.Year, YearMax);
            foreach (var (y, p) in ranged) baseline.ProjectedByYear[y] = p;
            AppendLog($"基线投影完成: {baseline.Initial.Year} → {YearMax}");
        }
        Scenarios.Add(baseline);
        ActiveScenario = baseline;
        CurrentYear = 2020;
        SyncEditFieldsFromInputs();
    }

    public PopulationPyramid? CurrentPyramid
    {
        get
        {
            if (ActiveScenario == null) return null;
            if (ActiveScenario.ProjectedByYear.TryGetValue(CurrentYear, out var p)) return p;
            if (Historical?.CensusPyramidByYear.TryGetValue(CurrentYear, out var c) == true) return c;
            return null;
        }
    }

    public PopulationPyramid? BaselinePyramid
    {
        get
        {
            var baseline = Scenarios.FirstOrDefault(s => s.Name == "Baseline");
            if (baseline == null) return null;
            if (baseline.ProjectedByYear.TryGetValue(CurrentYear, out var p)) return p;
            return null;
        }
    }

    public string TotalPopulationDisplay => FormatPersons(CurrentPyramid?.Total);
    public string BirthsDisplay => FormatPersons(GetInput()?.TotalBirths);
    public string SrbDisplay => GetInput() is { } i ? i.SexRatioAtBirth.ToString("0.0") : "—";
    public string TfrDisplay => GetInput() is { } i ? i.TotalFertilityRate.ToString("0.00") : "—";
    public string MafmMaleDisplay => GetInput() is { } i ? i.MeanAgeFirstMarriageMale.ToString("0.0") : "—";
    public string MafmFemaleDisplay => GetInput() is { } i ? i.MeanAgeFirstMarriageFemale.ToString("0.0") : "—";
    public string MarriageRateDisplay => GetInput() is { } i ? i.CrudeMarriageRate.ToString("0.0") : "—";
    public string DeathsDisplay
    {
        get
        {
            if (Historical?.DeathsByYear.TryGetValue(CurrentYear, out var d) == true)
                return FormatPersons(d);
            return "—";
        }
    }
    public string E0Display
    {
        get
        {
            if (Historical == null) return "—";
            var mDict = Historical.E0MaleByYear;
            var fDict = Historical.E0FemaleByYear;
            double? eM = mDict.TryGetValue(CurrentYear, out var m) ? m : (double?)null;
            double? eF = fDict.TryGetValue(CurrentYear, out var f) ? f : (double?)null;
            if (eM == null && eF == null) return "—";
            if (eM == null) return $"F {eF:0.0}";
            if (eF == null) return $"M {eM:0.0}";
            return $"M {eM:0.0} / F {eF:0.0}";
        }
    }

    public string DeviationReport
    {
        get
        {
            var scen = ActiveScenario;
            var baseline = Scenarios.FirstOrDefault(s => s.Name == "Baseline");
            if (scen == null || baseline == null || scen == baseline) return "（基线场景，无偏离）";
            if (!scen.ProjectedByYear.TryGetValue(CurrentYear, out var pScen)) return "—";
            if (!baseline.ProjectedByYear.TryGetValue(CurrentYear, out var pBase)) return "—";
            double dTotal = pScen.Total - pBase.Total;
            double pct = pBase.Total == 0 ? 0 : 100 * dTotal / pBase.Total;
            return $"总人口 Δ = {dTotal / 10000.0:+0.0;-0.0;0.0} 万 ({pct:+0.00;-0.00;0.00}%)";
        }
    }

    public string HealthReport
    {
        get
        {
            if (Historical == null) return "数据未加载";
            var sb = new StringBuilder();
            sb.AppendLine($"出生年份: {Historical.BirthsByYear.Count}  · 死亡年份: {Historical.DeathsByYear.Count}");
            sb.AppendLine($"性别比: {Historical.SexRatioAtBirthByYear.Count}  · 结婚率: {Historical.CrudeMarriageRateByYear.Count}");
            sb.AppendLine($"e0 锚: {Historical.E0OverallByYear.Count} 年");
            sb.AppendLine($"普查金字塔: {string.Join(",", Historical.CensusPyramidByYear.Keys.OrderBy(k => k))}");
            sb.Append("缺失年份线性插值；死亡 schedule 由 CD-East + Brass logit 从 e0 求解。");
            return sb.ToString();
        }
    }

    private DemographicInputs? GetInput() =>
        ActiveScenario?.InputsByYear.TryGetValue(CurrentYear, out var i) == true ? i : null;

    private static string FormatPersons(double? v)
    {
        if (v == null) return "—";
        double x = v.Value;
        if (x >= 1e8) return $"{x / 1e8:0.00} 亿";
        if (x >= 1e4) return $"{x / 1e4:0.0} 万";
        return x.ToString("0");
    }

    private void SyncEditFieldsFromInputs()
    {
        var i = GetInput();
        if (i == null) return;
        // 暂时挂起 OnXxxChanged 副作用，直接 set 字段
        editBirthsWan = i.TotalBirths / 10000.0;
        editSrb = i.SexRatioAtBirth;
        editTfr = i.TotalFertilityRate > 0 ? i.TotalFertilityRate : 1.6;
        editMarriageRate = i.CrudeMarriageRate;
        editMafmMale = i.MeanAgeFirstMarriageMale;
        editMafmFemale = i.MeanAgeFirstMarriageFemale;
        OnPropertyChanged(nameof(EditBirthsWan));
        OnPropertyChanged(nameof(EditSrb));
        OnPropertyChanged(nameof(EditTfr));
        OnPropertyChanged(nameof(EditMarriageRate));
        OnPropertyChanged(nameof(EditMafmMale));
        OnPropertyChanged(nameof(EditMafmFemale));
    }

    partial void OnCurrentYearChanged(int value)
    {
        SyncEditFieldsFromInputs();
        NotifyDerived();
    }

    partial void OnActiveScenarioChanged(Scenario? value)
    {
        SyncEditFieldsFromInputs();
        NotifyDerived();
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(CurrentPyramid));
        OnPropertyChanged(nameof(BaselinePyramid));
        OnPropertyChanged(nameof(TotalPopulationDisplay));
        OnPropertyChanged(nameof(BirthsDisplay));
        OnPropertyChanged(nameof(SrbDisplay));
        OnPropertyChanged(nameof(TfrDisplay));
        OnPropertyChanged(nameof(MafmMaleDisplay));
        OnPropertyChanged(nameof(MafmFemaleDisplay));
        OnPropertyChanged(nameof(MarriageRateDisplay));
        OnPropertyChanged(nameof(DeathsDisplay));
        OnPropertyChanged(nameof(E0Display));
        OnPropertyChanged(nameof(DeviationReport));
    }

    [RelayCommand]
    private void ApplyEdits()
    {
        var scen = ActiveScenario;
        if (scen == null) return;
        if (!scen.InputsByYear.TryGetValue(CurrentYear, out var inp))
        {
            inp = new DemographicInputs { Year = CurrentYear };
            scen.InputsByYear[CurrentYear] = inp;
        }
        inp.TotalBirths = EditBirthsWan * 10000.0;
        inp.SexRatioAtBirth = EditSrb;
        inp.CrudeMarriageRate = EditMarriageRate;
        inp.MeanAgeFirstMarriageMale = EditMafmMale;
        inp.MeanAgeFirstMarriageFemale = EditMafmFemale;
        inp.AgeSpecificFertility = _builder.Fertility.BuildAgeSpecificFertility(EditTfr, EditMafmFemale);
        scen.EditedYears.Add(CurrentYear);
        AppendLog($"已应用编辑 → {CurrentYear}");
        NotifyDerived();
    }

    [RelayCommand]
    private void RunProjection()
    {
        var scen = ActiveScenario;
        if (scen == null || scen.Initial == null) return;

        // 紧约束：锁定历史 → 把已观测年的 totals 复位
        if (LockToHistory && Historical != null)
        {
            foreach (var (y, inp) in scen.InputsByYear)
            {
                if (Historical.BirthsByYear.TryGetValue(y, out var b)) inp.TotalBirths = b;
                if (Historical.SexRatioAtBirthByYear.TryGetValue(y, out var s)) inp.SexRatioAtBirth = s;
                if (Historical.CrudeMarriageRateByYear.TryGetValue(y, out var m)) inp.CrudeMarriageRate = m;
            }
        }

        scen.ProjectedByYear.Clear();
        scen.ProjectedByYear[scen.Initial.Year] = scen.Initial.Clone();
        var cur = scen.Initial;
        for (int y = scen.Initial.Year; y < YearMax; y++)
        {
            if (!scen.InputsByYear.TryGetValue(y, out var inp)) break;
            // 紧约束：使用 Calibrator 让模型生成的 births 命中观测
            if (LockToHistory && Historical?.BirthsByYear.TryGetValue(y, out var obsB) == true)
                _calibrator.AlignBirthsToHistory(inp, obsB, cur);
            cur = _projector.Project(cur, inp);
            scen.ProjectedByYear[cur.Year] = cur;
        }
        AppendLog($"重跑投影完成 [{scen.Name}] {scen.Initial.Year}→{YearMax}");
        ProjectionStamp++;
        NotifyDerived();
    }

    [RelayCommand]
    private void ResetScenario()
    {
        var idx = ActiveScenario == null ? -1 : Scenarios.IndexOf(ActiveScenario);
        if (idx < 0) return;
        // 重建该 scenario
        var fresh = _builder.BuildBaseline(Historical ?? new HistoricalSeries(), YearMin, YearMax);
        fresh.Name = ActiveScenario!.Name;
        if (fresh.Initial != null)
        {
            var ranged = _projector.ProjectRange(fresh.Initial, fresh.InputsByYear, fresh.Initial.Year, YearMax);
            foreach (var (y, p) in ranged) fresh.ProjectedByYear[y] = p;
        }
        Scenarios[idx] = fresh;
        ActiveScenario = fresh;
        AppendLog($"已重置场景 [{fresh.Name}]");
        ProjectionStamp++;
        NotifyDerived();
    }

    [RelayCommand]
    private void CloneScenario()
    {
        var src = ActiveScenario ?? Scenarios.FirstOrDefault();
        if (src == null) return;
        var name = $"反事实 #{Scenarios.Count}";
        var clone = src.CloneAs(name);
        clone.LockToHistory = false;  // 克隆默认放开历史锁
        Scenarios.Add(clone);
        ActiveScenario = clone;
        AppendLog($"已克隆为新场景 [{name}]（历史锁默认关闭）");
        ProjectionStamp++;
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        StatusLog = $"[{stamp}] {line}\n" + (StatusLog.Length > 2000 ? StatusLog[..2000] : StatusLog);
    }

    partial void OnLockToHistoryChanged(bool value)
    {
        if (ActiveScenario != null) ActiveScenario.LockToHistory = value;
        AppendLog($"锁定历史 = {value}");
    }
}
