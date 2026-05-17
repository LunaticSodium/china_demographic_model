# 预测模型架构与候选

用户 round 6 / 7 方向（综合）：
> 模型在输入之后应用，输入作为数据存在而不影响模型的数学结构。
> 插值应当是我们的主要策略。
> 1-2 个国际通用模型 + 1 个中国本土模型 + 我们的单固定模型。
> [Lee-Carter 撤出]：随机方法是为了 PI，我们只要中位数；改善模式插值已能捕获。

## 1. 架构（不变）

```
DemographicInputs (data)  →  IForecastModel.Project(year, ctx)  →  Inputs for forecast year
       ↑                              ↑
   用户编辑                        用户在 Model 标签换选
   (不改模型结构)                  (不改输入数据)
```

`Scenario.InputsByYear` 只持有观测年的原始 inputs；预测年的 inputs 在 RunProjection 时由 `IForecastModel.Project(year, ctx)` 即时产生。

## 2. 候选模型（修订）— 全确定性

策略基调：**对历史已观测的标量做线性 / 加权插值，对未来年份用同样思路向前外推**（带边界 clamp）。

### 2.1 末值常数 (`ConstantLastForecast`)

最简单的零假设：所有变量保持最后观测年的值。

```
TFR(t)  = TFR(LastObs)
e0(t)   = e0(LastObs)
SRB(t)  = SRB(LastObs)
MAFM(t) = MAFM(LastObs)
婚率(t) = 婚率(LastObs)
```

仍然有 dynamics（cohort 衰减 → 出生数随 Female_15-49 缩小而自然下降）；本身不假设任何趋势。**作为零假设 baseline**。

### 2.2 OLS 趋势外推 (`OlsTrendForecast`) — 主力模型

对每个标量取末观测窗口（默认 5-10 年），最小二乘拟合线性趋势，外推到目标年；用上下限 clamp 防止失控。

```
slope_x = OLS(year, x_value | last N years)
intercept_x = mean(x) - slope_x · mean(year)
x(t) = clamp(intercept_x + slope_x · t, floor_x, ceiling_x)
```

clamp 边界：
- TFR ∈ [0.7, 6.0]
- e0 ∈ [60, 90]
- SRB ∈ [102, 130]
- MAFM ∈ [18, 40]
- 婚率 ∈ [1, 15]

特点：完全由数据驱动，可解释，确定性。直接体现用户"插值为主"原则（外推 = 拟合直线 → 等价于扩展插值到外侧）。

### 2.3 阻尼趋势 (`DampedTrendForecast`) — 原 ForecastModel

各变量按指数 / 线性带 floor / ceiling 的轨迹收敛。是 OLS 趋势的"软化"——避免极端外推。round 6 已实装。

参数化（默认值见 round 6 commit）：
```
TFR(t)  = max(0.85, TFR(LastObs) - 0.005·Δt)
e0(t)   = min(86, e0(LastObs) + 0.12·Δt)
SRB(t)  = 105.5 + (SRB(LastObs) - 105.5)·exp(-ln2/18 · Δt)
MAFM(t) = min(33/31, MAFM(LastObs) + 0.10·Δt)
婚率(t) = 3.0 + (婚率(LastObs) - 3.0)·exp(-ln2/20 · Δt)
```

### 2.4 用户固定 (`UserFixedForecast`) — 单值固定泛化

用户选择**一个标量锁定**到指定值，其余按 OLS 趋势外推。等价于约束情景规划——"如果 TFR 长期固定在 1.2，会怎样"等问题。

```
若用户选定 (var, value):
  var(t) = value (所有 t > LastObs)
其余变量(t) = OlsTrendForecast(...)
```

可锁定列表：TFR / e0 / SRB / MAFM / 婚率 / **婚后生育数**（= TFR / 已婚率）。

### 2.5 UN WPP 中变体 (`WppMediumVariantForecast`)

从 UN WPP 2024 的 China 中变体导入年度预测值（TFR / e0 / SRB）。本身是 Bayesian hierarchical 推断的产物，但**只取中位数**——对我们而言它是一组确定性年度值。

数据需求：`data/seed/wpp_2024_china_medium.csv`（2025-2100 年度）— round 8 补。

### 2.6 PADIS-INT 风格 (`PadisStyleForecast`)

CPDRC 自主软件的简化复刻：
- 城乡分层（农村 TFR ~1.3，城镇 ~0.9，按城镇化率加权合成全国 TFR）
- 政策弹性参数（生育政策松紧 → TFR 弹性）

数据需求：分年城镇化率（NBS 有）、城乡 TFR（七普公布）。round 8 补。

## 3. Round 7 实装范围

| 模型 | 状态 |
|---|---|
| ConstantLastForecast | ✓ 实装 |
| OlsTrendForecast | ✓ 实装（主力）|
| DampedTrendForecast | ✓ refactor 现有 ForecastModel |
| UserFixedForecast | ✓ 实装 |
| WppMediumVariantForecast | round 8（需 CSV）|
| PadisStyleForecast | round 8（需城乡数据）|

UI: 左栏新增"预测模型"卡片含 RadioButton 选择 + 模型特有参数面板（UserFixed 含 ComboBox 选锁定变量 + 滑条设值）。

CitationText 预测年自动反映当前选用模型的公式。

## 4. 数据 / 模型解耦的具体改动

之前：`ScenarioBuilder.BuildBaseline` 内部分支 `isForecast` ，硬填预测年 inputs。
之后：
- `BuildBaseline` 只构造**观测年**的 inputs；预测年留空
- `Scenario.ForecastModel` 字段持有 `IForecastModel`
- `MainViewModel.RunProjectionForScenario` 循环到预测年时：
  ```csharp
  if (y > lastObs) {
      inp = scen.ForecastModel.Project(y, ctx);
  }
  ```

切换模型 → 不改 InputsByYear（观测年） → 重跑产生新预测。
编辑某观测年的输入 → 不改模型 → 重跑产生新历史 → 新预测起点 → 新预测。
