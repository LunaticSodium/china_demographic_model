using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 把"总和生育率 TFR + 平均初婚年龄(女) + 离散度"翻译成 ASFR(年龄 15..49)。
/// 极简模型：以 (MAFM_f + 初婚至首育间隔) 为均值，σ=SigmaYears 的正态形分布，
/// 然后整体归一到 TFR。这一步是 Stage 2/3 的可替换接口 —— 真实情况下应分阶序数生育。
public sealed class FertilityModel
{
    public double SigmaYears { get; init; } = 4.0;
    public double MeanFirstBirthLag { get; init; } = 1.5;

    public double[] BuildAgeSpecificFertility(double tfr, double meanAgeFirstMarriageFemale)
    {
        double mean = meanAgeFirstMarriageFemale + MeanFirstBirthLag;
        var asfr = new double[PopulationPyramid.MaxAge + 1];
        double sum = 0;
        for (int a = 15; a <= 49; a++)
        {
            double z = (a - mean) / SigmaYears;
            double w = Math.Exp(-0.5 * z * z);
            asfr[a] = w;
            sum += w;
        }
        if (sum <= 0) return asfr;
        for (int a = 15; a <= 49; a++)
            asfr[a] = asfr[a] / sum * tfr;
        return asfr;
    }
}
