namespace ChinaDemographicModel.Core.Engine;

/// Coale-Demeny East-family life table，用 Brass logit (β=1) 从 e0 标准表
/// 平移到目标 e0。
///
/// 参考：
/// - Coale, A. J., & Demeny, P. (1983). Regional Model Life Tables and Stable Populations.
/// - Brass, W. (1971). On the scale of mortality.
///
/// 实现思路：
/// 1. 内置 CD-East-like 标准 q(x) 表（关键年龄点 + 线性插值到 0..100 单岁）。
/// 2. 从标准 q(x) 反推 l(x)，对每个 l(x) 取 logit Y(x) = 0.5*ln((1-l)/l)。
/// 3. Newton 迭代解 α 使 logit 平移后的生命表 e0 等于目标 e0。
/// 4. 还原 q(x) = 1 - l(x+1)/l(x)。
///
/// 标准 q(x) 的 e0 约为 70（女） / 66.5（男），近似对应 CD-East level 22-23。
public static class CoaleDemenyLifeTable
{
    // 关键年龄 q(x) —— Coale-Demeny East family 近似（女）
    private static readonly (int Age, double Q)[] StdFemaleKey =
    {
        (0, 0.040), (1, 0.010), (2, 0.005), (3, 0.003), (4, 0.002),
        (5, 0.0010), (10, 0.0008), (15, 0.0010), (20, 0.0012), (25, 0.0013),
        (30, 0.0016), (35, 0.0022), (40, 0.0032), (45, 0.0050), (50, 0.0080),
        (55, 0.0125), (60, 0.0195), (65, 0.0305), (70, 0.0480), (75, 0.0755),
        (80, 0.1155), (85, 0.1755), (90, 0.2605), (95, 0.3805), (100, 0.5500),
    };

    // 关键年龄 q(x) —— Coale-Demeny East family 近似（男）；年轻人 +"事故峰"
    private static readonly (int Age, double Q)[] StdMaleKey =
    {
        (0, 0.045), (1, 0.0115), (2, 0.0058), (3, 0.0035), (4, 0.0023),
        (5, 0.0012), (10, 0.0009), (15, 0.0014), (20, 0.0020), (25, 0.0023),
        (30, 0.0026), (35, 0.0035), (40, 0.0055), (45, 0.0085), (50, 0.0130),
        (55, 0.0200), (60, 0.0305), (65, 0.0465), (70, 0.0710), (75, 0.1075),
        (80, 0.1605), (85, 0.2305), (90, 0.3305), (95, 0.4505), (100, 0.5750),
    };

    public static double[] BuildQx(double targetE0, bool isMale)
    {
        var stdQx = InterpolateQx(isMale ? StdMaleKey : StdFemaleKey);
        var stdLx = LxFromQx(stdQx);

        // logit of standard l(x)
        var ystd = new double[101];
        for (int a = 0; a <= 100; a++)
        {
            double l = Math.Clamp(stdLx[a], 1e-9, 1 - 1e-9);
            ystd[a] = 0.5 * Math.Log((1 - l) / l);
        }

        // Newton iteration: solve e0(α) == targetE0
        double alpha = 0;
        for (int iter = 0; iter < 60; iter++)
        {
            double e0Now = ComputeE0FromLogitShift(ystd, alpha);
            double err = e0Now - targetE0;
            if (Math.Abs(err) < 0.005) break;
            const double h = 0.005;
            double e0H = ComputeE0FromLogitShift(ystd, alpha + h);
            double slope = (e0H - e0Now) / h;
            if (Math.Abs(slope) < 1e-6) break;
            double step = err / slope;
            // damp + clamp
            if (Math.Abs(step) > 0.3) step = 0.3 * Math.Sign(step);
            alpha -= step;
            alpha = Math.Clamp(alpha, -3.0, 3.0);
        }

        // build new l(x) and q(x)
        var lxNew = new double[101];
        for (int a = 0; a <= 100; a++)
            lxNew[a] = 1.0 / (1.0 + Math.Exp(2 * (alpha + ystd[a])));

        var qx = new double[101];
        for (int a = 0; a < 100; a++)
        {
            if (lxNew[a] <= 0) { qx[a] = 1.0; continue; }
            double q = 1.0 - lxNew[a + 1] / lxNew[a];
            qx[a] = Math.Clamp(q, 0, 1);
        }
        qx[100] = 1.0;
        return qx;
    }

    /// 同时返回 q(x) 与达成的 e0（供 UI 验证）。
    public static (double[] Qx, double AchievedE0) BuildQxWithE0Check(double targetE0, bool isMale)
    {
        var qx = BuildQx(targetE0, isMale);
        var lx = LxFromQx(qx);
        var ystd = new double[101];
        for (int a = 0; a <= 100; a++)
        {
            double l = Math.Clamp(lx[a], 1e-9, 1 - 1e-9);
            ystd[a] = 0.5 * Math.Log((1 - l) / l);
        }
        double achieved = ComputeE0FromLogitShift(ystd, 0);
        return (qx, achieved);
    }

    private static double[] InterpolateQx((int Age, double Q)[] key)
    {
        var qx = new double[101];
        for (int i = 0; i < key.Length - 1; i++)
        {
            int a0 = key[i].Age, a1 = key[i + 1].Age;
            double q0 = key[i].Q, q1 = key[i + 1].Q;
            for (int a = a0; a < a1; a++)
            {
                double t = a1 == a0 ? 0 : (double)(a - a0) / (a1 - a0);
                qx[a] = q0 * (1 - t) + q1 * t;
            }
        }
        qx[100] = key[^1].Q;
        return qx;
    }

    private static double[] LxFromQx(double[] qx)
    {
        var lx = new double[101];
        lx[0] = 1.0;
        for (int a = 0; a < 100; a++)
            lx[a + 1] = lx[a] * (1 - Math.Min(1, Math.Max(0, qx[a])));
        return lx;
    }

    private static double ComputeE0FromLogitShift(double[] ystd, double alpha)
    {
        var lx = new double[101];
        for (int a = 0; a <= 100; a++)
            lx[a] = 1.0 / (1.0 + Math.Exp(2 * (alpha + ystd[a])));

        // T0 = ∫ l(x) dx ≈ Σ L_x，其中 L_x = (l_x + l_{x+1}) / 2，
        // L_0 用 Coale a0 = 0.3 校正婴儿不均匀。
        double T = 0;
        const double a0 = 0.3;
        T += a0 * lx[0] + (1 - a0) * lx[1];
        for (int a = 1; a < 100; a++)
            T += (lx[a] + lx[a + 1]) / 2.0;
        // 开放年龄段：l(100) × 平均剩余生存（粗估 ≈ 2 年）
        T += lx[100] * 2.0;
        return lx[0] > 0 ? T / lx[0] : 0;
    }
}
