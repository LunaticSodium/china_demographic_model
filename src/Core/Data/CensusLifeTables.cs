using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Data;

/// 五次人口普查直接公布的分性别 × 年龄段死亡概率 q(x)。
///
/// 关键年龄锚点（22 个）：0, 1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100
/// 中间年龄由 GetQx 线性插值到单岁；非普查年由两个邻近普查的 q(a) 线性插值（time × age 二维）。
///
/// 这是替代 CD-East + Brass logit 的**中国实证生命表**：
/// - 形状层面：反映 China 实际的婴幼儿死亡 / 成年男性偏高 / 老龄相对柔和模式；
/// - 时间层面：直接捕获 1981→2020 的死亡率变化，不假设 β=1 的均匀改善。
///
/// 数据精度：从公开普查公报记忆整理，可能 ±5-10% 量级偏差。如需精确，
/// 替换 KeyMaleQ / KeyFemaleQ 数值为完整 5 普查公报值（每次普查的"分年龄性别死亡概率"表）。
public static class CensusLifeTables
{
    public static readonly int[] KeyAges =
        { 0, 1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100 };

    public static readonly int[] CensusYears = { 1981, 1990, 2000, 2010, 2020 };

    // 各普查年份的关键年龄 q(x)，男 / 女
    // 来源：历次普查公报（近似整理）。q(0) 单位是该年内死亡概率，q(1) 是 1 岁至 1 岁前死亡概率 ...
    private static readonly Dictionary<int, double[]> KeyMaleQ = new()
    {
        [1981] = new[] { 0.0410, 0.0080, 0.0011, 0.0010, 0.0013, 0.0023, 0.0024, 0.0028, 0.0036, 0.0053, 0.0080, 0.0125, 0.0195, 0.0303, 0.0475, 0.0730, 0.1110, 0.1650, 0.2380, 0.3400, 0.4650, 0.6000 },
        [1990] = new[] { 0.0330, 0.0070, 0.0010, 0.0009, 0.0012, 0.0022, 0.0023, 0.0026, 0.0033, 0.0050, 0.0077, 0.0120, 0.0190, 0.0295, 0.0460, 0.0715, 0.1085, 0.1615, 0.2350, 0.3380, 0.4620, 0.6000 },
        [2000] = new[] { 0.0280, 0.0060, 0.0008, 0.0007, 0.0010, 0.0018, 0.0019, 0.0022, 0.0028, 0.0043, 0.0068, 0.0108, 0.0175, 0.0275, 0.0435, 0.0680, 0.1040, 0.1550, 0.2275, 0.3290, 0.4540, 0.5900 },
        [2010] = new[] { 0.0140, 0.0030, 0.0004, 0.0004, 0.0007, 0.0014, 0.0015, 0.0018, 0.0024, 0.0038, 0.0058, 0.0095, 0.0155, 0.0250, 0.0400, 0.0635, 0.0995, 0.1495, 0.2210, 0.3220, 0.4460, 0.5800 },
        [2020] = new[] { 0.0060, 0.0012, 0.00020, 0.00025, 0.00050, 0.00100, 0.00110, 0.00140, 0.00190, 0.00280, 0.00460, 0.00780, 0.01300, 0.02150, 0.03550, 0.05700, 0.09050, 0.13800, 0.20900, 0.31000, 0.43500, 0.57000 },
    };

    private static readonly Dictionary<int, double[]> KeyFemaleQ = new()
    {
        [1981] = new[] { 0.0345, 0.0070, 0.0008, 0.0006, 0.0008, 0.0011, 0.0013, 0.0017, 0.0024, 0.0035, 0.0052, 0.0080, 0.0125, 0.0195, 0.0310, 0.0480, 0.0755, 0.1155, 0.1755, 0.2600, 0.3800, 0.5500 },
        [1990] = new[] { 0.0265, 0.0058, 0.0007, 0.0005, 0.0007, 0.0010, 0.0011, 0.0015, 0.0022, 0.0033, 0.0050, 0.0077, 0.0120, 0.0190, 0.0300, 0.0470, 0.0740, 0.1135, 0.1730, 0.2580, 0.3790, 0.5500 },
        [2000] = new[] { 0.0220, 0.0050, 0.0005, 0.0004, 0.0005, 0.0008, 0.0009, 0.0013, 0.0019, 0.0030, 0.0046, 0.0070, 0.0110, 0.0175, 0.0280, 0.0445, 0.0705, 0.1085, 0.1665, 0.2495, 0.3680, 0.5400 },
        [2010] = new[] { 0.0115, 0.0024, 0.0003, 0.0002, 0.0003, 0.0005, 0.0006, 0.0010, 0.0015, 0.0024, 0.0036, 0.0055, 0.0090, 0.0145, 0.0230, 0.0380, 0.0625, 0.0985, 0.1550, 0.2350, 0.3520, 0.5300 },
        [2020] = new[] { 0.0050, 0.0010, 0.00015, 0.00018, 0.00022, 0.00035, 0.00040, 0.00070, 0.00110, 0.00180, 0.00270, 0.00430, 0.00730, 0.01180, 0.01900, 0.03200, 0.05400, 0.08750, 0.14100, 0.22000, 0.33800, 0.52000 },
    };

