using ChinaDemographicModel.Core.Data;
using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 从 HistoricalSeries 装配 baseline Scenario 的工厂。
/// **只构造观测年的 inputs**（1978-LastObservedYear）。预测年的 inputs 在 RunProjection 时
/// 由 Scenario.ForecastModelId 解析的 IForecastModel 即时产生——不写入 InputsByYear。
/// 这是用户 round 6/7 强调的"数据 / 模型解耦"。
public sealed class ScenarioBuilder
{
    public FertilityModel Fertility { get; init; } = new();

    public Scenario BuildBaseline(HistoricalSeries hist, int yearFrom, int yearTo)
    {
        var s = new Scenario { Name = "Baseline", LockToHistory = true };

        int lastObs = hist.BirthsByYear.Count > 0
            ? hist.BirthsByYear.Keys.Max()
            : yearFrom;

        // 起始金字塔
        PopulationPyramid? initial = null;
        foreach (var (yr, p) in hist.CensusPyramidByYear)
        {
            if (yr <= yearFrom && (initial == null || yr > initial.Year)) initial = p;
        }
        initial ??= hist.CensusPyramidByYear.Values
            .OrderBy(p => Math.Abs(p.Year - yearFrom)).FirstOrDefault();
        s.Initial = initial?.Clone();

        // 仅构造**观测年** inputs
        for (int y = yearFrom; y <= Math.Min(yearTo, lastObs); y++)
        {
            double eM = hist.E0MaleByYear.Count > 0
                ? LookupOrInterp(hist.E0MaleByYear, y, fallback: EstimateE0(y, isMale: true))
                : EstimateE0(y, isMale: true);
            double eF = hist.E0FemaleByYear.Count > 0
                ? LookupOrInterp(hist.E0FemaleByYear, y, fallback: EstimateE0(y, isMale: false))
                : EstimateE0(y, isMale: false);

            var inp = new DemographicInputs
            {
                Year = y,
                TotalBirths = LookupOrInterp(hist.BirthsByYear, y),
                SexRatioAtBirth = LookupOrInterp(hist.SexRatioAtBirthByYear, y, fallback: 105.0),
                CrudeMarriageRate = LookupOrInterp(hist.CrudeMarriageRateByYear, y),
                MeanAgeFirstMarriageMale = LookupOrInterp(hist.MeanAgeFirstMarriageMaleByYear, y, fallback: 26.0),
                MeanAgeFirstMarriageFemale = LookupOrInterp(hist.MeanAgeFirstMarriageFemaleByYear, y, fallback: 24.0),
                MortalityMale = CensusLifeTables.GetQx(y, isMale: true, targetE0: eM),
                MortalityFemale = CensusLifeTables.GetQx(y, isMale: false, targetE0: eF),
            };
            // 历史年 ASFR 用 placeholder TFR=1.6 占位（Calibrator 会按观测 births 重缩放）
            inp.AgeSpecificFertility = Fertility.BuildAgeSpecificFertility(1.6, inp.MeanAgeFirstMarriageFemale);
            s.InputsByYear[y] = inp;
        }
        return s;
    }

    /// 构造 ForecastContext：末观测年的标量状态——交给 IForecastModel 用。
    public static ForecastContext BuildContext(Scenario scen, HistoricalSeries hist, FertilityModel fertility)
    {
        int lastObs = hist.BirthsByYear.Count > 0 ? hist.BirthsByYear.Keys.Max() : 2024;
        DemographicInputs? lastInp = scen.InputsByYear.TryGetValue(lastObs, out var li) ? li : null;
        return new ForecastContext
        {
            LastObservedYear = lastObs,
            LastTfr = LookupOrInterp(hist.TfrByYear, lastObs, fallback: 1.0),
            LastSrb = lastInp?.SexRatioAtBirth ?? LookupOrInterp(hist.SexRatioAtBirthByYear, lastObs, fallback: 108.0),
            LastMafmM = lastInp?.MeanAgeFirstMarriageMale ?? 29.4,
            LastMafmF = lastInp?.MeanAgeFirstMarriageFemale ?? 28.0,
            LastE0M = LookupOrInterp(hist.E0MaleByYear, lastObs, fallback: 75.9),
            LastE0F = LookupOrInterp(hist.E0FemaleByYear, lastObs, fallback: 81.5),
            LastMarriageRate = lastInp?.CrudeMarriageRate ?? LookupOrInterp(hist.CrudeMarriageRateByYear, lastObs, fallback: 4.5),
            Historical = hist,
            Fertility = fertility,
        };
    }

    public static double LookupOrInterp(IReadOnlyDictionary<int, double> dict, int year, double fallback = 0)
    {
        if (dict.TryGetValue(year, out double v)) return v;
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
        if (before.HasValue) return dict[before.Value];
        if (after.HasValue) return dict[after.Value];
        return fallback;
    }

    /// 中国 e0 粗略外推（仅当 historical e0 完全空时 fallback）
    private static double EstimateE0(int year, bool isMale)
    {
        double baseFor = 64 + (year - 1978) * 0.32;
        if (year >= 2010) baseFor = 74 + (year - 2010) * 0.32;
        if (baseFor > 82) baseFor = 82;
        return isMale ? baseFor - 3 : baseFor + 2;
    }
}
