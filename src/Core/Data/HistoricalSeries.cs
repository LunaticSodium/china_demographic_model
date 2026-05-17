using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Data;

/// 历史观测数据的整合容器。
public sealed class HistoricalSeries
{
    public Dictionary<int, double> BirthsByYear { get; init; } = new();        // 单位：人
    public Dictionary<int, double> DeathsByYear { get; init; } = new();        // 单位：人
    public Dictionary<int, double> SexRatioAtBirthByYear { get; init; } = new();
    public Dictionary<int, double> CrudeMarriageRateByYear { get; init; } = new();
    public Dictionary<int, double> MeanAgeFirstMarriageMaleByYear { get; init; } = new();
    public Dictionary<int, double> MeanAgeFirstMarriageFemaleByYear { get; init; } = new();
    public Dictionary<int, double> E0OverallByYear { get; init; } = new();
    public Dictionary<int, double> E0MaleByYear { get; init; } = new();
    public Dictionary<int, double> E0FemaleByYear { get; init; } = new();
    public Dictionary<int, PopulationPyramid> CensusPyramidByYear { get; init; } = new();

    public IEnumerable<int> Years
    {
        get
        {
            var s = new SortedSet<int>();
            foreach (var y in BirthsByYear.Keys) s.Add(y);
            foreach (var y in DeathsByYear.Keys) s.Add(y);
            foreach (var y in SexRatioAtBirthByYear.Keys) s.Add(y);
            foreach (var y in CrudeMarriageRateByYear.Keys) s.Add(y);
            foreach (var y in E0OverallByYear.Keys) s.Add(y);
            foreach (var y in CensusPyramidByYear.Keys) s.Add(y);
            return s;
        }
    }

    public static HistoricalSeries LoadFromSeedDir(string seedDir)
    {
        var s = new HistoricalSeries
        {
            BirthsByYear = ScaleWanToPersons(SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "births_yearly.csv"), "total_births_wan")),
            DeathsByYear = ScaleWanToPersons(SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "deaths_yearly.csv"), "total_deaths_wan")),
            SexRatioAtBirthByYear = SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "sex_ratio_at_birth.csv"), "srb"),
            CrudeMarriageRateByYear = SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "marriage_rate.csv"), "crude_rate_per_1000"),
            MeanAgeFirstMarriageMaleByYear = SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "mean_age_first_marriage.csv"), "male"),
            MeanAgeFirstMarriageFemaleByYear = SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "mean_age_first_marriage.csv"), "female"),
            E0OverallByYear = SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "life_expectancy.csv"), "e0_overall"),
            E0MaleByYear = SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "life_expectancy.csv"), "e0_male"),
            E0FemaleByYear = SeedLoader.LoadYearlyScalar(Path.Combine(seedDir, "life_expectancy.csv"), "e0_female"),
        };
        var pyrDir = Path.Combine(seedDir, "census_pyramids");
        if (Directory.Exists(pyrDir))
        {
            foreach (var file in Directory.EnumerateFiles(pyrDir, "*.csv"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name.Replace("pyramid_", ""), out int year))
                    s.CensusPyramidByYear[year] = SeedLoader.LoadCensusPyramid(file, year);
            }
        }
        // fallback: 缺失年份从 CensusSeedPyramids 取近似
        foreach (var y in CensusSeedPyramids.AvailableYears)
        {
            if (!s.CensusPyramidByYear.ContainsKey(y))
            {
                var p = CensusSeedPyramids.Get(y);
                if (p != null) s.CensusPyramidByYear[y] = p;
            }
        }
        return s;
    }

    private static Dictionary<int, double> ScaleWanToPersons(Dictionary<int, double> wan)
    {
        var d = new Dictionary<int, double>();
        foreach (var (y, v) in wan) d[y] = v * 10000.0;
        return d;
    }
}
