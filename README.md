# 中国人口结构 · 工作台

[![Release](https://img.shields.io/github/v/release/LunaticSodium/china_demographic_model)](https://github.com/LunaticSodium/china_demographic_model/releases)

C# (.NET 8) + WPF 桌面应用。父项目 _2025 年前后的中国社会阶级情况调查_ 的 Stage 3 工具链。

## 这是什么

一个 cohort-component 中国人口结构**建模 + 多模型预测 + 反事实编辑**的桌面工作台。

- **数据层**：加载 1978-2025 NBS 年鉴、五次普查（1982/1990/2000/2010/2020）、民政部婚姻统计、TFR 序列、WHO/NBS 预期寿命估计。
- **历史层**：用 CCM (Cohort-Component Method) 从 1982 普查金字塔逐年向前推演。观测年的总出生数 / 性别比 / 结婚率 / 年末总人口被**作为硬约束**（Calibrator + PopulationAlignment），保证模型与 NBS 数据精确一致。
- **预测层**：从 2026 起切换到**可选预测模型**：OLS 趋势外推（默认）、末值常数、阻尼趋势。模型只负责生成预测年的标量（TFR / SRB / MAFM / e0 / 婚率），CCM 据此演化人口结构。
- **反事实层**：克隆 baseline → 自由编辑任意年的输入向量 → 重跑投影 → 对比偏离基线。

**数据 / 模型解耦**（v0.2.0 架构）：输入是数据，模型是数学操作，二者独立。用户编辑数据不改模型结构；切换模型不改输入数据。

## 跑起来

### 选项 A：下 release binary

去 [Releases](https://github.com/LunaticSodium/china_demographic_model/releases) 下最新 zip（含 .NET 8 runtime，无需另装），解压双击 `工作台.exe`。

### 选项 B：从源码跑

需要 **.NET 8 SDK** 或更高（已测试 .NET 10）。

```pwsh
git clone https://github.com/LunaticSodium/china_demographic_model.git
cd china_demographic_model
dotnet run --project src/UI/UI.csproj
```

或在 Visual Studio 2022+ 中打开 `ChinaDemographicModel.sln` 按 F5。

## 界面

三栏布局：

- **左**：场景列表 / 历史锁开关 / **预测模型选择** / 数据健康
- **中**：Tab 切换 — 人口金字塔（Canvas, X 轴固定刻度, **点击单龄条带显示详情**：人口 / q(a) / 已结婚估比例 / 已生育至少 1 子估比例 / 累计 cohort 损失）/ 时间序列（万人 / 万对 / 比率三子组）/ 输入编辑（滑条）
- **右**：当前年指标（预测年自动加 cyan 外框 + 模型派生数值）/ 偏离基线 / 日志 / 数据来源

顶栏自定义 4 色 YearSlider：一般灰 / 普查草绿 / 预测天青 / 反事实鹅黄。

## 当前状态 (v1.0.0)

### 已实装

| 类 | 内容 |
|---|---|
| 数据 | 1978-2025 出生 / 死亡 / SRB / 婚率 / 万对结婚 / 万对离婚 / 平均初婚 / e0 / TFR / 年末总人口；5 个普查金字塔（2020 详细 CSV + 其余 5 岁组近似 fallback）|
| 引擎 | CCM 投影、FertilityModel (TFR + MAFM → ASFR)、Calibrator（出生缩放对齐）、PopulationAlignment（NBS 年末口径修正）、CensusLifeTables（5 普查 × 22 锚 q(x) 二维插值）|
| 预测模型 | IForecastModel 接口 + 3 个实现：OLS 趋势 / 末值常数 / 阻尼趋势 |
| UI | 圆角深色 Fluent 主题、4 色 YearSlider、固定刻度金字塔 + 点击详情面板、时间序列三子组状态机、citation 面板、模型选择卡 |

### 待办（roadmap）

- **UserFixedForecast** — 用户锁定一个标量 + 设值，其余走 OLS
- **WppMediumVariantForecast** — 导入 UN WPP 2024 中变体 CSV
- **PadisStyleForecast** — 城乡分层 + 政策弹性参数（CPDRC 风格）
- 死亡侧 calibration（`AlignDeathsToHistory` 对称 `AlignBirthsToHistory`）
- 死亡率曲线 / ASFR(15..49) 折线编辑器
- IPF 形状对齐（普查 enumeration 与 CCM 形状残差吸收）
- 1985-2001 婚率历史数据（民政部系统数据自 2002 起）
- 国际迁移 NM
- 真正的 WPP-style project-and-adjust 迭代

## 项目结构

```
china_demographic_model/
├── ChinaDemographicModel.sln
├── README.md / PHILOSOPHY.md
├── src/
│   ├── Core/                       # 纯 C# 后端
│   │   ├── Models/                 # PopulationPyramid / DemographicInputs / Scenario
│   │   ├── Engine/                 # CCM / Fertility / Calibrator / PopulationAlignment
│   │   │                           # IForecastModel + 3 个实现 + ForecastRegistry
│   │   └── Data/                   # SeedLoader / HistoricalSeries / CensusLifeTables / CensusSeedPyramids
│   └── UI/                         # WPF 前端 (.NET 8)
│       ├── MainWindow.xaml / Themes/ModernTheme.xaml
│       ├── Views/                  # Pyramid / TimeSeries / InputsEditor
│       ├── ViewModels/MainViewModel.cs  (CommunityToolkit.Mvvm)
│       └── Controls/YearSlider.xaml + EnumToBoolConverter
├── data/seed/                      # CSV 种子数据
│   ├── births_yearly.csv           # 1978-2025
│   ├── deaths_yearly.csv           # 1978-2025
│   ├── total_population_yearbook.csv  # NBS 年末
│   ├── sex_ratio_at_birth.csv      # 普查 + 1‰ 抽样锚
│   ├── mean_age_first_marriage.csv # 普查锚
│   ├── marriage_rate.csv           # 民政部 2002+
│   ├── marriages_yearly.csv        # 民政部万对
│   ├── divorces_yearly.csv         # 民政部万对
│   ├── life_expectancy.csv         # 普查 + WHO 估计
│   ├── tfr_yearly.csv              # 普查 + 1‰ + NBS 估计
│   └── census_pyramids/            # 单岁 × 男女
└── docs/
    ├── MODEL.md                    # CCM 公式 + Brass logit + 时间约定
    ├── AUDIT.md                    # 对照 WPP 2022 方法学的自查
    ├── CROSS_SOURCE_REFS.md        # 多源数据对比 + 数值采纳原则
    ├── MODEL_CANDIDATES.md         # 预测模型架构与候选评估
    └── references/                 # UN WPP / Manual X PDF (gitignored)
```

## 数据立场

参 `PHILOSOPHY.md` §5："对中国官方人口数据，方法论可质疑，**数值层面必须全盘采纳**——没有可比可信度的替代源。口径修正用显式命名函数（`PopulationAlignment`）而非藏在私有 helper 里。"

NBS 年末 / 民政部 / 历次普查 是基础锚；模型基于这些锚做插值 + 外推 + cohort 演化。在普查年（1982/1990/2000/2010/2020），右栏所有指标显示值 = CSV 锚点值（插值在锚点 returns 锚点本身）。

参 `docs/CROSS_SOURCE_REFS.md` 有完整多源对比表。

## License

私人项目，未授权重发。
