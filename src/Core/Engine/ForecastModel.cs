using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 预测年（> LastObservedYear）的输入投影模型。
///
/// 每个 demographic input 分别有自己的轨迹假设：
/// - TFR: 假设末观测值持续（默认 1.0 for 2024）+ 缓慢漂移; clamped 至 floor。
/// - e0: 持续上升趋势 + 上限封顶（人类寿命极限不超过 ~95）。
/// - SRB: 指数收敛至理论生物正常 ~105.5（女婴瞒报激励消失 + 重男轻女观念松动）。
/// - MAFM: 持续上升 + 上限（人口学经验值 ~32 男 / ~30 女）。
/// - 粗结婚率: 指数收敛至底线（参考东亚低生育社会 ~2-3‰）。
///
/// 关键的预测**信号传递**：本模型只设置每年的 rate / 标量；
/// 出生总数由 CCM 自动从 ASFR × 当年女性 15-49 cohort 派生
/// （inp.TotalBirths = 0 触发 CCM 内部 fallback）。
/// 这样**人口动力学衰减**会自然进入预测——1990s-2010s 出生人口缩小队列年年长成 15-49 育龄段，
/// 即使 TFR 不变，每年出生数也会下降。
///
/// 参数都可以通过 init 修改；默认值是基于 2020-2025 趋势的保守估计。
public sealed class ForecastModel
{
    // TFR
    public double TfrAt2024 { get; init; } = 1.00;     // 假设 2024 period TFR ≈ 1.0
    public double TfrDriftPerYear { get; init; } = -0.005;  // 缓慢下行
    public double TfrFloor { get; init; } = 0.85;     // 不低于此

    // e0
    public double E0MaleAt2024 { get; init; } = 75.9;
    public double E0FemaleAt2024 { get; init; } = 81.5;
    public double E0DriftPerYear { get; init; } = 0.12;  // 寿命继续上升 ~0.1 年/年
    public double E0Ceiling { get; init; } = 86.0;       // 上限

    // SRB
    public double SrbTarget { get; init; } = 105.5;
    public double SrbHalfLifeYears { get; init; } = 18.0;

    // MAFM
    public double MafmMaleDriftPerYear { get; init; } = 0.10;
    public double MafmFemaleDriftPerYear { get; init; } = 0.10;
    public double MafmMaleCeiling { get; init; } = 33.0;
    public double MafmFemaleCeiling { get; init; } = 31.0;

    // 粗结婚率
    public double MarriageRateTarget { get; init; } = 3.0;
    public double MarriageRateHalfLifeYears { get; init; } = 20.0;

    public double ProjectTfr(int year, int lastObservedYear)
    {
        int dy = year - lastObservedYear;
        return Math.Max(TfrFloor, TfrAt2024 + TfrDriftPerYear * dy);
    }

    public double ProjectE0(int year, int lastObservedYear, bool isMale)
    {
        int dy = year - lastObservedYear;
        double last = isMale ? E0MaleAt2024 : E0FemaleAt2024;
        return Math.Min(E0Ceiling, last + E0DriftPerYear * dy);
    }

    public double ProjectSrb(int year, int lastObservedYear, double lastSrb)
    {
        int dy = year - lastObservedYear;
        double k = Math.Log(2) / SrbHalfLifeYears;
        return SrbTarget + (lastSrb - SrbTarget) * Math.Exp(-k * dy);
    }

    public double ProjectMafmMale(int year, int lastObservedYear, double lastMafm)
    {
        int dy = year - lastObservedYear;
        return Math.Min(MafmMaleCeiling, lastMafm + MafmMaleDriftPerYear * dy);
    }

    public double ProjectMafmFemale(int year, int lastObservedYear, double lastMafm)
    {
        int dy = year - lastObservedYear;
        return Math.Min(MafmFemaleCeiling, lastMafm + MafmFemaleDriftPerYear * dy);
    }

    public double ProjectMarriageRate(int year, int lastObservedYear, double lastRate)
    {
        int dy = year - lastObservedYear;
        double k = Math.Log(2) / MarriageRateHalfLifeYears;
        return MarriageRateTarget + (lastRate - MarriageRateTarget) * Math.Exp(-k * dy);
    }
}
