# 模型说明

## 1. Cohort-Component Method (CCM) 基本方程

记 $P_{a,s,t}$ 为 $t$ 年龄龄 $a$、性别 $s$ 的人口数，$q_{a,s,t}$ 为一年内死亡概率，$B_t$ 为 $t$ 年总出生数，$\mathrm{SRB}_t$ 为出生性别比（男婴/100女婴）。

**老化 + 存活**（年龄 $a$ → $a+1$）：

$$
P_{a+1, s, t+1} = P_{a, s, t} \cdot (1 - q_{a, s, t})
$$

**开放年龄段**（$a = A_{\max}$, 本实现 $A_{\max}=100$）累加旧顶 + 新进入：

$$
P_{A_{\max}, s, t+1} = P_{A_{\max}-1, s, t}(1 - q_{A_{\max}-1, s, t}) + P_{A_{\max}, s, t}(1 - q_{A_{\max}, s, t})
$$

**出生**：

$$
B_t = \sum_{a=15}^{49} P_{a, F, t} \cdot f_{a, t}
$$

其中 $f_{a,t}$ 是 ASFR (Age-Specific Fertility Rate)，即年龄 $a$ 女性当年每人生育数。

**婴儿入位**：

$$
P_{0, M, t+1} = B_t \cdot \frac{\mathrm{SRB}_t}{100 + \mathrm{SRB}_t} \cdot (1 - q_{0, M, t})
$$

$$
P_{0, F, t+1} = B_t \cdot \frac{100}{100 + \mathrm{SRB}_t} \cdot (1 - q_{0, F, t})
$$

本实现：`src/Core/Engine/CohortComponentProjector.cs`。

## 2. ASFR 从 (TFR, 平均初婚年龄) 反推

$$
f_{a,t} = \frac{\exp\!\left( -\frac{(a - \mu_t)^2}{2\sigma^2} \right)}{\sum_{a'=15}^{49} \exp\!\left( -\frac{(a' - \mu_t)^2}{2\sigma^2} \right)} \cdot \mathrm{TFR}_t
$$

$\mu_t = \mathrm{MAFM}_F(t) + \Delta$，$\Delta = 1.5$（初婚到首育间隔，默认）。

实现：`src/Core/Engine/FertilityModel.cs`。是一个**可替换的简化接口** —— 真实场景应该用 cohort-specific 的胎次序数模型 + 推迟修正。

## 3. 紧约束（LockToHistory）

定义：

- 集合 $O$ = 1978–2024 已观测年。
- 集合 $H$ = $\{\text{TotalBirths}, \text{SRB}, \text{CrudeMarriageRate}\}$ —— 已有可信公开统计的字段。

**约束**：

$$
\forall t \in O, \forall x \in H: \quad \mathrm{input}(t, x) = \mathrm{observed}(t, x)
$$

`RunProjection` 在 `LockToHistory = true` 时先把这些字段回退到 `HistoricalSeries` 中的值，然后再调用 `Calibrator.AlignBirthsToHistory` 对 ASFR 做整体缩放，使模型生成的 $\sum P_{a,F,t} \cdot f_{a,t} = \mathrm{TotalBirths}^{\mathrm{obs}}_t$。

数学上这是**欠定**的 —— 同一个总出生数可以对应不同的 ASFR 形状。当前 stub 是"按比例缩放当前 ASFR"，未来可换成"最小化与基线 ASFR 形状的 KL 距离 + 命中观测总量"的 LP/QP 问题。

实现：`src/Core/Engine/Calibrator.cs`。

## 4. 普查年 re-anchor (TODO)

当 $t$ 是普查年且 `CensusPyramidByYear[t]` 存在时，可调用 `Calibrator.AlignPyramidToCensus` 把当年模型金字塔替换为观测。这相当于把模型的累积偏差吸收掉，下一段从观测重启。

