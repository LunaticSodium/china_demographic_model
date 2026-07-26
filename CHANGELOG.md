# 变更记录

开发者向的详细变更记录。GitHub Releases 页面只放安装包，细节看这里。

## v1.3.1

- **修复 macOS dmg 无法打开**。此前 .app 完全未签名，Apple Silicon 上从网络下载（带 quarantine 标记）会直接报「应用已损坏」。现在 CI 对 bundle 做 ad-hoc 签名（`codesign --force --deep --sign -`）并校验。
- macOS 仅提供 Apple Silicon (`osx-arm64`) 预编译 dmg；现役 Mac 基本都是 M 系列，Intel 用户可自行 `-r osx-x64` 从源码构建。
- macOS bundle 的 `CFBundleIdentifier` 与 Android/iOS 统一为 `io.github.lunaticsodium.chinademographic`（此前是 `com.demo.…` 占位）。
- README 改写为项目自述；详细变更移入本文件。

## v1.3.0

- **手机端响应式布局**。此前 apk 沿用桌面三栏（280 + 内容 + 340），手机上中间区域被挤没。现在 <900 逻辑像素折叠为单栏：左右栏收起，内容改为「设置」「指标」标签页，年份滑条独占整行，副标题 / 图例 / 顶栏按钮让位。
  - 左右栏抽成 `Views/ControlsPanel` 与 `Views/MetricsPanel`，两种布局复用同一份定义。
  - `ControlsPanel.ShowActions`：窄屏「设置」页内置「重跑投影 / 重置基线」（顶栏按钮此时隐藏），宽屏不重复。
  - 坑：`x:Name` 加在 `ColumnDefinition` 上不会生成字段（它不是控件），需按索引访问。
- **包名规范化**：`com.CompanyName.*` → `io.github.lunaticsodium.chinademographic`（Android `ApplicationId` + iOS `CFBundleIdentifier`）。改包名会让 apk 被当作新应用安装，需先卸载旧版。
- 验证：跨 1500 / 1000 / 899 / 470 / 360 px 程序化断言列宽、面板显隐、标签页显隐、操作按钮归属。

## v1.2.2

- **修复金字塔条形渲染粘连**。`rowH = h/101` 为小数，条带 y 与高度落在非整数像素上，抗锯齿把边缘摊成两个半透明像素、填掉 1px 缝隙；各行小数部分不同，于是有的粘有的不粘。改为按**设备像素对齐**（DPI 感知）：行高取整数物理像素、固定留 1 物理像素缝、整体居中；条带关闭抗锯齿；条带过薄时去掉圆角。绘制与点击命中共用同一布局，杜绝点错行。
  - 未采用「固定缩放 + Viewbox」：非整数倍拉伸会让边缘重新落在半像素上（均匀地糊），且面板文字会跟着缩放。
- **死亡侧校准外推稳健化**。k(y) 实测形状为三段：1983–2004 平（≈1.0）→ 2004–2013 陡升至 1.25 → 2013 至今在 1.23–1.30 平台化。故末窗口线性 R²≈0.24 不是拟合差，而是已无趋势可拟合；拉长窗口把 R² 做高（30 年 0.88）反而会外推旧爬坡段。改为**稳健水平锚（末 5 年均值）+ 按解释力收缩的斜率**（slope × R²），并改报 RMSE（0.018）。边界跳变 +0.7% → +0.4%。

## v1.2.1

- **修复死亡模型 2020/21 跳变**。`CensusLifeTables.GetQx` 此前只在普查区间**之外**做 Brass e0 校准：2020 用未校准原表（隐含 e0 = 68.99/74.94，比公布的 75.4/80.9 低 6 岁），2021 起突然对齐 e0 → 死亡数 1257万 跌到 702万（−44%）。改为**所有年份统一校准**：普查表提供年龄形状，水平由公布 e0 决定。
- **新增死亡侧拟合校准**（对称于出生侧观测锁）。q(x) 校准正确后模型死亡仍比 NBS 低 10–15%，根因是 CCM 金字塔 65+ 比七普薄 16.9%（同一 q(x) 换用七普结构 → 1040万 vs NBS 998万）。新增 `Calibrator.AlignDeathsToHistory` + `MortalityCalibration`：观测年缩放 q(x) 使模型死亡精确等于 NBS；预测年 k(y) 经拟合 + 阻尼外推 + clamp。
- **幂等性修复**：`AlignDeathsToHistory` 就地改写并持久化 q(x)，导致第二次「重跑投影」时 k 退化为 1、预测年修正丢失、跳变复现。改为观测年每次从生命表重建基准 q(x)。
- 补齐 `YearMax`(2050) 的输入向量，右栏指标不再显示「—」。

## v1.2.0

- **修复数据编辑不生效**。`ApplyEdits` 只写 `InputsByYear` 并发通知，从不重跑投影，金字塔读的是旧 `ProjectedByYear`，时间序列（监听 `ProjectionStamp`）更完全不刷新。现在应用编辑后立即重跑投影。
- **出生数 ↔ TFR 联动**。二者此前可独立设置且相互矛盾：投影器在 `TotalBirths > 0` 时直接采用、无视 ASFR，故 TFR 编辑被静默忽略。现按 births = TFR × K 双向联动（K = Σ 育龄女性 × 归一化 ASFR 形状）。
- 历史锁下编辑观测年时明确提示已回退到 NBS 观测值（需克隆场景才能改历史）。
- 边界年（`InputsByYear` 中不存在的年份）新建输入时从邻近年克隆死亡率 / ASFR，避免零死亡率导致人口虚增。

## v1.1.0-preview1

- 首个跨平台构建：WPF UI 移植到 Avalonia，一份代码产出 Windows / Android / macOS 安装包。
- `Core` 引擎（net8.0）原样复用；seed CSV 改为内嵌资源（`HistoricalSeries.LoadEmbedded`），因 Android/iOS 无相邻文件系统。
- 时间序列改为自绘 `MiniLineChart`，去掉 ScottPlot 依赖（ScottPlot.Avalonia 仅支持 Avalonia 11，与本项目的 12 不兼容），同时减少移动端依赖面。
- GitHub Actions：打 `v*` tag 即构建全平台并发布 Release。
- 主题：Fluent 深色底 + 设计令牌 + 语义样式类；`Border.card.forecast` 取代 WPF 的 `DataTrigger`。

## v1.0.0 及更早

见各版本 Release 页与 `docs/`。核心为 WPF 版：CCM 投影、历史锁、反事实场景、多预测模型、普查生命表。
