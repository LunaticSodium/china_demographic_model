# 自查与修正记录 (Round 3 audit)

参照：`docs/references/UN_WPP_2022_Methodology.pdf` （UN DESA 官方方法学，CC BY 3.0 IGO）。

逐条对照本项目实现 vs WPP 方法学，列出违反点和处置。

---

## §1. 普查年覆盖 vs intercensal consistency（**致命问题**）

### WPP 怎么做

WPP 2022 §I.A & Figure I.1: CCMPP 是 **consistency engine**——
1. 从基准期 $t_0$ 普查起跑
2. 用估计的生育 / 死亡 / 迁移投影到下一个基准期 $t_1$ 普查
3. **对比** CCMPP 投影 vs $t_1$ 普查
4. 不匹配则**迭代调整组件**（特别是迁移残差）使 CCMPP 输出收敛于普查
5. 重新跑直到收敛

关键原则：**普查从不直接覆盖投影**。普查是**比对基准**，差异通过组件调整吸收。

### 本项目原 round 2 实现

`MainViewModel.RunProjectionForScenario`：

```csharp
// 普查年 re-anchor：替换为普查金字塔（保留形状）
if (Historical.CensusPyramidByYear.TryGetValue(next.Year, out var census))
    next = _calibrator.AlignPyramidToCensus(next, census);   // ❌ 暴力覆盖
```

`Calibrator.AlignPyramidToCensus` 内部就是单岁 copy。结果：
- 1989 → 1990：CCM 投影到 1990，整个年龄结构被覆盖为 1990 census 的形状
- 1990 → 1991：从 1990 census 形状再 CCM 投影
- 用户在 slider 上拖到 1990 看见结构**瞬变**，1989 / 1990 同一 cohort 不衔接

### Round 3 处置

**移除** `RunProjectionForScenario` 中的 `AlignPyramidToCensus` 调用。

- CCM 自 1982 普查起跑，**自由**投影到 2050；
- `PopulationAlignment.AlignToNbsYearEnd` 每年仍执行，保证**总量**逐年贴合 NBS 年末口径；
- 年龄结构在普查年**不被覆盖**，但通过 CCM + 紧约束生育 + 校准死亡率 schedule 自然演化；
- 与普查实际形状的偏差不在 UI 上做"硬切回"，而是允许 deviation 累积——这与 WPP 一致；
- 未来：实现真正的迭代项目-and-adjust（用迁移残差吸收偏差），那时再启用 re-anchor。

`Calibrator.AlignPyramidToCensus` 函数本身**保留**作为公共 API（未来 IPF / 软对齐扩展的钩子），但 Runtime 路径不再调用。

---

## §2. 配色：预测段不该是红色

### 用户反馈

"未来部分用红色太神秘了"——之前 round 2 预测段用 `AccentTertiaryBrush = #FB7185`（rose-400，偏红），视觉上容易理解为"危险 / 错误"。

### Round 3 处置

新配色（与用户指定吻合）：

| 段 | 含义 | Brush | 色值 |
|---|---|---|---|
| 一般 | NBS 估算年，非普查 | `BgElevBrush` | #334155 slate-700 |
| 普查 | 1982/1990/2000/2010/2020 | `SuccessBrush` | #4ADE80 草绿 green-400 |
| 预测 | > 2024 | `ForecastBrush`（新增）| #67E8F9 天青 cyan-300 |
| 反事实 | 克隆场景内任何年 | `WarnBrush` | #FBBF24 鹅黄 amber-400 |

涉及文件：
- `Themes/ModernTheme.xaml`：加 `ForecastBrush` 资源
- `Controls/YearSlider.xaml.cs`：brush lookup 改名
- `MainWindow.xaml`：顶栏图例 4 个 dot 改用新 brush

---

## §3. 时间参考约定（reference dates）

### WPP 怎么做

WPP 2022 Table I.1: 2022 修订把 population 输出口径统一为 **1 January** of reference year；vital rates 用 **1 January 到 31 December** 的整日历年。

之前的 2019 修订是 **1 July** mid-year。

### 本项目

我们的 `PopulationPyramid.Year` 含义没显性声明，但 CCM 实现：
```csharp
var next = new PopulationPyramid { Year = start.Year + 1 };
```
即 `Project(pyramid_t, inputs_t) → pyramid_{t+1}`。

我们的 `total_population_yearbook.csv` 标的是 NBS **年末**（12-31）。

如果约定 `pyramid.Year = t` 代表 1-Jan-t：
- 1-Jan-t + 整年 t 事件 → 1-Jan-(t+1)
- NBS 年末 Y = 12-31-Y ≈ 1-Jan-(Y+1)
- → `pyramid_{Y+1}` 应该对齐 `NBS_year_end[Y]`，**off-by-one**

如果约定 `pyramid.Year = t` 代表 12-31-t（年末）：
- 12-31-t + 整年 (t+1) 事件 → 12-31-(t+1)
- 那 `inputs[t]` 实际是"年 t+1 的事件"——标签不直观

### Round 3 处置

