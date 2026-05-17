namespace ChinaDemographicModel.Core.Models;

/// 单岁分性别人口金字塔。
///
/// **时间参考约定**（与 NBS 年末口径 + WPP 2022 1×1 框架兼容）：
///   `Year = t` 代表"年末 t"的状态，约等于"1 January (t+1)"。
///   即 PopulationPyramid 的标签和 NBS "年末人口" 标签一致。
///
/// CCM 步骤：`Project(pyramid_t, inputs_t) → pyramid_{t+1}`
///   含义："从年末 t 出发，经历年 t+1 的整年事件（生育 / 死亡），到达年末 t+1"。
///   所以 `inputs_t` 在本约定下实际代表"年 t+1 的发生量"。
///
/// 详见 docs/MODEL.md §时间约定 与 docs/AUDIT.md §3。
public sealed class PopulationPyramid
{
    public const int MaxAge = 100;

    public int Year { get; init; }
    public double[] Male { get; init; } = new double[MaxAge + 1];
    public double[] Female { get; init; } = new double[MaxAge + 1];

    public double TotalMale => Male.Sum();
    public double TotalFemale => Female.Sum();
    public double Total => TotalMale + TotalFemale;

    public double SexRatio => TotalFemale <= 0 ? double.NaN : 100.0 * TotalMale / TotalFemale;

    public PopulationPyramid Clone()
    {
        var p = new PopulationPyramid { Year = Year };
        Array.Copy(Male, p.Male, Male.Length);
        Array.Copy(Female, p.Female, Female.Length);
        return p;
    }

    public static PopulationPyramid Empty(int year) => new() { Year = year };

    public double WorkingAgePopulation(int min = 15, int max = 64)
    {
        double s = 0;
        for (int a = min; a <= Math.Min(max, MaxAge); a++) s += Male[a] + Female[a];
        return s;
    }

    public double DependentChildren(int upTo = 14)
    {
        double s = 0;
        for (int a = 0; a <= Math.Min(upTo, MaxAge); a++) s += Male[a] + Female[a];
        return s;
    }

    public double DependentElderly(int from = 65)
    {
        double s = 0;
        for (int a = Math.Min(from, MaxAge); a <= MaxAge; a++) s += Male[a] + Female[a];
        return s;
    }
}
