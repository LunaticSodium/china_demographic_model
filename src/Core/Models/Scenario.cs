namespace ChinaDemographicModel.Core.Models;

/// 一个反事实 / 预测场景。
/// Baseline：历史观测锁定 + 当前数据外推。
/// 用户可 clone Baseline 然后修改任意年的 Inputs，重新 Project，对比偏离历史的程度。
public sealed class Scenario
{
    public string Name { get; set; } = "Baseline";
    public bool LockToHistory { get; set; } = true;

    /// 起始金字塔（通常对应起始年的普查 / 估计）。
    public PopulationPyramid? Initial { get; set; }

    /// 各年输入（按年索引）。
    public Dictionary<int, DemographicInputs> InputsByYear { get; set; } = new();

    /// 模拟产出（每年的金字塔）。
    public Dictionary<int, PopulationPyramid> ProjectedByYear { get; set; } = new();

    /// 用户手动改过的年份（区别于 baseline 输入）。
    public HashSet<int> EditedYears { get; set; } = new();

    public Scenario CloneAs(string newName)
    {
        var s = new Scenario
        {
            Name = newName,
            LockToHistory = LockToHistory,
            Initial = Initial?.Clone(),
        };
        foreach (var (y, inp) in InputsByYear) s.InputsByYear[y] = inp.Clone();
        foreach (var (y, p) in ProjectedByYear) s.ProjectedByYear[y] = p.Clone();
        s.EditedYears = new HashSet<int>(EditedYears);
        return s;
    }
}