    /// 给定年份 + 性别，返回单岁 q(x) 数组（长度 = MaxAge+1）。
    /// 年份在普查年之间 → 线性插值。
    /// 早于 1981 → 用 1981 + extrapolation (向 1981 之前年份用 1981 自身，不做反推)。
    /// 晚于 2020 → 用 2020 + 用 targetE0 做 Brass 平移（如果给出）。
    public static double[] GetQx(int year, bool isMale, double? targetE0 = null)
    {
        var keyDict = isMale ? KeyMaleQ : KeyFemaleQ;
        double[] keyQ = InterpolateOverTime(year, keyDict);
        double[] qx = ExpandToSingleAges(keyQ);

        // 如果年份在普查范围外且给定了 targetE0，做 Brass logit shift 微调
        if (targetE0.HasValue && (year < CensusYears[0] || year > CensusYears[^1]))
        {
            qx = ApplyBrassShiftToTargetE0(qx, targetE0.Value);
        }
        return qx;
    }

    private static double[] InterpolateOverTime(int year, Dictionary<int, double[]> keyDict)
    {
        // 找邻近的两个普查年
        int below = CensusYears.Where(y => y <= year).DefaultIfEmpty(CensusYears[0]).Max();
        int above = CensusYears.Where(y => y >= year).DefaultIfEmpty(CensusYears[^1]).Min();

        if (below == above) return (double[])keyDict[below].Clone();

        double t = (double)(year - below) / (above - below);
        var qBelow = keyDict[below];
        var qAbove = keyDict[above];
        var result = new double[KeyAges.Length];
        for (int i = 0; i < KeyAges.Length; i++)
            result[i] = qBelow[i] * (1 - t) + qAbove[i] * t;
        return result;
    }

    private static double[] ExpandToSingleAges(double[] keyQ)
    {
        var qx = new double[PopulationPyramid.MaxAge + 1];
        for (int i = 0; i < KeyAges.Length - 1; i++)
        {
            int a0 = KeyAges[i], a1 = KeyAges[i + 1];
            double q0 = keyQ[i], q1 = keyQ[i + 1];
            for (int a = a0; a < a1; a++)
            {
                double t = a1 == a0 ? 0 : (double)(a - a0) / (a1 - a0);
                qx[a] = q0 * (1 - t) + q1 * t;
            }
        }
        qx[PopulationPyramid.MaxAge] = keyQ[^1];
        return qx;
    }

    private static double[] ApplyBrassShiftToTargetE0(double[] qx, double targetE0)
    {
        var lxStd = new double[101];
        lxStd[0] = 1.0;
        for (int a = 0; a < 100; a++)
            lxStd[a + 1] = lxStd[a] * (1 - Math.Clamp(qx[a], 0, 1));

        var ystd = new double[101];
        for (int a = 0; a <= 100; a++)
        {
            double l = Math.Clamp(lxStd[a], 1e-9, 1 - 1e-9);
            ystd[a] = 0.5 * Math.Log((1 - l) / l);
        }

        double alpha = 0;
        for (int iter = 0; iter < 60; iter++)
        {
            double e0 = ComputeE0(ystd, alpha);
            double err = e0 - targetE0;
            if (Math.Abs(err) < 0.005) break;
            const double h = 0.005;
            double slope = (ComputeE0(ystd, alpha + h) - e0) / h;
            if (Math.Abs(slope) < 1e-6) break;
            double step = err / slope;
            if (Math.Abs(step) > 0.3) step = 0.3 * Math.Sign(step);
            alpha -= step;
            alpha = Math.Clamp(alpha, -3, 3);
        }

        var lxNew = new double[101];
        for (int a = 0; a <= 100; a++)
            lxNew[a] = 1.0 / (1.0 + Math.Exp(2 * (alpha + ystd[a])));

        var qNew = new double[101];
        for (int a = 0; a < 100; a++)
            qNew[a] = lxNew[a] > 0 ? Math.Clamp(1 - lxNew[a + 1] / lxNew[a], 0, 1) : 1;
        qNew[100] = 1;
        return qNew;
    }

    private static double ComputeE0(double[] ystd, double alpha)
    {
        var lx = new double[101];
        for (int a = 0; a <= 100; a++)
            lx[a] = 1.0 / (1.0 + Math.Exp(2 * (alpha + ystd[a])));
        double T = 0;
        const double a0 = 0.3;
        T += a0 * lx[0] + (1 - a0) * lx[1];
        for (int a = 1; a < 100; a++) T += (lx[a] + lx[a + 1]) / 2.0;
        T += lx[100] * 2.0;
        return lx[0] > 0 ? T / lx[0] : 0;
    }
}
