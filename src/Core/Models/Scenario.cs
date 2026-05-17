namespace ChinaDemographicModel.Core.Models;

/// 一个场景（含观测数据 + 反事实编辑 + 预测模型选择）。
/// - InputsByYear: 只持有**观测年**的输入（1978-LastObservedYear）+ 用户已编辑的反事实年。
/// - ForecastModelId: 选定的预测模型 Id（由 ScenarioBuilder/RunProjection 解析为 IForecastModel）。
///   预测年的 inputs 在 RunProjection 时由 model.Project() 即时生成，不写入 InputsByYear。
public sealed class Scenario
{
    public string Name { get; set; } = "Baseline";
    public bool LockToHistory { get; set; } = true;

    /// 起始金字塔（通常对应起始年的普查 / 估计）。
    public PopulationPyramid? Initial { get; set; }

    public Dictionary<int, DemographicInputs> InputsByYear { get; set; } = new();
    public Dictionary<int, PopulationPyramid> ProjectedByYear { get; set; } = new();
    public HashSet<int> EditedYears { get; set; } = new();

    /// 选定的预测模型 Id。默认 "ols-trend"。
    public string ForecastModelId { get; set; } = "ols-trend";

    public Scenario CloneAs(string newName)
    {
        var s = new Scenario
        {
            Name = newName,
            LockToHistory = LockToHistory,
            Initial = Initial?.Clone(),
            ForecastModelId = ForecastModelId,
        };
        foreach (var (y, inp) in InputsByYear) s.InputsByYear[y] = inp.Clone();
        foreach (var (y, p) in ProjectedByYear) s.ProjectedByYear[y] = p.Clone();
        s.EditedYears = new HashSet<int>(EditedYears);
        return s;
    }
}
