# 预测模型候选与架构设计（round 7 准备）

用户原话（round 6）：
> 预测模型你可以固定 TFR，或者预期生育年龄，或者预期结婚年龄，或者预期婚后生育个数，或者把此类单值固定模型统称为同一个然后允许用户自选项和值。
> 我们可以先选择 1-2 个国际通用的模型，一个中国自己提出的模型，以及我们的单固定模型，然后保留精细的输入编辑，这里的输入作为数据存在而不影响模型的数学结构，也就是说模型应当在其之后应用。

## 1. 架构原则（关键）

**输入 (Inputs) 与 模型 (Model) 解耦**：

```
┌────────────────────┐    ┌─────────────────────┐    ┌──────────────┐
│ DemographicInputs  │ →  │  IForecastModel     │ →  │ Pyramid 序列  │
│ (data, 年表)       │    │  (math, 投影规则)    │    │              │
└────────────────────┘    └─────────────────────┘    └──────────────┘
   ↑                          ↑
   用户编辑                    用户从下拉框选
   (不改模型结构)              (不改输入数据)
```

`DemographicInputs.AgeSpecificFertility` 之类的字段当前混着 "原始数据" 和 "模型推导"。新架构里，输入只保留**观测得到 / 用户编辑的标量**（TFR / SRB / MAFM / 婚率 / e0），模型负责把这些标量投影到 ASFR 曲线 / q(x) 表 / 等需要的中间结构。

## 2. 单值固定模型（我们的，泛化）— `SingleValueFixedModel`

用户可选**锁定其中一个变量**，其余按既定 trajectory 投影：

| 锁定变量 | 含义 | 末观测值参考 |
|---|---|---|
| TFR | 总和生育率 | ~1.0 (2024) |
| 平均初婚年龄（女） | 婚育推迟程度的标量 | 27.95 (2020 七普) |
| 平均生育年龄（女） | = MAFM + 平均初婚-首育间隔（默认 1.5 年） | ~29 (推算) |
| 婚后期望生育数 | TFR / 已婚率 ≈ 已婚妇女期望生育数 | ~1.4 |
| e0 (男 + 女) | 预期寿命 | 75.9 / 81.5 (2024) |
| SRB | 出生性别比 | 108.5 (2024 估计) |

非锁定变量按 round 6 `ForecastModel.cs` 已写的指数/线性趋势继续投影。

UI: 在"模型"标签页里给用户一个 ComboBox 选锁定变量 + 一个 Slider 设其值，其余变量按默认 trajectory。

## 3. 国际通用模型候选

### 3.1 Lee-Carter mortality + ARIMA forecasting （**首选国际模型 A**）

经典死亡率随机预测方法。1992 论文（Lee & Carter）至今被引超 5000 次。

公式：
$$\ln m(x, t) = a_x + b_x k_t + \varepsilon$$

其中：
- $a_x$ = 年龄 $x$ 的对数死亡率平均水平
- $b_x$ = 年龄 $x$ 的相对变化敏感度
- $k_t$ = 时间索引（通常 ARIMA(0,1,0) 随机游走 with drift）
- $\varepsilon$ = 残差

预测：$k_{t+h} = k_t + h \cdot d + \sigma \sqrt{h} \cdot Z$ where $d$ = drift, $Z$ = N(0,1).

特点：
- 数据需求：长时序的 m(x,t) 矩阵（中国有 5 普查的 q(x)，可重建近似）
- 输出：概率性预测（中位数 + 80% 区间）
- 局限：长期 $b_x$ 假设固定，可能不适用于死亡率从高龄向低龄迁移的国家

实装计划：从 CensusLifeTables 重建中国 1981/1990/2000/2010/2020 五个时点的 m(x,t) 矩阵，做 SVD 提取 $a_x, b_x, k_t$，对 $k_t$ 用 ARIMA(0,1,0) 外推。

