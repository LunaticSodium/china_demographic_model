using ChinaDemographicModel.Core.Data;
using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 从 HistoricalSeries 装配 baseline Scenario 的工厂。
/// - 观测年（y ≤ LastObservedYear）：TotalBirths/SRB/MAFM/婚率 用 NBS / 民政部 观测值；
///   ASFR 用 placeholder TFR=1.6 占位（RunProjection 时 Calibrator 重缩放使 births 命中观测）。
/// - 预测年（y > LastObservedYear）：用 ForecastModel 投影 TFR / e0 / SRB / MAFM / 婚率；
///   TotalBirths 留 0 让 CCM 从 ASFR × Female_15-49 派生（捕获 cohort 衰减效应）。
public sealed class ScenarioBuilder
{
    public FertilityModel Fertility { get; init; } = new();
    public ForecastModel Forecast { get; init; } = new();

    public Scenario BuildBaseline(HistoricalSeries hist, int yearFrom, int yearTo)
    {
        var s = new Scenario { Name = "Baseline", LockToHistory = true };

        int lastObs = hist.BirthsByYear.Count > 0
            ? hist.BirthsByYear.Keys.Max()
            : yearFrom;

        // 选起始金字塔
        PopulationPyramid? initial = null;
        foreach (var (yr, p) in hist.CensusPyramidByYear)
        {
            if (yr <= yearFrom && (initial == null || yr > initial.Year)) initial = p;
        }
        initial ??= hist.CensusPyramidByYear.Values
            .OrderBy(p => Math.Abs(p.Year - yearFrom)).FirstOrDefault();
        s.Initial = initial?.Clone();

        // 末观测年的状态——作为 ForecastModel 起跑点
        double lastSrb = LookupOrInterp(hist.SexRatioAtBirthByYear, lastObs, fallback: 105.0);
        double lastMafmM = LookupOrInterp(hist.MeanAgeFirstMarriageMaleByYear, lastObs, fallback: 26.0);
        double lastMafmF = LookupOrInterp(hist.MeanAgeFirstMarriageFemaleByYear, lastObs, fallback: 24.0);
        double lastMR = LookupOrInterp(hist.CrudeMarriageRateByYear, lastObs, fallback: 6.0);

        for (int y = yearFrom; y <= yearTo; y++)
        {
            bool isForecast = y > lastObs;

            double eM, eF;
            if (isForecast)
            {
                eM = Forecast.ProjectE0(y, lastObs, isMale: true);
                eF = Forecast.ProjectE0(y, lastObs, isMale: false);
            }
            else
            {
                eM = hist.E0MaleByYear.Count > 0
                    ? LookupOrInterp(hist.E0MaleByYear, y, fallback: EstimateE0(y, isMale: true))
                    : EstimateE0(y, isMale: true);
                eF = hist.E0FemaleByYear.Count > 0
                    ? LookupOrInterp(hist.E0FemaleByYear, y, fallback: EstimateE0(y, isMale: false))
                    : EstimateE0(y, isMale: false);
            }

            var inp = new DemographicInputs
            {
                Year = y,
                // 预测年 TotalBirths 留 0 → CCM 从 ASFR × Female_15-49 派生
                TotalBirths = isForecast ? 0 : LookupOrInterp(hist.BirthsByYear, y),
                SexRatioAtBirth = isForecast
                    ? Forecast.ProjectSrb(y, lastObs, lastSrb)
                    : LookupOrInterp(hist.SexRatioAtBirthByYear, y, fallback: 105.0),
                CrudeMarriageRate = isForecast
                    ? Forecast.ProjectMarriageRate(y, lastObs, lastMR)
                    : LookupOrInterp(hist.CrudeMarriageRateByYear, y),
                MeanAgeFirstMarriageMale = isForecast
                    ? Forecast.ProjectMafmMale(y, lastObs, lastMafmM)
                    : LookupOrInterp(hist.MeanAgeFirstMarriageMaleByYear, y, fallback: 26.0),
                MeanAgeFirstMarriageFemale = isForecast
                    ? Forecast.ProjectMafmFemale(y, lastObs, lastMafmF)
                    : LookupOrInterp(hist.MeanAgeFirstMarriageFemaleByYear, y, fallback: 24.0),
                MortalityMale = CensusLifeTables.GetQx(y, isMale: true, targetE0: eM),
                MortalityFemale = CensusLifeTables.GetQx(y, isMale: false, targetE0: eF),
            };

            // ASFR：历史年用 placeholder TFR=1.6 占位（Calibrator 会按观测 births 重缩放）；
            //       预测年用 ForecastModel.ProjectTfr 得到真实 TFR 轨迹。
            double tfr = isForecast
                ? Forecast.ProjectTfr(y, lastObs)
                : 1.6;
            inp.AgeSpecificFertility = Fertility.BuildAgeSpecificFertility(tfr, inp.MeanAgeFirstMarriageFemale);
            s.InputsByYear[y] = inp;
        }
        return s;
    }

    private static double LookupOrInterp(IReadOnlyDictionary<int, double> dict, int year, double fallback = 0)
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

    /// 中国 e0 粗略外推（1978→64, 2024→78.6）—— 仅当 historical e0 完全空时 fallback 用。
    private static double EstimateE0(int year, bool isMale)
    {
        double baseFor = 64 + (year - 1978) * 0.32;
        if (year >= 2010) baseFor = 74 + (year - 2010) * 0.32;
        if (baseFor > 82) baseFor = 82;
        return isMale ? baseFor - 3 : baseFor + 2;
    }
}
