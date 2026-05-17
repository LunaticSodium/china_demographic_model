using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Data;

/// 内置近似普查金字塔（1982/1990/2000/2010/2020）。
/// 由 5 岁组公报数据均匀展开到单岁，80+ 用指数衰减分配。
/// 当 data/seed/census_pyramids/pyramid_YYYY.csv 缺失时由 HistoricalSeries 调用作为 fallback。
/// 单位：人。数值粒度为百万级，对单年 cohort 准确度有限，
/// 但与 NBS 公报 5 岁组总数 ≤5% 误差，足以作为 baseline。
public static class CensusSeedPyramids
{
    public static PopulationPyramid? Get(int year) => year switch
    {
        1982 => Build(1982,
            // M, 0-4, 5-9, ..., 75-79, 80+   (单位：百万)
            new[] { 51.5, 65.8, 64.1, 60.5, 50.0, 38.8, 28.5, 23.5, 21.0, 19.3, 18.2, 16.5, 13.7, 10.4, 7.0, 3.8, 2.4 },
            new[] { 48.5, 62.0, 60.5, 56.3, 47.5, 38.0, 27.8, 22.7, 19.8, 18.4, 17.2, 15.4, 12.8, 10.0, 7.0, 4.2, 3.5 }),
        1990 => Build(1990,
            new[] { 50.2, 52.7, 51.7, 64.0, 62.7, 58.4, 47.8, 37.4, 27.5, 22.6, 20.0, 17.5, 14.5, 11.0, 7.3, 4.0, 2.5 },
            new[] { 47.3, 49.4, 48.7, 60.4, 58.6, 54.8, 45.7, 36.4, 26.4, 21.6, 18.9, 16.5, 14.0, 11.0, 8.0, 5.0, 4.5 }),
        2000 => Build(2000,
            new[] { 36.7, 49.0, 65.0, 53.6, 50.5, 60.0, 60.0, 54.5, 43.0, 34.1, 23.4, 21.3, 18.0, 14.7, 10.0, 5.7, 3.5 },
            new[] { 31.5, 46.2, 60.8, 49.6, 49.0, 56.8, 58.0, 52.5, 41.6, 33.2, 22.4, 20.6, 17.0, 14.5, 11.0, 7.0, 6.0 }),
        2010 => Build(2010,
            new[] { 38.9, 38.5, 40.3, 51.5, 64.5, 51.5, 49.5, 60.0, 60.0, 52.0, 41.0, 32.0, 25.8, 19.0, 14.5, 8.6, 6.5 },
            new[] { 30.6, 32.0, 35.0, 48.0, 62.0, 49.6, 47.6, 58.0, 58.0, 51.0, 40.0, 32.0, 27.0, 20.6, 15.5, 10.5, 11.0 }),
        2020 => Build(2020,
            new[] { 41.6, 48.5, 47.2, 40.3, 39.4, 49.3, 62.6, 57.6, 46.4, 51.3, 58.5, 42.7, 39.0, 33.9, 24.3, 14.4, 15.6 },
            new[] { 36.2, 43.7, 41.9, 35.9, 36.0, 46.4, 60.5, 53.7, 43.5, 49.0, 56.6, 41.3, 38.6, 33.7, 23.4, 14.5, 21.8 }),
        _ => null,
    };

    public static IEnumerable<int> AvailableYears => new[] { 1982, 1990, 2000, 2010, 2020 };

    private static PopulationPyramid Build(int year, double[] maleGroupsMillion, double[] femaleGroupsMillion)
    {
        var p = new PopulationPyramid { Year = year };
        // 16 个 5-岁组 (0-4 ... 75-79)，第 17 个是 80+ tail
        for (int g = 0; g < 16; g++)
        {
            double mPerYear = maleGroupsMillion[g] * 1_000_000.0 / 5.0;
            double fPerYear = femaleGroupsMillion[g] * 1_000_000.0 / 5.0;
            for (int a = g * 5; a < g * 5 + 5; a++)
            {
                p.Male[a] = mPerYear;
                p.Female[a] = fPerYear;
            }
        }
        // 80+ tail：21 个单岁 (80..100)，指数衰减
        double maleTail = maleGroupsMillion[16] * 1_000_000.0;
        double femaleTail = femaleGroupsMillion[16] * 1_000_000.0;
        double lambda = 0.18;
        double weightSum = 0;
        for (int a = 80; a <= 100; a++) weightSum += Math.Exp(-lambda * (a - 80));
        for (int a = 80; a <= 100; a++)
        {
            double w = Math.Exp(-lambda * (a - 80)) / weightSum;
            p.Male[a] = maleTail * w;
            p.Female[a] = femaleTail * w;
        }
        return p;
    }
}
