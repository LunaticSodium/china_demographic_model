using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// Cohort-Component Method (CCM) — 经典人口外推法。
/// 给定 year t 的金字塔 + year t 的输入向量，产出 year t+1 的金字塔。
/// 简化点：忽略国际迁移（中国年净迁移占比极低，对结构影响可忽略）。
public sealed class CohortComponentProjector
{
    public PopulationPyramid Project(PopulationPyramid start, DemographicInputs inputs)
    {
        if (inputs.Year != start.Year)
        {
            // 不要求严格相等，但记录一下
        }
        var next = new PopulationPyramid { Year = start.Year + 1 };
        int top = PopulationPyramid.MaxAge;

        // 1. 老化 + 存活
        for (int a = 0; a < top; a++)
        {
            double qM = Clamp01(inputs.MortalityMale[a]);
            double qF = Clamp01(inputs.MortalityFemale[a]);
            next.Male[a + 1] = start.Male[a] * (1 - qM);
            next.Female[a + 1] = start.Female[a] * (1 - qF);
        }
        // open-age bucket (top): 上一年顶段的幸存者累加到本年顶段（已在上面 a+1 写入 next[top]，再补本年 top 自身的存活）
        double qMTop = Clamp01(inputs.MortalityMale[top]);
        double qFTop = Clamp01(inputs.MortalityFemale[top]);
        next.Male[top] += start.Male[top] * (1 - qMTop);
        next.Female[top] += start.Female[top] * (1 - qFTop);

        // 2. 出生
        double births = inputs.TotalBirths;
        if (births <= 0)
        {
            // 由 ASFR × 育龄女性推导
            for (int a = 15; a <= 49 && a <= top; a++)
                births += start.Female[a] * inputs.AgeSpecificFertility[a];
        }
        double srb = inputs.SexRatioAtBirth <= 0 ? 105.0 : inputs.SexRatioAtBirth;
        double maleShare = srb / (100.0 + srb);
        double femaleShare = 100.0 / (100.0 + srb);

        // 婴儿存活率（粗略：1 - 0 岁死亡率）
        double q0M = Clamp01(inputs.MortalityMale[0]);
        double q0F = Clamp01(inputs.MortalityFemale[0]);
        next.Male[0] = births * maleShare * (1 - q0M);
        next.Female[0] = births * femaleShare * (1 - q0F);

        return next;
    }

    public IReadOnlyDictionary<int, PopulationPyramid> ProjectRange(
        PopulationPyramid start,
        IReadOnlyDictionary<int, DemographicInputs> inputsByYear,
        int yearFrom,
        int yearTo)
    {
        var result = new Dictionary<int, PopulationPyramid>();
        var cur = start;
        result[cur.Year] = cur;
        for (int y = yearFrom; y < yearTo; y++)
        {
            if (!inputsByYear.TryGetValue(y, out var inputs)) break;
            cur = Project(cur, inputs);
            result[cur.Year] = cur;
        }
        return result;
    }

    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
}
