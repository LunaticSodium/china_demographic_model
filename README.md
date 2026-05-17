# 中国人口结构 · 反事实工具

C# (.NET 8) + WPF 桌面应用。属于父项目 _2025 年前后的中国社会阶级情况调查_ Stage 3 工具链。

## 这是什么

一个 cohort-component 人口模型 + 反事实编辑器：

- 加载 1978–2024 的公开历史观测（出生数 / 出生性别比 / 结婚率 / 平均初婚年龄 / 普查金字塔）。
- 用 CCM (Cohort-Component Method) 把起始普查金字塔逐年向前推演到 2050。
- 默认 `LockToHistory` 模式下，已观测年的 **总出生数 / 性别比 / 结婚率** 被作为硬约束；模型 ASFR / 死亡率 schedule 被 Calibrator 缩放以命中观测；模型金字塔总人口被 PopulationAlignment 对齐到 NBS 年末。
- 用户可 **克隆 baseline 为反事实场景**：解除历史锁，自由修改任意年的输入向量（例如"如果 1980 年代不强推计生""如果 2016 年开放三孩"），重跑投影，对比偏离基线。
- 数据本身不被假设为"真实"；本工具用"以观测为约束 + 反事实推演"代替"以观测为真相"。详见 `data/sources.md`。

## 跑起来

需要 **.NET 8 SDK** 或更高（已测试 .NET 10）。

```pwsh
cd china_demographic_model
dotnet restore
dotnet build
dotnet run --project src/UI/UI.csproj
```

或在 Visual Studio 2022+ 中打开 `ChinaDemographicModel.sln`，按 F5。

## 结构

```
china_demographic_model/
├── ChinaDemographicModel.sln
├── README.md
├── src/
│   ├── Core/                         # 纯 C# 后端，无 UI 依赖
│   │   ├── Models/                   # PopulationPyramid / DemographicInputs / Scenario
│   │   ├── Engine/                   # CohortComponentProjector / FertilityModel / Calibrator / ScenarioBuilder
│   │   └── Data/                     # SeedLoader / HistoricalSeries / CensusSeedPyramids
│   └── UI/                           # WPF 前端
│       ├── App.xaml, MainWindow.xaml
│       ├── Themes/ModernTheme.xaml   # 圆角 + 深色科技感主题
│       ├── Views/                    # PyramidView (Canvas), TimeSeriesView (ScottPlot), InputsEditorView (滑条)
│       └── ViewModels/MainViewModel.cs  # CommunityToolkit.Mvvm
├── data/
│   ├── seed/                         # CSV 种子数据 (1978-2024)
│   │   ├── births_yearly.csv
│   │   ├── deaths_yearly.csv
│   │   ├── sex_ratio_at_birth.csv
│   │   ├── marriage_rate.csv
│   │   ├── marriages_yearly.csv      # 万对 (民政部)
│   │   ├── divorces_yearly.csv       # 万对 (民政部)
│   │   ├── mean_age_first_marriage.csv
│   │   ├── life_expectancy.csv
│   │   ├── total_population_yearbook.csv  # NBS 年末口径
│   │   └── census_pyramids/pyramid_2020.csv
│   └── sources.md                    # 数据来源 + 口径 + caveat
└── docs/MODEL.md                     # CCM 公式 + 约束推导
```

数据被 csproj 用 `<None Include="..\..\data\**\*.csv">` 复制到 bin/{config}/net8.0-windows/data/seed/。运行时 SeedLoader 用 ResolveSeedDir 寻路。

## 当前状态 (MVP)

✅ 三栏 WPF 主窗口、圆角深色主题、三 Tab：人口金字塔 / 时间序列 / 输入编辑
✅ CCM 投影引擎（生存 + 老化 + 出生分性别）
✅ FertilityModel（TFR + 平均初婚年龄 → ASFR）
✅ **CensusLifeTables**：直接采用 5 次普查 22 锚 × 男女 q(x)，时间×年龄二维插值（普查范围外用 Brass shift）
✅ **PopulationAlignment**（显式命名）：对 NBS 年末口径做缩放修正；UI 显示对齐后数值
✅ 自由 CCM 投影（移除普查覆盖以保 cohort 连续性，详见 docs/AUDIT.md §1）
✅ Calibrator（模型出生 ↔ 观测出生缩放对齐）
✅ ScenarioBuilder（baseline 自动装配 + 观测 e0 接入）
✅ 历史数据 1978-2024 全量年度（出生 / 死亡 / SRB / 结婚率 / 平均初婚 / e0 / 年末人口 / 结婚 / 离婚）+ 5 个普查金字塔
✅ 反事实克隆 + 偏离基线对比
✅ 自定义 4 色 YearSlider（一般 / 普查 / 预测 / 反事实）
✅ Pyramid 400ms hover tooltip（年龄 / 男女 / q(a)）
✅ TimeSeriesView 三子组状态机：万人 / 万对 / 比率

⏳ 待办：

- 死亡侧 calibration（`AlignDeathsToHistory`，对称 `AlignBirthsToHistory`）
- 死亡率曲线编辑器（当前仅 e0 单标量可编辑）
- ASFR(15..49) 折线编辑器（当前由 FertilityModel 自动生成）
- 真实 1982/1990/2000/2010 普查金字塔 CSV（当前用 5 岁组 → 均匀展开近似）
- IPF 形状对齐（替换 stub `AlignPyramidToCensus`）
- 历次 1% 抽样年（1987/2005/2015）作为次级 anchor
- Brass logit β-自由形式（当前 β=1）
- 国际迁移项
- WPP / 育娲 / Wittgenstein Centre 并行加载做敏感性带

## 设计约束

来自父项目 PHILOSOPHY：

- **官 / 半官源默认不信** → 本工具默认把 NBS / 民政部数据当**实证下限**，不当真相终点
- **可见性反比例 + 拟合外推** → 用户可对 2024 后年份做反事实未来推演；偏离基线幅度可见
- **数据可追溯** → 每个 CSV 顶部含来源 + caveat
- **AI 污染意识** → 本工具不调用任何 LLM 服务；所有计算可复现

## License

私人项目，未授权重发。
