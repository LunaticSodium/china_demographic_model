using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 默认死亡率表（粗略 — 用作 placeholder 直到从 life table CSV 加载）。
/// 形状：U 型，0 岁略高 + 老年指数增长。中国 2020 期望寿命约 78，量级匹配。
public static class DefaultMortalitySchedule
{
    public static double[] Male(double e0 = 75.0)
    {
        var q = new double[PopulationPyramid.MaxAge + 1];
        for (int a = 0; a <= PopulationPyramid.MaxAge; a++) q[a] = Hazard(a, e0, isMale: true);
        return q;
    }

    public static double[] Female(double e0 = 80.0)
    {
        var q = new double[PopulationPyramid.MaxAge + 1];
        for (int a = 0; a <= PopulationPyramid.MaxAge; a++) q[a] = Hazard(a, e0, isMale: false);
        return q;
    }

    private static double Hazard(int age, double e0, bool isMale)
    {
        // 极简 Gompertz-like
        double baseQ = age == 0 ? 0.006 : 0.0004;          // infant + child floor
        double maleAdj = isMale ? 1.15 : 1.0;
        double aging = Math.Exp(0.085 * (age - 30)) - 1;   // 老龄 hazard 指数项
        aging = Math.Max(0, aging) * 0.00025;
        double scale = 80.0 / Math.Max(40, e0);            // e0 越高，整体压低
        double q = (baseQ + aging) * maleAdj * scale;
        return Math.Min(0.5, q);
    }
}
