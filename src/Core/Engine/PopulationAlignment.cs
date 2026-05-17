using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Engine;

/// **官方数据口径修正函数 / Official-Data Methodology Alignment**
///
/// 设计立场（参 PHILOSOPHY.md "数值采纳 / 口径修正" 一节）：
/// - 对中国官方人口 / 出生 / 死亡 / 普查等数值，**全盘采纳**——
///   没有可比可信度的替代源；独立估计的方差远大于 NBS 公布。
/// - 但 NBS "年末总人口估计" 和 "普查时点人口" 是**两个不同口径**：
///   - 年中估计：基于公安户籍 + 抽样调查；
///   - 普查时点：基于实地登记 + 调整。
///   两者在普查年通常有 0.5–3% 的差距（普查发现年中估计往往偏低）。
///
/// 本类提供**显式命名的口径修正**：
/// - 输入：模型 CCM 投影产出的金字塔总和（initial=1982 普查 + 历年生育死亡推演而来）
/// - 输出：与"NBS 年末总人口"口径对齐的金字塔（按年度按比例缩放）
///
/// 修正函数本身是简单的标量缩放——不改变年龄结构形状，只对齐总量。
/// 形状对齐由 Calibrator.AlignPyramidToCensus 在普查年完成。
///
/// 这个修正必须**显式存在**：UI 上展示的"总人口"应当是修正后的；
/// MainViewModel 的 TotalPopulationDisplay 须使用本类。
public sealed class PopulationAlignment
{
    /// 给定年份和模型金字塔，返回按 NBS 年末人口对齐的新金字塔。
    /// 不在 nbsYearEndByYear 范围内的年份 → 返回原金字塔不变（标志 wasCorrected=false）。
    public static (PopulationPyramid Pyramid, bool WasCorrected, double Factor) AlignToNbsYearEnd(
        PopulationPyramid model,
        IReadOnlyDictionary<int, double> nbsYearEndByYear)
    {
        if (!nbsYearEndByYear.TryGetValue(model.Year, out double target))
            return (model, false, 1.0);
        double current = model.Total;
        if (current <= 0) return (model, false, 1.0);
        double factor = target / current;

        var aligned = new PopulationPyramid { Year = model.Year };
        for (int a = 0; a <= PopulationPyramid.MaxAge; a++)
        {
            aligned.Male[a] = model.Male[a] * factor;
            aligned.Female[a] = model.Female[a] * factor;
        }
        return (aligned, true, factor);
    }

    /// 给定一系列模型金字塔 + NBS 序列，批量对齐。
    /// 不修改原 dict 引用；返回新 dict。
    public static Dictionary<int, PopulationPyramid> AlignBatch(
        IReadOnlyDictionary<int, PopulationPyramid> modelByYear,
        IReadOnlyDictionary<int, double> nbsYearEndByYear)
    {
        var result = new Dictionary<int, PopulationPyramid>();
        foreach (var (y, p) in modelByYear)
        {
            var (aligned, _, _) = AlignToNbsYearEnd(p, nbsYearEndByYear);
            result[y] = aligned;
        }
        return result;
    }
}
