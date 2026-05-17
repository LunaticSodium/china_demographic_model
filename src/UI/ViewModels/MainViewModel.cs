using System.Collections.ObjectModel;
using System.Text;
using ChinaDemographicModel.Core.Data;
using ChinaDemographicModel.Core.Engine;
using ChinaDemographicModel.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChinaDemographicModel.UI.ViewModels;

public enum SeriesGroup
{
    TenThousandPeople,  // 万人组：出生、死亡
    TenThousandPairs,   // 万对组：结婚、离婚
    Ratios,             // 比率组：SRB、TFR、粗结婚率
}

public partial class MainViewModel : ObservableObject
{
    private readonly CohortComponentProjector _projector = new();
    private readonly Calibrator _calibrator = new();
    private readonly ScenarioBuilder _builder = new();

    public HistoricalSeries? Historical { get; }
    public ObservableCollection<Scenario> Scenarios { get; } = new();

    public int YearMin { get; } = 1982;
    public int YearMax { get; } = 2050;

    public IEnumerable<int> CensusYears =>
        Historical?.CensusPyramidByYear.Keys.OrderBy(k => k) ?? Enumerable.Empty<int>();

    public int LastObservedYear =>
        Historical?.BirthsByYear.Keys.DefaultIfEmpty(2024).Max() ?? 2024;

    public bool IsCounterfactualScenario
    {
        get
        {
            var baseline = Scenarios.FirstOrDefault(s => s.Name == "Baseline");
            return baseline != null && ActiveScenario != null && ActiveScenario != baseline;
        }
    }

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
    [ObservableProperty] private SeriesGroup selectedSeriesGroup = SeriesGroup.TenThousandPeople;
    [ObservableProperty] private double pyramidMaxPerAge;  // 跨所有 scenario + year 的最大单龄人数，X 轴固定刻度

    public MainViewModel()
    {
        try
        {
            var seedDir = SeedLoader.ResolveSeedDir();
            Historical = HistoricalSeries.LoadFromSeedDir(seedDir);
            AppendLog($"种子目录: {seedDir}");
            // 显式 sanity check —— 任一关键字典为空都立刻可见，避免 round 3 那种
            // silent empty-dict bug 再次潜伏。
            AppendLog($"数据载入: 出生{Historical.BirthsByYear.Count} 死亡{Historical.DeathsByYear.Count} 年末人口{Historical.TotalPopulationYearEndByYear.Count} SRB{Historical.SexRatioAtBirthByYear.Count} 结婚率{Historical.CrudeMarriageRateByYear.Count} 万对{Historical.MarriagesByYear.Count} 平均初婚{Historical.MeanAgeFirstMarriageMaleByYear.Count}");
            AppendLog($"e0载入: 整体{Historical.E0OverallByYear.Count} 男{Historical.E0MaleByYear.Count} 女{Historical.E0FemaleByYear.Count}  · 普查金字塔: {string.Join(",", Historical.CensusPyramidByYear.Keys.OrderBy(k => k))}");
        }
        catch (Exception ex)
        {
            AppendLog($"种子加载失败: {ex.Message}");
            Historical = new HistoricalSeries();
        }

        var baseline = _builder.BuildBaseline(Historical, YearMin, YearMax);
        Scenarios.Add(baseline);
        ActiveScenario = baseline;
        RunProjectionForScenario(baseline, baseline.LockToHistory);
        AppendLog($"基线投影完成 [Baseline] {baseline.Initial?.Year} → {YearMax}");
        CurrentYear = 2020;
        SyncEditFieldsFromInputs();
    }

    /// 给定 scenario 跑完整投影：观测复位 → CCM → NBS 口径对齐。
    /// 这是 baseline 初始化和 RunProjection 命令的共用实现。
    private void RunProjectionForScenario(Scenario scen, bool applyHistoryLock)
    {
        if (scen.Initial == null || Historical == null) return;

        // 起始金字塔：对齐 NBS 年末
        var (initialAligned, initWasCorr, _) = PopulationAlignment.AlignToNbsYearEnd(
            scen.Initial, Historical.TotalPopulationYearEndByYear);
        scen.ProjectedByYear.Clear();
        scen.ProjectedByYear[initialAligned.Year] = initialAligned;
        if (initWasCorr)
            AppendLog($"PopulationAlignment: 起始 {initialAligned.Year} 已对齐 NBS 年末口径");

        var cur = initialAligned;
        for (int y = cur.Year; y < YearMax; y++)
        {
            if (!scen.InputsByYear.TryGetValue(y, out var inp)) break;

            // 观测复位：已观测年的总值强制等于 NBS
            if (applyHistoryLock)
            {
                if (Historical.BirthsByYear.TryGetValue(y, out var b)) inp.TotalBirths = b;
                if (Historical.SexRatioAtBirthByYear.TryGetValue(y, out var s)) inp.SexRatioAtBirth = s;
                if (Historical.CrudeMarriageRateByYear.TryGetValue(y, out var m)) inp.CrudeMarriageRate = m;
                _calibrator.AlignBirthsToHistory(inp, inp.TotalBirths, cur);
            }

            // CCM
            var next = _projector.Project(cur, inp);

            // 注意：round 2 在此处有 AlignPyramidToCensus 覆盖普查年金字塔。
            // round 3 移除——WPP 2022 §I.A 说明 CCMPP 应当通过迭代调整组件来逼近
            // 普查 benchmark，而不是用普查覆盖投影。覆盖会破坏 cohort 连续性
            // （1989→1990 同一 cohort 不衔接）。详见 docs/AUDIT.md §1。
            // 普查金字塔现在只作为 baseline.Initial 的种子，以及未来 IPF / 软对齐
            // 的钩子（Calibrator.AlignPyramidToCensus 函数本身保留但不再被调用）。

            // 显性口径修正：对齐到 NBS 年末（逐年连续）
            var (alignedNext, _, _) = PopulationAlignment.AlignToNbsYearEnd(
                next, Historical.TotalPopulationYearEndByYear);
            next = alignedNext;

            scen.ProjectedByYear[next.Year] = next;
            cur = next;
        }

        RecomputePyramidMax();
    }

