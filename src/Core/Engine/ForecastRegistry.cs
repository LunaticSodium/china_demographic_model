namespace ChinaDemographicModel.Core.Engine;

/// 预测模型注册表。UI 列出 AllModels；Scenario.ForecastModelId 通过 Resolve() 取实例。
public static class ForecastRegistry
{
    public static readonly IReadOnlyList<IForecastModel> AllModels = new IForecastModel[]
    {
        new OlsTrendForecast(),
        new ConstantLastForecast(),
        new DampedTrendForecast(),
        // round 8+ 待加：UserFixedForecast, WppMediumVariantForecast, PadisStyleForecast
    };

    public static IForecastModel Resolve(string id) =>
        AllModels.FirstOrDefault(m => m.Id == id) ?? AllModels[0];
}
