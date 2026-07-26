# 中国人口结构 · 工作台

[![Release](https://img.shields.io/github/v/release/LunaticSodium/china_demographic_model)](https://github.com/LunaticSodium/china_demographic_model/releases)

一个可以"篡改历史"的中国人口推演工具。

## 这是什么

给定 1978 年至今的出生、死亡、性别比、结婚率、预期寿命等观测序列，用 cohort-component 方法逐年推演出完整的人口年龄结构，并允许你提出反事实假设——如果某几年的出生数不是官方公布的那个数字，今天的劳动年龄人口会是什么样？

它的立场是**紧模型**：在锁定历史的模式下，所有已观测年份必须精确复现 NBS 公布的总量，模型没有自由发挥的余地。系统实际上是欠定的，工具不假装唯一解，只是明确地选一个（比例缩放），并把这个选择写在文档里。想突破这条线，就克隆一个场景、关掉历史锁——"在历史之内建模"和"对着历史建模"是两件事，不该混为一谈。

预测年（2026 起）则交给可切换的模型：OLS 趋势外推、末值常数、阻尼趋势。数据是数据，模型是数学操作，切换模型不改输入，编辑输入不改模型结构。

这是父项目《2025 年前后的中国社会阶级情况调查》的 Stage 3 工具链。

## 拿来用

到 [Releases](https://github.com/LunaticSodium/china_demographic_model/releases) 下载：

| 平台 | 说明 |
|---|---|
| Windows | 自包含单文件 exe，双击即可，无需安装 .NET |
| Android | apk 侧载（需在系统里允许安装未知来源应用） |
| macOS | dmg，仅 Apple Silicon（M 系列）。Intel Mac 未提供预编译包，可自行 `-r osx-x64` 从源码构建 |

macOS 上应用只做了 ad-hoc 签名、没有 Apple 公证（那需要付费开发者账号），所以首次打开会被拦一次。把 app 拖进「应用程序」后：

```sh
xattr -dr com.apple.quarantine /Applications/ChinaDemographicModel.app
```

或者直接双击，然后到 `系统设置 → 隐私与安全性` 点「仍要打开」。

从源码跑（需 .NET 8 SDK 或更高）：

```pwsh
git clone https://github.com/LunaticSodium/china_demographic_model.git
cd china_demographic_model
dotnet run --project src/App/ChinaDemographicModel.App.Desktop   # 跨平台版
dotnet run --project src/UI/UI.csproj                            # 原 WPF 版（仅 Windows）
```

## 数据立场

对中国官方人口数据，**方法论可质疑，数值层面全盘采纳**——没有可比可信度的替代源，独立重构的标准误差比公布序列更大。

所以：口径修正一律用显式命名的函数（`PopulationAlignment`、`MortalityCalibration`、`AlignDeathsToHistory`），不藏在私有 helper 里；文档写明修正的存在、输入与假设；界面显示修正后的结果。缺失年份做显式线性插值，绝不把速率当常数外推。

基础锚点是 NBS 年末数据、民政部婚姻统计、五次全国人口普查。完整的多源对照见 `docs/CROSS_SOURCE_REFS.md`，设计哲学见 `PHILOSOPHY.md`，模型公式与时间约定见 `docs/MODEL.md`。

## 已知的不足

模型金字塔的 65 岁以上人口比七普实测薄约 17%，这是四十年 CCM 推演累积的形状残差。死亡侧用显式校准系数吸收了它，使观测年死亡数与 NBS 精确一致，但根治需要在普查年做形状重锚定（IPF）——那会打破 cohort 连续性，是一个尚未决定的权衡（见 `docs/AUDIT.md` §1）。

其余待办见 `docs/BACKLOG.md`，变更记录见 `CHANGELOG.md`。

## License

私人项目，未授权重发。
