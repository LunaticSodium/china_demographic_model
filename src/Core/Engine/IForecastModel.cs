using ChinaDemographicModel.Core.Data;
using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 预测模型接口。
/// 关键架构原则（用户 round 6/7）：模型在输入之后应用，输入作为数据存在而不影响模型数学结构。
/// 用户切换模型 → InputsByYear 观测年部分不变 → 仅 forecast 年部分由 model 重新产生。
public interface IForecastModel
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }

    DemographicInputs Project(int year, ForecastContext ctx);

    /// 公开标量层级 —— UI 显示预测年指标时直接用 (不必经 CCM)
    ForecastedScalars ProjectScalars(int year, ForecastContext ctx);
}

/// 预测模型只投影标量，由基类负责组装成 DemographicInputs（ASFR / mortality 数组）。
public sealed class ForecastedScalars
{
    public double Tfr { get; init; }
    public double Srb { get; init; }
    public double MafmM { get; init; }
    public double MafmF { get; init; }
    public double E0M { get; init; }
    public double E0F { get; init; }
    public double MarriageRate { get; init; }
}

public sealed class ForecastContext
{
    public int LastObservedYear { get; init; }
    public double LastTfr { get; init; }
    public double LastSrb { get; init; }
    public double LastMafmM { get; init; }
    public double LastMafmF { get; init; }
    public double LastE0M { get; init; }
    public double LastE0F { get; init; }
    public double LastMarriageRate { get; init; }
    public HistoricalSeries Historical { get; init; } = new();
    public FertilityModel Fertility { get; init; } = new();
}

public abstract class ForecastModelBase : IForecastModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }

    public abstract ForecastedScalars ProjectScalars(int year, ForecastContext ctx);

    public DemographicInputs Project(int year, ForecastContext ctx)
    {
        var s = ProjectScalars(year, ctx);
        var inp = new DemographicInputs
        {
            Year = year,
            TotalBirths = 0,   // 0 = CCM 从 ASFR × Female_15-49 派生
            SexRatioAtBirth = s.Srb,
            CrudeMarriageRate = s.MarriageRate,
            MeanAgeFirstMarriageMale = s.MafmM,
            MeanAgeFirstMarriageFemale = s.MafmF,
            MortalityMale = CensusLifeTables.GetQx(year, isMale: true, targetE0: s.E0M),
            MortalityFemale = CensusLifeTables.GetQx(year, isMale: false, targetE0: s.E0F),
        };
        inp.AgeSpecificFertility = ctx.Fertility.BuildAgeSpecificFertility(s.Tfr, s.MafmF);
        return inp;
    }
}
