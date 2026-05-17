namespace ChinaDemographicModel.Core.Engine;

/// 末值常数预测：所有标量保持最后观测年的值。
/// 仍有 dynamics（cohort 衰减使出生数随 Female_15-49 减少而下降），但本身不假设任何趋势。
/// 作为零假设 baseline 比较其他模型偏离情况。
public sealed class ConstantLastForecast : ForecastModelBase
{
    public override string Id => "constant-last";
    public override string DisplayName => "末值常数";
    public override string Description => "所有变量 (TFR / SRB / MAFM / e0 / 婚率) 保持最后观测年的值";

    public override ForecastedScalars ProjectScalars(int year, ForecastContext ctx) =>
        new()
        {
            Tfr = ctx.LastTfr,
            Srb = ctx.LastSrb,
            MafmM = ctx.LastMafmM,
            MafmF = ctx.LastMafmF,
            E0M = ctx.LastE0M,
            E0F = ctx.LastE0F,
            MarriageRate = ctx.LastMarriageRate,
        };
}