当前实现是**朴素覆盖**。未来用 IPF（iterative proportional fitting）+ 平滑约束做真正的"拉齐 + 保留模型动力学信号"。

RunProjection 中目前**未启用** re-anchor —— 用户如需启用，可在 `RunProjection` 循环里增加：

```csharp
if (Historical?.CensusPyramidByYear.TryGetValue(cur.Year, out var c) == true)
    cur = _calibrator.AlignPyramidToCensus(cur, c);
```

## 5. 反事实场景

`Scenario.CloneAs(name)` 复制基线场景；用户在 InputsEditorView 修改任意年的滑条值，点"应用到当前年"，把值写入 `Scenario.InputsByYear[y]` 并把 $y$ 加入 `EditedYears`。

随后 `RunProjection`：
- 若仍 `LockToHistory = true`：观测年的 TotalBirths/SRB/CrudeMarriageRate 会被回退（即使被用户改过也无效）。其他字段（TFR/MAFM_M/MAFM_F/死亡率 schedule）仍生效，并通过 Calibrator 与观测总量协调。
- 若 `LockToHistory = false`（克隆场景默认）：完全使用用户编辑值。

`DeviationReport` = 当前场景在 $t$ 年总人口与 baseline 场景的差。

## 6. 死亡率 schedule —— 中国普查实证生命表

实现：`src/Core/Data/CensusLifeTables.cs`。

### 6.1 直接采用普查公布的 q(x)

五次人口普查（1981/1990/2000/2010/2020）公布的分年龄分性别死亡概率，在 22 个关键年龄锚点（0, 1, 5, 10, ..., 95, 100）以代码常量形式存储。这取代了上一版的 Coale-Demeny East + Brass 间接构造 —— 对中国 1981-2020 年范围内的死亡 schedule，**直接使用普查实证值**，不用模型族族近似。

### 6.2 普查间年份插值

对 $t_1 < t < t_2$（$t_1, t_2$ 是两个相邻普查年），关键年龄 $a$ 处的死亡概率：
$$
q(a, t) = q(a, t_1) \cdot \frac{t_2 - t}{t_2 - t_1} + q(a, t_2) \cdot \frac{t - t_1}{t_2 - t_1}
$$

然后单岁通过相邻关键年龄之间线性插值填充到 0..100。

### 6.3 普查范围外（< 1981 或 > 2020）

落在普查范围外的年份，先取最近普查作为模板，然后应用 **Brass logit 平移**到目标 $e_0$（来自 `life_expectancy.csv`）：
$$
l^{\text{new}}(x) = \frac{1}{1 + \exp\!\left(2(\alpha + Y^{\text{std}}(x))\right)},
\quad Y^{\text{std}}(x) = \tfrac{1}{2} \ln \frac{1 - l^{\text{std}}(x)}{l^{\text{std}}(x)}
$$

阻尼 Newton 解 $\alpha$，收敛准则 $|\Delta e_0| < 0.005$。本质上是把"中国普查 schedule"作为标准，而不是 CD-East。

### 6.4 数据精度 caveat

代码内置的 22 锚点 q(x) 是从历次普查公报记忆整理，相对真值可能 ±5–10%。如要精确，把 `CensusLifeTables.KeyMaleQ` / `KeyFemaleQ` 替换为完整 5 普查公报值。

### 6.5 局限

- 锚点 22 个，单岁线性展开会平滑掉 65-70 死亡 hump 等局部特征；改用 spline 可改进。
- 普查反映"普查时点 12 个月"死亡率，不一定等于普查所代表的整 10 年。
- 2020 之后用 Brass shift 自普查值平移，仍然单参数 (β=1)，未来可换 β-自由形式。

## 7. 总人口口径修正 —— `PopulationAlignment`

实现：`src/Core/Engine/PopulationAlignment.cs`。**显式命名**——按 PHILOSOPHY §5 "数值采纳 / 口径修正" 原则要求。