    /// 计算所有 scenario × year × age × sex 的最大单龄人数。
    /// 用作 PyramidView X 轴的**固定刻度**，让跨年比较视觉可比。
    private void RecomputePyramidMax()
    {
        double m = 0;
        foreach (var scen in Scenarios)
        {
            foreach (var (_, p) in scen.ProjectedByYear)
            {
                for (int a = 0; a <= PopulationPyramid.MaxAge; a++)
                {
                    if (p.Male[a] > m) m = p.Male[a];
                    if (p.Female[a] > m) m = p.Female[a];
                }
            }
        }
        PyramidMaxPerAge = m;
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
            // 与 ScenarioBuilder 的 LookupOrInterp 一致：邻近年份线性插值
            // （否则 1982 显 "—" 因为 CSV 锚是 1981 而非 1982）；预测年（无 before / after）仍显 "—"。
            double? eM = TryInterp(Historical.E0MaleByYear, CurrentYear);
            double? eF = TryInterp(Historical.E0FemaleByYear, CurrentYear);
            if (eM == null && eF == null) return "—";
            if (eM == null) return $"F {eF:0.0}";
            if (eF == null) return $"M {eM:0.0}";
            return $"M {eM:0.0} / F {eF:0.0}";
        }
    }

    /// 邻近年份线性插值；年份在锚之外（only-after 或 only-before 都不算）返回 null，
    /// 让 display 显示 "—" 而非"用最远锚的常值"——避免误导。
    private static double? TryInterp(IReadOnlyDictionary<int, double> dict, int year)
    {
        if (dict.Count == 0) return null;
        if (dict.TryGetValue(year, out var v)) return v;
        int? before = null, after = null;
        foreach (var k in dict.Keys)
        {
            if (k < year && (before == null || k > before)) before = k;
            if (k > year && (after == null || k < after)) after = k;
        }
        if (before.HasValue && after.HasValue)
        {
            double t = (double)(year - before.Value) / (after.Value - before.Value);
            return dict[before.Value] * (1 - t) + dict[after.Value] * t;
        }
        return null;  // 只有单边锚 → 不外推
    }

    public string CitationText
    {
        get
        {
            int y = CurrentYear;
            var censusYears = new HashSet<int> { 1982, 1990, 2000, 2010, 2020 };

            if (censusYears.Contains(y))
                return BuildCensusCitation(y);
            if (y > LastObservedYear)
                return BuildForecastCitation(y);
            return BuildEstimateCitation(y);
        }
    }

    private static string BuildCensusCitation(int y)
    {
        var (name, url) = y switch
        {
            1982 => ("第三次全国人口普查 (1982-07-01)", "https://www.stats.gov.cn/sj/tjgb/rkpcgb/qgrkpcgb/"),
            1990 => ("第四次全国人口普查 (1990-07-01)", "https://www.stats.gov.cn/sj/tjgb/rkpcgb/qgrkpcgb/"),
            2000 => ("第五次全国人口普查 (2000-11-01)", "https://www.stats.gov.cn/sj/tjgb/rkpcgb/qgrkpcgb/"),
            2010 => ("第六次全国人口普查 (2010-11-01)", "https://www.stats.gov.cn/zt_18555/zdtjgz/zgrkpc/d6crkpc/"),
            2020 => ("第七次全国人口普查 (2020-11-01)", "https://www.stats.gov.cn/sj/pcsj/rkpc/d7c/"),
            _ => ("普查公报", "https://www.stats.gov.cn/sj/pcsj/rkpc/")
        };
        return "数据源 · " + name + "\n" + url + "\n" +
               "\n直接取自普查公报:\n" +
               "  · 出生性别比 (SRB)\n" +
               "  · 平均初婚年龄 (男/女)\n" +
               "  · 出生时预期寿命 e0 (男/女)\n" +
               "\n取自 NBS《中国统计年鉴》年度数据:\n" +
               "  · 总人口 (年末口径) · 年出生数 · 年死亡数 · 粗结婚率\n" +
               "\n金字塔形状 = CCM 自 1982 起向前推演; 普查实际年龄结构\n" +
               "不直接覆盖（保 cohort 连续性, 见 docs/AUDIT.md §1）";
    }

    private static string BuildEstimateCitation(int y)
    {
        return $"拟合 · {y} 年\n" +
               "\n取自 NBS《中国统计年鉴》年度估算:\n" +
               "  · 总人口 (年末口径) · 年出生数 · 年死亡数\n" +
               "  · 粗结婚率 (民政部统计公报, 2002+)\n" +
               "\n邻近普查年线性插值:\n" +
               "  · 出生性别比 (SRB)\n" +
               "  · 平均初婚年龄 · 出生时预期寿命 e0\n" +
               "\n演化公式:\n" +
               "  P(a+1, s, t+1) = P(a, s, t) · (1 − q(a, s, t))\n" +
               "  B(t)           ≡ NBS_births(t)   [观测锁]\n" +
               "  P(0, M, t+1)   = B · SRB/(100+SRB) · (1−q₀ᴹ)\n" +
               "  P(0, F, t+1)   = B · 100/(100+SRB) · (1−q₀ꜰ)\n" +
               "  P_aligned       = P · NBS_yearend / Σ P  (PopulationAlignment)\n" +
               "  q(a,s,t)        ← CensusLifeTables (5 普查×22 锚, 时间×年龄插值)";
    }

    private string BuildForecastCitation(int y)
    {
        var sb = new System.Text.StringBuilder();
        int last = LastObservedYear;
        int dy = y - last;
        sb.AppendLine($"预测 · {y} 年 (后 NBS 观测期 {last})");
        sb.AppendLine();
        sb.AppendLine("ForecastModel 投影 (Core/Engine/ForecastModel.cs):");
        sb.AppendLine($"  TFR(t)  = max(0.85, 1.00 − 0.005·Δt)  [Δt = {dy}]");
        sb.AppendLine($"  e0(t)   = min(86, e0({last}) + 0.12·Δt)");
        sb.AppendLine($"  SRB(t)  = 105.5 + (SRB({last}) − 105.5)·exp(−ln2/18·Δt)");
        sb.AppendLine($"  MAFM(t) = min(33男 / 31女, MAFM({last}) + 0.10·Δt)");
        sb.AppendLine($"  婚率(t) = 3.0 + (婚率({last}) − 3.0)·exp(−ln2/20·Δt)");
        sb.AppendLine();
        sb.AppendLine("演化（关键）：TotalBirths(t) 不外推为常数,");
        sb.AppendLine("  改由 CCM 从 ASFR(t) × Female_15-49(t) 派生.");
        sb.AppendLine("  → 1990s-2010s 缩小队列进入育龄段时, births 自然下降.");
        sb.AppendLine();
        sb.AppendLine("q(a,s,t) ← CensusLifeTables[2020] + Brass shift 到 e0(t).");
        sb.AppendLine("无 NBS 对齐 / 无观测复位 → 预测自由演化.");

        if (IsCounterfactualScenario && ActiveScenario != null && ActiveScenario.EditedYears.Contains(y))
        {
            sb.AppendLine();
            sb.AppendLine($"反事实修改 (本年, 来自场景 [{ActiveScenario.Name}]):");
            if (ActiveScenario.InputsByYear.TryGetValue(y, out var inp))
            {
                sb.AppendLine($"  TotalBirths = {inp.TotalBirths / 1e4:0} 万");
                sb.AppendLine($"  SRB         = {inp.SexRatioAtBirth:0.0}");
                sb.AppendLine($"  TFR         = {inp.TotalFertilityRate:0.00}");
                sb.AppendLine($"  MAFM (F)    = {inp.MeanAgeFirstMarriageFemale:0.0}");
                sb.AppendLine($"  CrudeMarriageRate = {inp.CrudeMarriageRate:0.0}");
            }
        }
        else if (IsCounterfactualScenario)
        {
            sb.AppendLine();
            sb.AppendLine($"场景 [{ActiveScenario?.Name}] 当前年未被编辑.");
        }

        return sb.ToString();
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
        OnPropertyChanged(nameof(IsCounterfactualScenario));
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
        OnPropertyChanged(nameof(CitationText));
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
        if (scen == null) return;
        RunProjectionForScenario(scen, LockToHistory);
        AppendLog($"重跑投影完成 [{scen.Name}] {scen.Initial?.Year}→{YearMax} (lock={LockToHistory})");
        ProjectionStamp++;
        NotifyDerived();
    }

    [RelayCommand]
    private void ResetScenario()
    {
        var idx = ActiveScenario == null ? -1 : Scenarios.IndexOf(ActiveScenario);
        if (idx < 0) return;
        var fresh = _builder.BuildBaseline(Historical ?? new HistoricalSeries(), YearMin, YearMax);
        fresh.Name = ActiveScenario!.Name;
        fresh.LockToHistory = ActiveScenario.LockToHistory;
        Scenarios[idx] = fresh;
        ActiveScenario = fresh;
        RunProjectionForScenario(fresh, fresh.LockToHistory);
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
