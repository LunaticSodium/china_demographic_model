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

## 6. 死亡率 schedule —— Coale-Demeny East + Brass logit

实现：`src/Core/Engine/CoaleDemenyLifeTable.cs`。

### 6.1 标准生命表

内置 **CD-East family** 在关键年龄点的 $q(x)$（女标准 $e_0 \approx 70$，男 $\approx 66.5$），单岁通过线性插值填充：

- 婴儿段：$q(0) \approx 0.040$（女）/ 0.045（男）
- 童年低谷：$q(5..15) \in [0.0008, 0.0014]$
- 年轻成人事故峰（男）：$q(20..30) \approx 0.0020$
- 老年 Gompertz 段：$q(60+)$ 指数上升

从 $q(x)$ 推出标准存活函数 $l^{\text{std}}(x)$：
$$
l^{\text{std}}(0) = 1, \quad l^{\text{std}}(x+1) = l^{\text{std}}(x) \cdot (1 - q^{\text{std}}(x))
$$

### 6.2 Brass logit 平移

定义 logit 变换：
$$
Y^{\text{std}}(x) = \tfrac{1}{2} \ln \frac{1 - l^{\text{std}}(x)}{l^{\text{std}}(x)}
$$

对目标 $e_0$，求解参数 $\alpha$ 使：
$$
l^{\text{new}}(x; \alpha) = \frac{1}{1 + \exp\!\left(2 (\alpha + Y^{\text{std}}(x))\right)}
$$

满足 $e_0(l^{\text{new}}) = e_0^{\text{target}}$。

代码用阻尼 Newton 迭代（每步 step 上限 0.3，$\alpha$ 在 $[-3, +3]$ 之间）。收敛准则：$|\Delta e_0| < 0.005$ 年。典型 5-10 步内收敛。

从 $l^{\text{new}}(x)$ 推回 $q^{\text{new}}(x) = 1 - l^{\text{new}}(x+1) / l^{\text{new}}(x)$，输入到 CCM 引擎。

### 6.3 数据接入

`data/seed/life_expectancy.csv` 给五次普查直接公布的 $e_0$（男 / 女）+ WHO / NBS 非普查年估计。`ScenarioBuilder.BuildBaseline` 按观测年取直接值；非观测年用 `LookupOrInterp` 线性插值；落在数据外的年份回退到 `EstimateE0`（trend line）。

`data/seed/deaths_yearly.csv` 给年总死亡观测。当前 **未** 用于反向 calibrate $e_0$（即不做"已知死亡数 → 反推真实 $e_0$"）。未来可加：把模型生成的 $\sum_a P_a \cdot q_a$ 与观测总死亡比较，做与 `AlignBirthsToHistory` 对称的 `AlignDeathsToHistory`。

### 6.4 局限

- **形状层面**：CD-East 是 1960s 数据拟合，对当代中国可能略偏 —— 比如 50-65 岁段心血管负担与现代不同。如果要 fit 更准，应换 UN Far East 表或直接用 2010 普查中国生命表作为标准。
- **β=1 假设**：当前 Brass 形式只移动整体水平 ($\alpha$)，不调整年龄结构形状 ($\beta$)。China 1981→2020 的死亡率改善在低龄段 (婴儿) 比老龄段更快 —— 真实形状 $\beta > 1$。当前 $\beta=1$ 会把整体提升均匀分给所有年龄，低估婴儿改善 / 高估老龄改善。

## 7. 模型局限（综合）

| 项 | 当前状态 | 影响 |
|---|---|---|
| 国际迁移 | 忽略 | 中国近年净迁移约 -50 万/年级，长期累积 -几百万。结构性影响小。|
| 区域 / 城乡 | 单一全国模型 | 完全损失结构信号。Stage 3 应展开。|
| 死亡率 schedule | CD-East + Brass logit (β=1) | 见 §6.4。换 β-自由 Brass 或 WAJ log-quadratic 可改进。|
| 死亡侧 calibration | 未做 | 模型生成死亡 vs 观测死亡偏离未约束。`AlignDeathsToHistory` TODO。|
| ASFR 形状 | 正态展开 | 应该用 cohort-specific 推迟模型。|
| 1% 抽样年 anchor | 未利用 | 1987/2005/2015 三个数据点未参与 Calibration。|
| 数据下限假设 | 隐含 | 本模型把 NBS 数据当下限 + 紧约束。如要做"上限"反推（瞒报 / 漏报），需新增 InflationFactor 参数。|

## 8. 与父项目方法论的对应

- **可见性反比例 + 拟合外推**（PHILOSOPHY §Stage 3）：本工具的反事实模式即是该方法的最小实现。把"可见的官方数据"作为下限 → 把"显著偏离基线的反事实"作为可见的外推候选 → 看哪个反事实假设产出与其他独立证据（社会观察、间接指标）最吻合。
- **数据可追溯**（PHILOSOPHY §1）：每份 CSV 顶部声明来源 + caveat；模型代码每个 stub 处明示当前假设。
- **AI 污染意识**（PHILOSOPHY §2）：本工具不调用 LLM；所有计算可复现，可单测。