参考：
- [Lee-Carter 30 年综述, ScienceDirect 2022](https://www.sciencedirect.com/science/article/pii/S0169207022001455)
- [PMC: Prediction of China's Population Mortality](https://pmc.ncbi.nlm.nih.gov/articles/PMC9565027/)

### 3.2 UN WPP Bayesian hierarchical （**首选国际模型 B，已有引用**）

`docs/references/UN_WPP_2022_Methodology.pdf` 已经下载到本地。WPP 2022 用 Bayesian hierarchical models 估计 TFR 和 e0 的全年序列。

公式（TFR）：双 logistic 趋势 + 概率性预测：
$$\mathrm{TFR}(t+1) - \mathrm{TFR}(t) = d_1(\mathrm{TFR}(t); \theta) + d_2(\mathrm{TFR}(t); \theta) + \varepsilon$$

其中 $d_1$ 是系统下降段（从前现代水平到低生育水平），$d_2$ 是反弹段（向 $\sim 2.1$ 收敛的可能）。参数 $\theta$ 在所有国家间共享 + 国家级随机效应。

特点：
- 数据需求：每个国家的 TFR / e0 长时序（中国 1950-至今）
- 输出：高 / 中 / 低 / 概率分布 4 个变体
- 优势：全球可比、有 prior 约束防止灾难性外推

实装复杂度高（Bayesian 推断 + MCMC）。简化方案：**只取 WPP 的中变体预测值**作为外部输入（CSV 形式），不在本工具内做 Bayes 推断。等于把 WPP 的输出当数据接进来。

### 3.3 其他可考虑

- IIASA Wittgenstein Centre SSP scenarios（含教育维度，复杂度高，defer）
- US Census IDB（International Data Base）— 简单 trend extrapolation，类似单值固定

## 4. 中国本土模型候选（**首选 PADIS-INT**）

### 4.1 PADIS-INT（中国人口与发展研究中心）

国家卫生健康委员会下属的 **中国人口与发展研究中心 (CPDRC)** 自主开发的人口预测软件。基于 cohort-component 方法 + 中国特化参数。十余年的内部研究与应用基础。

特点：
- 中国官方权威（卫健委直接使用）
- 数学基础：经典 CCM，与本工具相同
- 中国特色参数：
  - 城乡分层（户籍 vs 非户籍 / 农业 vs 非农）— 本工具未实装
  - 婚育政策调整反应模型（生育意愿 vs 政策的弹性）— 经验值
  - 流动人口处理（省际迁移矩阵）
- 输出：年龄 × 性别 × 城乡 / 户籍维度的预测

**实装策略**：作为参照模型，从 CPDRC 公开报告抽取核心参数：
- 城乡 TFR 差异（农村 1.3 vs 城镇 1.0 量级）
- 政策弹性参数（生育意愿对政策的响应）

或：实装一个**简化 PADIS-INT 风格**模型——保留 CCM 主体但加上城乡分层（输入 CSV 加列）。

参考：
- [PADIS-INT 介绍, CPDRC](https://www.cpdrc.org.cn/sjzw/yjgj/202311/t20231124_17127.html)
- [王广州 中国人口预测方法及未来人口政策](http://ft.newdu.com/uploads/collect/201808/14/W0201808143805157958104156.pdf)

### 4.2 育娲人口 (梁建章 / 任泽平团队) — 民间独立估计

非官方但有影响力。预测假设 TFR 进一步下降到 0.8 floor + 政策不松动场景。

特点：
- 数据：用 NBS 数据 + 独立估算调整（如出生瞒报修正）
- 模型：标准 CCM + 主观 TFR 路径
- 输出：长期预测 + 经济影响分析

实装策略：作为**第二中国模型**或第四国际/民间模型，简单：直接 hard-code 育娲发布的 TFR 路径作为一个 preset。

### 4.3 翟振武 / 陈卫 论文模型

主要是学术研究，集中在政策弹性建模（"全面二孩"等）。每篇论文有自己的参数。defer。

## 5. Round 7 实装计划

### 接口

```csharp
public interface IForecastModel
{
    string Name { get; }
    string Description { get; }
    /// 给定末观测年的标量状态 + 目标年, 返回投影的 DemographicInputs
    DemographicInputs Project(int year, int lastObservedYear, DemographicInputs lastObsInputs, ForecastContext ctx);
}

public sealed class ForecastContext
{
    public HistoricalSeries Historical { get; init; }
    public IReadOnlyDictionary<int, PopulationPyramid> ProjectedSoFar { get; init; }
}
```

### 实装顺序

1. `SingleValueFixedModel`（refactor 现有 ForecastModel；参数化）
2. `LeeCarterModel`（从 CensusLifeTables 重建 m(x,t)，SVD，ARIMA）
3. `WppMediumVariantModel`（导入 WPP-2024 中变体 CSV，逐年查表）
4. `PadisStyleModel`（简化版：含城乡分层占位 + CCM）

### UI

新增 **"模型"** Tab（或左侧抽屉），含：
- 模型选择 RadioButton 组
- 每个模型展开后显示其特有参数
- 应用按钮触发 ScenarioBuilder 重建 forecast 部分

### 输入与模型解耦

`ScenarioBuilder.BuildBaseline` 当前直接把"forecast 期 inputs"硬填进 Scenario.InputsByYear。改为：
- Scenario 持有 `IForecastModel` 引用
- InputsByYear 只持有观测年的 inputs
- RunProjection 时, forecast 年的 inputs 由 model.Project(...) 即时生成
- 用户在 input editor 改观测年 → 数据变, 模型重跑
- 用户在 model 标签页换 forecast 模型 → 重跑, 观测年 inputs 不变

## 6. 本轮 (round 6) 已落地 / round 7 待办

| 项 | round 6 状态 | round 7 行动 |
|---|---|---|
| 工具名 "反事实工具" 不妥 | ✓ 改为 "工作台" | — |
| 金字塔 X 轴固定刻度 | ✓ 跨 scenario × year 全局最大 | — |
| 单值固定预测模型 (TFR + e0 + SRB + MAFM + 婚率) | ✓ 默认 trajectory 在 `ForecastModel.cs` | refactor 为 `SingleValueFixedModel` + 用户可选锁定变量 |
| Lee-Carter | — | round 7 实装 |
| WPP 中变体 (导入 CSV) | — | round 7 实装 |
| PADIS-INT 风格 | — | round 7 实装（简化版）|
| 多模型选择 UI | — | round 7 |
| 输入 / 模型解耦 | 部分（`isForecast` 分支）| round 7 完全解耦 |
| 复杂化 + 分页 | — | round 7+ 慢慢加 |
