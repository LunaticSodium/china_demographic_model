using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChinaDemographicModel.Core.Data;
using ChinaDemographicModel.Core.Engine;
using ChinaDemographicModel.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChinaDemographicModel.App.ViewModels;

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

    public bool IsCurrentYearForecast => Historical != null && CurrentYear > LastObservedYear;

    public string CurrentYearMetricsHeader =>
        IsCurrentYearForecast ? "当前年指标 · 模型预测" : "当前年指标";

    /// 预测年的模型派生标量；非预测年返回 null。
    private ForecastedScalars? GetCurrentForecastScalars()
    {
        if (!IsCurrentYearForecast || ActiveScenario == null || Historical == null) return null;
        var ctx = ScenarioBuilder.BuildContext(ActiveScenario, Historical, _builder.Fertility);
        var model = ForecastRegistry.Resolve(ActiveScenario.ForecastModelId);
        return model.ProjectScalars(CurrentYear, ctx);
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
    [ObservableProperty] private string selectedForecastModelId = "ols-trend";

    public IReadOnlyList<IForecastModel> AllForecastModels => ForecastRegistry.AllModels;

    public MainViewModel()
    {
        try
        {
            // 跨平台：从内嵌资源加载（Android/iOS 无相邻文件系统）。桌面也走内嵌，行为一致。
            Historical = HistoricalSeries.LoadEmbedded();
            AppendLog("数据源: 内嵌资源 (Core.dll)");
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

        // 预先构造 ForecastContext + 解析模型——预测年用
        var ctx = ScenarioBuilder.BuildContext(scen, Historical, _builder.Fertility);
        var forecastModel = ForecastRegistry.Resolve(scen.ForecastModelId);
        int lastObs = ctx.LastObservedYear;

        // 死亡侧口径修正：观测年硬对齐 NBS 死亡数并记录 k(y)，预测年用拟合曲线平滑外推。
        var mortCal = new MortalityCalibration();

        var cur = initialAligned;
        for (int y = cur.Year; y < YearMax; y++)
        {
            DemographicInputs inp;
            bool isUserEdited = scen.EditedYears.Contains(y);
            if (y > lastObs && !isUserEdited)
            {
                inp = forecastModel.Project(y, ctx);
                scen.InputsByYear[y] = inp;
            }
            else if (scen.InputsByYear.TryGetValue(y, out var existing))
            {
                inp = existing;
            }
            else
            {
                break;
            }

            // 观测复位：仅对观测年（y ≤ lastObs）
            if (applyHistoryLock && y <= lastObs)
            {
                if (Historical.BirthsByYear.TryGetValue(y, out var b)) inp.TotalBirths = b;
                if (Historical.SexRatioAtBirthByYear.TryGetValue(y, out var s)) inp.SexRatioAtBirth = s;
                if (Historical.CrudeMarriageRateByYear.TryGetValue(y, out var m)) inp.CrudeMarriageRate = m;
                _calibrator.AlignBirthsToHistory(inp, inp.TotalBirths, cur);
            }

            if (inp.TotalBirths <= 0)
            {
                double derived = 0;
                for (int a = 15; a <= 49 && a <= PopulationPyramid.MaxAge; a++)
                    derived += cur.Female[a] * inp.AgeSpecificFertility[a];
                inp.TotalBirths = derived;
            }

            // 死亡侧修正（对称于出生侧观测锁）：
            //   观测年 → 硬对齐 NBS 死亡数，记录 k(y)；
            //   预测年 → 用 k(y) 的拟合曲线外推，保证边界连续、远期收敛。
            //
            // 幂等性：AlignDeathsToHistory 会就地缩放并持久化 q(x)，若不复位，
            // 第二次重跑时 k 会退化为 1、预测年修正丢失 → 边界跳变复现。
            // 因此观测年每次都从 CensusLifeTables 重建基准 q(x)（形状 + e0 水平），
            // 使 RunProjection 成为 (数据, 模型, 编辑) 的纯函数。
            if (y <= lastObs)
            {
                double eM = ScenarioBuilder.LookupOrInterp(Historical.E0MaleByYear, y, fallback: 0);
                double eF = ScenarioBuilder.LookupOrInterp(Historical.E0FemaleByYear, y, fallback: 0);
                if (eM > 0) inp.MortalityMale = CensusLifeTables.GetQx(y, isMale: true, targetE0: eM);
                if (eF > 0) inp.MortalityFemale = CensusLifeTables.GetQx(y, isMale: false, targetE0: eF);
            }

            if (y <= lastObs && Historical.DeathsByYear.TryGetValue(y, out var obsDeaths) && obsDeaths > 0)
            {
                double k = applyHistoryLock
                    ? _calibrator.AlignDeathsToHistory(inp, obsDeaths, cur)
                    : SafeRatio(obsDeaths, Calibrator.PredictDeaths(inp, cur));
                mortCal.Observe(y, k);
            }
            else if (y > lastObs)
            {
                Calibrator.ScaleMortality(inp, mortCal.ProjectK(y));
            }

            var next = _projector.Project(cur, inp);

            // 显性口径修正：对齐到 NBS 年末（逐年连续）
            var (alignedNext, _, _) = PopulationAlignment.AlignToNbsYearEnd(
                next, Historical.TotalPopulationYearEndByYear);
            next = alignedNext;

            scen.ProjectedByYear[next.Year] = next;
            cur = next;
        }

        // 末年 YearMax 本身不再向前投影，循环不会为它生成输入向量 →
        // 年份拖到 2050 时右栏指标会显示 "—"。这里补一份供显示用。
        if (YearMax > lastObs && !scen.EditedYears.Contains(YearMax))
        {
            var lastInp = forecastModel.Project(YearMax, ctx);
            Calibrator.ScaleMortality(lastInp, mortCal.ProjectK(YearMax));
            if (lastInp.TotalBirths <= 0 && scen.ProjectedByYear.TryGetValue(YearMax, out var pEnd))
            {
                double derived = 0;
                for (int a = 15; a <= 49 && a <= PopulationPyramid.MaxAge; a++)
                    derived += pEnd.Female[a] * lastInp.AgeSpecificFertility[a];
                lastInp.TotalBirths = derived;
            }
            scen.InputsByYear[YearMax] = lastInp;
        }

        if (mortCal.PointCount >= 2)
            AppendLog($"死亡侧校准: {mortCal.PointCount} 观测年 · 水平 k={mortCal.Level:0.000} · 残差 RMSE={mortCal.Rmse:0.000} · 趋势 R²={mortCal.RSquared:0.00}（平台期 R² 低属正常，见 MortalityCalibration 注释）");

        RecomputePyramidMax();
    }

    private static double SafeRatio(double num, double den) => den > 0 ? num / den : 1.0;

    /// 计算所有 scenario × year × age × sex 的最大单龄人数（PyramidView X 轴固定刻度）。
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
    public string TfrDisplay
    {
        get
        {
            if (Historical != null)
            {
                double? tfr = TryInterp(Historical.TfrByYear, CurrentYear);
                if (tfr != null) return tfr.Value.ToString("0.00");
            }
            return GetInput() is { } i ? i.TotalFertilityRate.ToString("0.00") : "—";
        }
    }
    public string MafmMaleDisplay => GetInput() is { } i ? i.MeanAgeFirstMarriageMale.ToString("0.0") : "—";
    public string MafmFemaleDisplay => GetInput() is { } i ? i.MeanAgeFirstMarriageFemale.ToString("0.0") : "—";
    public string MarriageRateDisplay => GetInput() is { } i ? i.CrudeMarriageRate.ToString("0.0") : "—";
    public string DeathsDisplay
    {
        get
        {
            if (Historical?.DeathsByYear.TryGetValue(CurrentYear, out var d) == true)
                return FormatPersons(d);
            if (IsCurrentYearForecast && ActiveScenario != null && CurrentPyramid is { } p
                && ActiveScenario.InputsByYear.TryGetValue(CurrentYear, out var inp))
            {
                double deaths = 0;
                for (int a = 0; a < PopulationPyramid.MaxAge; a++)
                {
                    deaths += p.Male[a] * inp.MortalityMale[a];
                    deaths += p.Female[a] * inp.MortalityFemale[a];
                }
                return FormatPersons(deaths);
            }
            if (IsCurrentYearForecast && CurrentPyramid is { } pp)
            {
                var scalars = GetCurrentForecastScalars();
                if (scalars != null)
                {
                    var qM = CensusLifeTables.GetQx(CurrentYear, isMale: true, targetE0: scalars.E0M);
                    var qF = CensusLifeTables.GetQx(CurrentYear, isMale: false, targetE0: scalars.E0F);
                    double deaths = 0;
                    for (int a = 0; a < PopulationPyramid.MaxAge; a++)
                    {
                        deaths += pp.Male[a] * qM[a];
                        deaths += pp.Female[a] * qF[a];
                    }
                    return FormatPersons(deaths);
                }
            }
            return "—";
        }
    }
    public string E0Display
    {
        get
        {
            if (IsCurrentYearForecast)
            {
                var scalars = GetCurrentForecastScalars();
                if (scalars != null)
                    return $"M {scalars.E0M:0.0} / F {scalars.E0F:0.0}";
            }
            if (Historical == null) return "—";
            double? eM = TryInterp(Historical.E0MaleByYear, CurrentYear);
            double? eF = TryInterp(Historical.E0FemaleByYear, CurrentYear);
            if (eM == null && eF == null) return "—";
            if (eM == null) return $"F {eF:0.0}";
            if (eF == null) return $"M {eM:0.0}";
            return $"M {eM:0.0} / F {eF:0.0}";
        }
    }

    /// 邻近年份线性插值；单边锚不外推（返回 null → 显示 "—"）。
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
        return null;
    }

    public string CitationText
    {
        get
        {
            int y = CurrentYear;
            var censusYears = new HashSet<int> { 1982, 1990, 2000, 2010, 2020 };
            if (censusYears.Contains(y)) return BuildCensusCitation(y);
            if (y > LastObservedYear) return BuildForecastCitation(y);
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
        var sb = new StringBuilder();
        int last = LastObservedYear;
        int dy = y - last;
        var model = ForecastRegistry.Resolve(SelectedForecastModelId);
        sb.AppendLine($"预测 · {y} 年 (后 NBS 观测期 {last}, Δt={dy})");
        sb.AppendLine();
        sb.AppendLine($"模型: {model.DisplayName} ({model.Id})");
        sb.AppendLine($"  {model.Description}");
        sb.AppendLine();
        sb.AppendLine("演化（共同）：TotalBirths(t) 不外推为常数,");
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

    // ---- 出生数 ↔ TFR 联动（B2：单变量、消除过定）----
    // 关系：births = TFR × K，K = Σ_{15..49} 育龄女性 × 归一化 ASFR 形状（当前年金字塔女性 + 当前 MAFM）。
    // 拖动出生滑条 → TFR 联动；拖动 TFR → 出生联动；改 MAFM（形状）→ 保持 TFR、重算出生。
    private bool _syncingEditPair;

    /// 单位 TFR 对应的出生人数 K。无当前金字塔时返回 0（联动跳过）。
    private double CurrentReproWeight()
    {
        var p = CurrentPyramid;
        if (p == null) return 0;
        var shape = _builder.Fertility.BuildAgeSpecificFertility(1.0, EditMafmFemale); // TFR=1 → 形状归一
        double k = 0;
        for (int a = 15; a <= 49 && a <= PopulationPyramid.MaxAge; a++)
            k += p.Female[a] * shape[a];
        return k;
    }

    partial void OnEditBirthsWanChanged(double value)
    {
        if (_syncingEditPair) return;
        double k = CurrentReproWeight();
        if (k <= 0) return;
        _syncingEditPair = true;
        EditTfr = Math.Clamp(value * 10000.0 / k, 0.5, 6.5);
        _syncingEditPair = false;
    }

    partial void OnEditTfrChanged(double value)
    {
        if (_syncingEditPair) return;
        double k = CurrentReproWeight();
        if (k <= 0) return;
        _syncingEditPair = true;
        EditBirthsWan = Math.Clamp(value * k / 10000.0, 0, 3500);
        _syncingEditPair = false;
    }

    partial void OnEditMafmFemaleChanged(double value)
    {
        if (_syncingEditPair) return;
        double k = CurrentReproWeight();
        if (k <= 0) return;
        _syncingEditPair = true;
        EditBirthsWan = Math.Clamp(EditTfr * k / 10000.0, 0, 3500);
        _syncingEditPair = false;
    }

    /// 为 InputsByYear 里尚不存在的年新建输入时，从最近邻年克隆死亡率/ASFR 结构，
    /// 避免零死亡率导致该年无死亡、人口虚增（B4）。
    private DemographicInputs SeedNewInput(Scenario scen, int year)
    {
        DemographicInputs? src = null;
        for (int d = 1; d < 40 && src == null; d++)
        {
            if (scen.InputsByYear.TryGetValue(year - d, out var a)) src = a;
            else if (scen.InputsByYear.TryGetValue(year + d, out var b)) src = b;
        }
        var inp = new DemographicInputs { Year = year };
        if (src != null)
        {
            inp.MortalityMale = (double[])src.MortalityMale.Clone();
            inp.MortalityFemale = (double[])src.MortalityFemale.Clone();
            inp.AgeSpecificFertility = (double[])src.AgeSpecificFertility.Clone();
            inp.SexRatioAtBirth = src.SexRatioAtBirth;
            inp.MeanAgeFirstMarriageMale = src.MeanAgeFirstMarriageMale;
            inp.MeanAgeFirstMarriageFemale = src.MeanAgeFirstMarriageFemale;
            inp.CrudeMarriageRate = src.CrudeMarriageRate;
        }
        return inp;
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
        if (value != null && value.ForecastModelId != selectedForecastModelId)
        {
            selectedForecastModelId = value.ForecastModelId;
            OnPropertyChanged(nameof(SelectedForecastModelId));
        }
        NotifyDerived();
    }

    partial void OnSelectedForecastModelIdChanged(string value)
    {
        if (ActiveScenario == null) return;
        ActiveScenario.ForecastModelId = value;
        RunProjectionForScenario(ActiveScenario, LockToHistory);
        var m = ForecastRegistry.Resolve(value);
        AppendLog($"切换预测模型 → {m.DisplayName}");
        ProjectionStamp++;
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
        OnPropertyChanged(nameof(IsCurrentYearForecast));
        OnPropertyChanged(nameof(CurrentYearMetricsHeader));
    }

    [RelayCommand]
    private void ApplyEdits()
    {
        var scen = ActiveScenario;
        if (scen == null) return;
        if (!scen.InputsByYear.TryGetValue(CurrentYear, out var inp))
        {
            inp = SeedNewInput(scen, CurrentYear);   // B4：带死亡率结构，不再零死亡率
            scen.InputsByYear[CurrentYear] = inp;
        }
        inp.TotalBirths = EditBirthsWan * 10000.0;
        inp.SexRatioAtBirth = EditSrb;
        inp.CrudeMarriageRate = EditMarriageRate;
        inp.MeanAgeFirstMarriageMale = EditMafmMale;
        inp.MeanAgeFirstMarriageFemale = EditMafmFemale;
        inp.AgeSpecificFertility = _builder.Fertility.BuildAgeSpecificFertility(EditTfr, EditMafmFemale);
        scen.EditedYears.Add(CurrentYear);

        // B1：立即重跑投影，让编辑生效到金字塔 / 时间序列（否则 ProjectedByYear 是旧的）。
        RunProjectionForScenario(scen, LockToHistory);

        // B3：历史锁 + 观测年 → 编辑被回退，明确告知，别让用户以为"没反应"。
        if (LockToHistory && CurrentYear <= LastObservedYear)
            EditHint = $"注意：历史锁开启，{CurrentYear} 是观测年 —— 出生数 / 性别比 / 结婚率已回退到 NBS 观测值。" +
                       "要真正改历史，请先「＋克隆为反事实」（克隆默认关闭历史锁）。";
        else
            EditHint = $"已应用并重跑投影 → {CurrentYear}。生育 / 死亡是 {CurrentYear}→{CurrentYear + 1} 的转移，" +
                       $"因此结构变化体现在 {CurrentYear + 1} 及之后（看时间序列或把年份拖到 {CurrentYear + 1}）。";

        AppendLog($"已应用编辑 + 重跑投影 → {CurrentYear}");
        ProjectionStamp++;
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
