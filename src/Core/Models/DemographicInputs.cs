namespace ChinaDemographicModel.Core.Models;

/// 单年的人口动力学输入向量。所有数组按年龄索引 0..MaxAge。
/// 锁定语义：在 LockToHistory 模式下，TotalBirths / SexRatioAtBirth 等观测量
/// 视为硬约束；其余（生育年龄结构、死亡率结构）会被 Calibrator 缩放以匹配观测总量。
public sealed class DemographicInputs
{
    public int Year { get; init; }

    /// 当年总出生人口（单位：人）。0 表示模型自洽推导（由 ASFR × 育龄女性）。
    public double TotalBirths { get; set; }

    /// 出生性别比，男婴 / 100 女婴。中国 1980s 后明显偏高。
    public double SexRatioAtBirth { get; set; } = 105.0;

    /// 各年龄一年内死亡概率（0..1）。
    public double[] MortalityMale { get; set; } = new double[PopulationPyramid.MaxAge + 1];
    public double[] MortalityFemale { get; set; } = new double[PopulationPyramid.MaxAge + 1];

    /// 各年龄一年内每女性的生育数（ASFR）。15..49 之外通常为 0。
    public double[] AgeSpecificFertility { get; set; } = new double[PopulationPyramid.MaxAge + 1];

    /// 粗结婚率（每千人）。
    public double CrudeMarriageRate { get; set; }

    /// 平均初婚年龄。
    public double MeanAgeFirstMarriageMale { get; set; }
    public double MeanAgeFirstMarriageFemale { get; set; }

    /// TFR (Total Fertility Rate) — sum of ASFR over reproductive ages.
    public double TotalFertilityRate => AgeSpecificFertility.Skip(15).Take(35).Sum();

    public DemographicInputs Clone() => new()
    {
        Year = Year,
        TotalBirths = TotalBirths,
        SexRatioAtBirth = SexRatioAtBirth,
        MortalityMale = (double[])MortalityMale.Clone(),
        MortalityFemale = (double[])MortalityFemale.Clone(),
        AgeSpecificFertility = (double[])AgeSpecificFertility.Clone(),
        CrudeMarriageRate = CrudeMarriageRate,
        MeanAgeFirstMarriageMale = MeanAgeFirstMarriageMale,
        MeanAgeFirstMarriageFemale = MeanAgeFirstMarriageFemale,
    };
}
