namespace ChinaDemographicModel.Core.Engine;

/// 阻尼趋势预测：各变量按指数 / 线性带 floor / ceiling 的预设轨迹收敛。
/// 是 round 6 的 ForecastModel 移植到 IForecastModel 接口下的版本。
/// 参数为构造时设定的"软目标"，避免 OLS 极端外推。
public sealed class DampedTrendForecast : ForecastModelBase
{
    public double TfrDriftPerYear { get; init; } = -0.005;
    public double TfrFloor { get; init; } = 0.85;
    public double E0DriftPerYear { get; init; } = 0.12;
    public double E0Ceiling { get; init; } = 86.0;
    public double SrbTarget { get; init; } = 105.5;
    public double SrbHalfLifeYears { get; init; } = 18.0;
    public double MafmMaleDriftPerYear { get; init; } = 0.10;
    public double MafmFemaleDriftPerYear { get; init; } = 0.10;
    public double MafmMaleCeiling { get; init; } = 33.0;
    public double MafmFemaleCeiling { get; init; } = 31.0;
    public double MarriageRateTarget { get; init; } = 3.0;
    public double MarriageRateHalfLifeYears { get; init; } = 20.0;

    public override string Id => "damped-trend";
    public override string DisplayName => "阻尼趋势";
    public override string Description => "线性 + 指数收敛到预设目标 (floor / ceiling), 防极端外推";

    public override ForecastedScalars ProjectScalars(int year, ForecastContext ctx)
    {
        int dy = year - ctx.LastObservedYear;
        return new ForecastedScalars
        {
            Tfr = Math.Max(TfrFloor, ctx.LastTfr + TfrDriftPerYear * dy),
            Srb = SrbTarget + (ctx.LastSrb - SrbTarget) * Math.Exp(-Math.Log(2) / SrbHalfLifeYears * dy),
            MafmM = Math.Min(MafmMaleCeiling, ctx.LastMafmM + MafmMaleDriftPerYear * dy),
            MafmF = Math.Min(MafmFemaleCeiling, ctx.LastMafmF + MafmFemaleDriftPerYear * dy),
            E0M = Math.Min(E0Ceiling, ctx.LastE0M + E0DriftPerYear * dy),
            E0F = Math.Min(E0Ceiling, ctx.LastE0F + E0DriftPerYear * dy),
            MarriageRate = MarriageRateTarget + (ctx.LastMarriageRate - MarriageRateTarget) * Math.Exp(-Math.Log(2) / MarriageRateHalfLifeYears * dy),
        };
    }
}