**约定**：`pyramid.Year = t` 代表 **年末 t（≈ 1-Jan-(t+1)）**。即金字塔标签 = NBS 年末标签。`inputs[t]` 代表"从年末 t-1 到年末 t 的事件总和"。

这与现在的代码行为自洽（`PopulationAlignment` 当前用 `nbsYearEnd[pyramid.Year]` 对齐），不需要改逻辑，只需在文档和注释中明确声明。

涉及文件：
- `Models/PopulationPyramid.cs`：在类注释加约定说明
- `Engine/CohortComponentProjector.cs`：在 Project 方法注释说明
- `docs/MODEL.md`：加一节"时间参考约定"

---

## §4. Population exposures vs start-of-period stock

### WPP 怎么做

WPP 2022 §I.A: 速率（fertility、mortality）应该应用于 **person-years of exposure**（约等于 mid-year population without migration）。WPP 2022 显式计算 exposure 作为独立指标。

### 本项目

我们的 CCM 把 q(x) 应用于 **start-of-period** 人口 `start.Male[a] * (1 - q[a])`。严格说这是 "discrete probability" 而非 "rate × exposure"。

差别在数量上很小（一阶项），但概念上是 single-decrement life table 的近似。

### Round 3 处置

**接受**——MVP 阶段允许 ±0.5% 量级偏差。在 `MODEL.md` §3 补一条 caveat 即可。未来如做 multi-decrement / 高精度版本，需要分离 exposure 计算。

---

## §5. 国际迁移 (NM)

### WPP

CCMPP 完整方程：$P(t+n) = P(t) + B - D + NM$。WPP 显式估计每国净迁移并作为残差吸收 benchmark 差异。

### 本项目

完全忽略 $NM$。中国近年净迁移 ~-50 万/年（出 > 入），相对 14 亿盘子是 0.04%/年，但 cumulative 几百万级。

### Round 3 处置

**仍然忽略**。但因为我们已经移除了 census 覆盖（§1），普查年与 CCM 投影的总量差异现在通过 `PopulationAlignment` 等比缩放吸收，而非 migration residual——这与 WPP 的"用 migration 吸收残差"不同，但产生的总量是一致的，**形状偏差**没有被吸收。

未来如要做更细致：实现 `MigrationResidualEstimator`，从普查 vs CCM 投影的总量差 + 形状差反推出隐含的迁移结构，然后回填到 CCM 的 NM 输入。**不在本轮**。

---

## §6. 1×1 vs 5×5 framework

### WPP

2022 修订从历史的 5 岁组 × 5 年期间（5×5）转到 **单岁 × 单年（1×1）**。理由：更精细、对短期冲击（COVID 等）更敏感、更易用于不规则年龄段（如学龄、退休年龄）。

### 本项目

我们一开始就是 1×1 单岁。✓ 与 WPP 现行标准一致。

不过 WPP 内部用 0..130 单岁，输出用 0..100+。我们内部 + 输出都用 0..100，80+ 段精度有限。**接受**。

---

## §7. 不确定性区间

### WPP

每个 country-year 输出 80% 与 95% 预测区间，源自概率性 Bayesian 模型（fertility / mortality / migration 各自的双 logistic + 等）。

### 本项目

完全确定性，无 PI。这是个 MVP 工具，不是 WPP 替代品。

### Round 3 处置

**接受**——文档中标明。如未来做敏感性带，应模仿 WPP 的双 logistic + Bayesian hierarchical 框架。

---

## §8. 数据 quality 标记

### WPP

每个 country-year 的指标都有 quality flag（数据源 / 估计方法 / 不确定度），用户在 DataPortal 可看。

### 本项目

我们的 `sex_ratio_at_birth.csv` 有 `quality` 列（anchor / interp），但其他 csv 没有。

### Round 3 处置

**defer**——可以未来在每个 CSV 加 quality 列，UI 在 hover 时显示。本轮不做。

---

## §9. 总结

| 编号 | 问题 | 严重度 | 本轮处置 |
|---|---|---|---|
| §1 | 普查年覆盖破坏连续性 | **critical** | **移除** AlignPyramidToCensus 调用 |
| §2 | 配色（预测红色过于神秘）| 中 | 改 4 色：草绿 / 天青 / 鹅黄 / 灰 |
| §3 | 时间参考约定不明确 | 低 | 文档显性声明（pyramid 年标 = 年末口径）|
| §4 | exposure vs stock | 低 | 接受 + 文档 caveat |
| §5 | 无 NM | 中 | 接受 + 文档 caveat（PopulationAlignment 已吸收总量差异）|
| §6 | 1×1 framework | - | 已符合，无操作 |
| §7 | 无 PI | 中 | 接受 + 文档 caveat |
| §8 | 无 quality flag | 低 | defer |

未来 round 4+ 候选：
- 实现 IPF 形状对齐（§1 高阶版本）
- 实现 NM 残差估计（§5 partial）
- 实现 1% 抽样年（1987/2005/2015）作为次级 anchor
- 数据精度提升：用真实历次普查的 22-锚 q(x) 值替换 `CensusLifeTables` 的近似常量
