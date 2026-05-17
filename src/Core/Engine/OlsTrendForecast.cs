namespace ChinaDemographicModel.Core.Engine;

/// OLS 趋势外推：对每个标量取末观测窗口（默认 8 年），最小二乘拟合线性趋势，
/// 外推到目标年；上下限 clamp 防失控。
/// 完全由数据驱动、可解释、确定性。是"插值为主要策略"的自然延伸（外推 = 拟合直线 → 扩展插值）。
public sealed class OlsTrendForecast : ForecastModelBase
{
    public int WindowYears { get; init; } = 8;

    public override string Id => "ols-trend";
    public override string DisplayName => "OLS 趋势外推";
    public override string Description => "末 N 年最小二乘线性趋势外推 + 上下限 clamp";

    // 上下限 clamp 边界（防止外推到不合理值）
    private static readonly (double Floor, double Ceiling) TfrBounds = (0.70, 6.00);
    private static readonly (double Floor, double Ceiling) SrbBounds = (102, 130);
    private static readonly (double Floor, double Ceiling) MafmBounds = (18, 40);
    private static readonly (double Floor, double Ceiling) E0Bounds = (60, 90);
    private static readonly (double Floor, double Ceiling) MrBounds = (1, 15);

    public override ForecastedScalars ProjectScalars(int year, ForecastContext ctx)
    {
        int last = ctx.LastObservedYear;
        int first = last - WindowYears + 1;
        var hist = ctx.Historical;
        return new ForecastedScalars
        {
            Tfr = OlsProject(hist.TfrByYear, year, first, last, TfrBounds, ctx.LastTfr),
            Srb = OlsProject(hist.SexRatioAtBirthByYear, year, first, last, SrbBounds, ctx.LastSrb),
            MafmM = OlsProject(hist.MeanAgeFirstMarriageMaleByYear, year, first, last, MafmBounds, ctx.LastMafmM),
            MafmF = OlsProject(hist.MeanAgeFirstMarriageFemaleByYear, year, first, last, MafmBounds, ctx.LastMafmF),
            E0M = OlsProject(hist.E0MaleByYear, year, first, last, E0Bounds, ctx.LastE0M),
            E0F = OlsProject(hist.E0FemaleByYear, year, first, last, E0Bounds, ctx.LastE0F),
            MarriageRate = OlsProject(hist.CrudeMarriageRateByYear, year, first, last, MrBounds, ctx.LastMarriageRate),
        };
    }

    private static double OlsProject(IReadOnlyDictionary<int, double> dict, int year,
        int first, int last, (double Floor, double Ceiling) bounds, double fallback)
    {
        var pts = dict.Where(kv => kv.Key >= first && kv.Key <= last)
                      .OrderBy(kv => kv.Key)
                      .ToArray();
        if (pts.Length < 2) return Math.Clamp(fallback, bounds.Floor, bounds.Ceiling);

        double xBar = pts.Average(p => (double)p.Key);
        double yBar = pts.Average(p => p.Value);
        double num = 0, den = 0;
        foreach (var p in pts)
        {
            double dx = p.Key - xBar;
            num += dx * (p.Value - yBar);
            den += dx * dx;
        }
        double slope = den > 0 ? num / den : 0;
        double intercept = yBar - slope * xBar;
        double projected = intercept + slope * year;
        return Math.Clamp(projected, bounds.Floor, bounds.Ceiling);
    }
}
