using ChinaDemographicModel.Core.Data;
using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 从 HistoricalSeries 装配 baseline Scenario 的工厂。
/// 紧模型约束：在历史观测年里，TotalBirths/SRB 用观测值；缺失年份用临近年份插值。
public sealed class ScenarioBuilder
{
    public FertilityModel Fertility { get; init; } = new();

    public Scenario BuildBaseline(HistoricalSeries hist, int yearFrom, int yearTo)
    {
        var s = new Scenario { Name = "Baseline", LockToHistory = true };

        // 选起始金字塔
        PopulationPyramid? initial = null;
        foreach (var (yr, p) in hist.CensusPyramidByYear)
        {
            if (yr <= yearFrom && (initial == null || yr > initial.Year)) initial = p;
        }
        initial ??= hist.CensusPyramidByYear.Values
            .OrderBy(p => Math.Abs(p.Year - yearFrom)).FirstOrDefault();
        s.Initial = initial?.Clone();

        // 各年 inputs
        for (int y = yearFrom; y <= yearTo; y++)
        {
            // e0：优先用观测，否则回退到估计
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
                // 死亡率 schedule：1981-2020 直接用普查生命表；范围外用 Brass shift 到目标 e0
                MortalityMale = Data.CensusLifeTables.GetQx(y, isMale: true, targetE0: eM),
                MortalityFemale = Data.CensusLifeTables.GetQx(y, isMale: false, targetE0: eF),
            };
            // ASFR 从 TFR + MAFM 推导。TFR 用 births / 育龄女性 反推（如果有 initial 金字塔）。
            double tfr = 1.6; // placeholder
            inp.AgeSpecificFertility = Fertility.BuildAgeSpecificFertility(tfr, inp.MeanAgeFirstMarriageFemale);
            s.InputsByYear[y] = inp;
        }
        return s;
    }

    private static double LookupOrInterp(IReadOnlyDictionary<int, double> dict, int year, double fallback = 0)
    {
        if (dict.TryGetValue(year, out double v)) return v;
        // 找最近上下
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

    /// 中国 e0 粗略外推（1978→64, 2024→78.6）。
    private static double EstimateE0(int year, bool isMale)
    {
        double baseFor = 64 + (year - 1978) * 0.32;
        if (year >= 2010) baseFor = 74 + (year - 2010) * 0.32;
        if (baseFor > 82) baseFor = 82;
        return isMale ? baseFor - 3 : baseFor + 2;
    }
}