### 7.1 问题

CCM 投影从 1982 普查起始金字塔向前推演，累积偏差使模型总人口与 NBS 年末估计逐年偏离。例如 fallback 1982 金字塔总和 ≈ 966M 但 NBS 1982 年末估计 = 1016.5M（差 ≈ 5%）。

### 7.2 函数定义

对每年 $t$，令 $S(t)$ = 模型金字塔总人口，$N(t)$ = NBS 年末估计：
$$
\text{factor}(t) = \frac{N(t)}{S(t)}
$$

对当年金字塔每个 bin $(a, s)$ 等比缩放：
$$
P^{\text{aligned}}(a, s, t) = P^{\text{model}}(a, s, t) \cdot \text{factor}(t)
$$

形状（年龄结构、性别比）完全保留；只对齐总量。

### 7.3 调用顺序

`MainViewModel.RunProjectionForScenario` 每年的步骤：
1. 紧约束应用（已观测年 totals 复位）
2. CCM 单步（$P_t \to P_{t+1}^{\text{model}}$）
3. 普查 re-anchor（如果 $t+1$ 是普查年，用普查金字塔替换 $P^{\text{model}}$；保留形状）
4. **PopulationAlignment** 缩放到 NBS 年末 $N(t+1)$

普查年 re-anchor 后 + 再做 NBS 年末对齐，是有意的：普查时点（11月1日）≠ 年末（12月31日），相差几百万级。UI 显示年末口径为求与非普查年连续。

### 7.4 反事实场景下的语义

LockToHistory = false 时仍调用 PopulationAlignment，但 NBS 序列只覆盖 1978-2024，2025+ 年份没有目标，函数返回 `WasCorrected = false`，金字塔保留模型原始值。反事实场景在 1978-2024 仍然被对齐——因为这些年的 NBS 数据是"事实"，反事实改的是输入（生育 / 死亡率 / 婚姻），不改"已发生的事实总量"。要想反事实 NBS 序列本身，需要扩展 PopulationAlignment 接受场景内 override（未实现）。

## 8. 模型局限（综合）

| 项 | 当前状态 | 影响 |
|---|---|---|
| 国际迁移 | 忽略 | 中国近年净迁移约 -50 万/年级，长期累积 -几百万。结构性影响小。|
| 区域 / 城乡 | 单一全国模型 | 完全损失结构信号。Stage 3 应展开。|
| 死亡率 schedule | CensusLifeTables (5 普查 × 22 锚) + 范围外 Brass | 锚点级联线性插值会平滑年龄结构 hump；可换 spline。|
| 死亡侧 calibration | 未做 | 模型生成死亡 vs 观测死亡偏离未约束。`AlignDeathsToHistory` TODO。|
| ASFR 形状 | 正态展开 | 应该用 cohort-specific 推迟模型。|
| 1% 抽样年 anchor | 未利用 | 1987/2005/2015 三个数据点未参与 Calibration。|
| 数据下限假设 | 隐含 | 本模型把 NBS 数据当下限 + 紧约束。如要做"上限"反推（瞒报 / 漏报），需新增 InflationFactor 参数。|
| PopulationAlignment | 仅总量缩放 | 不修正形状偏差；不区分年龄段偏差大小。|

## 9. 与父项目方法论的对应

- **可见性反比例 + 拟合外推**（PHILOSOPHY §Stage 3）：本工具的反事实模式即是该方法的最小实现。把"可见的官方数据"作为下限 → 把"显著偏离基线的反事实"作为可见的外推候选 → 看哪个反事实假设产出与其他独立证据（社会观察、间接指标）最吻合。
- **数据可追溯**（PHILOSOPHY §1）：每份 CSV 顶部声明来源 + caveat；模型代码每个 stub 处明示当前假设。
- **AI 污染意识**（PHILOSOPHY §2）：本工具不调用 LLM；所有计算可复现，可单测。
