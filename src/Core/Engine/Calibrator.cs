using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// 历史观测对齐器。
/// 给定观测总出生数 → 整体缩放 ASFR 使模型预测 == 观测；
/// 给定普查年龄结构 → 对当年金字塔做形状对齐（最小二乘投影；这里 stub 为 ratio 拉齐）。
/// 任何"篡改"在 LockToHistory=true 时都会被这里强制回退到观测值。
public sealed class Calibrator
{
    public void AlignBirthsToHistory(DemographicInputs inputs, double observedBirths, PopulationPyramid start)
    {
        if (observedBirths <= 0) return;
        double predicted = 0;
        for (int a = 15; a <= 49 && a <= PopulationPyramid.MaxAge; a++)
            predicted += start.Female[a] * inputs.AgeSpecificFertility[a];
        if (predicted <= 0)
        {
            // 无 ASFR 信号：直接 set total
            inputs.TotalBirths = observedBirths;
            return;
        }
        double scale = observedBirths / predicted;
        for (int a = 15; a <= 49 && a <= PopulationPyramid.MaxAge; a++)
            inputs.AgeSpecificFertility[a] *= scale;
        inputs.TotalBirths = observedBirths;
    }

    /// 朴素的形状对齐：把 current 金字塔按年龄段比例拉到 observed。
    /// 真实场景应该用 IPF / 最小二乘 + 平滑约束，先 stub。
    public PopulationPyramid AlignPyramidToCensus(PopulationPyramid current, PopulationPyramid observed)
    {
        var aligned = new PopulationPyramid { Year = current.Year };
        for (int a = 0; a <= PopulationPyramid.MaxAge; a++)
        {
            aligned.Male[a] = observed.Male[a];
            aligned.Female[a] = observed.Female[a];
        }
        return aligned;
    }
}
