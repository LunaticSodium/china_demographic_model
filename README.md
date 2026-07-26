# 中国人口结构 · 工作台

[![Release](https://img.shields.io/github/v/release/LunaticSodium/china_demographic_model)](https://github.com/LunaticSodium/china_demographic_model/releases)

按 cohort-component 方法计算 1978–2050 年中国人口年龄结构。可修改任意年份的出生数、出生性别比、总和生育率、平均初婚年龄、预期寿命等输入，重新计算并与基线对比。

## 系统要求

- Windows 10 及以上，64 位
- Android 5.0 及以上
- macOS 11 及以上，Apple Silicon

## 安装

从 [Releases](https://github.com/LunaticSodium/china_demographic_model/releases) 下载对应平台的文件。

**Windows**：下载 `.exe`，双击运行。无需安装 .NET 运行时。

**Android**：下载 `.apk`，在系统设置中允许安装未知来源的应用，然后打开该文件。

**macOS**：下载 `.dmg`，打开后将应用拖入「应用程序」文件夹。首次启动会被系统拦截，在「系统设置 → 隐私与安全性」中点击「仍要打开」。

## 从源码构建

需要 .NET 8 SDK 或更高版本。

```sh
git clone https://github.com/LunaticSodium/china_demographic_model.git
cd china_demographic_model
dotnet run --project src/App/ChinaDemographicModel.App.Desktop
```

## 数据来源

国家统计局《中国统计年鉴》、第三至第七次全国人口普查、民政部婚姻登记统计。观测年份使用官方公布值，缺失年份线性插值。

## 文档

- `PHILOSOPHY.md` — 设计立场
- `docs/MODEL.md` — 模型公式与时间约定
- `docs/AUDIT.md` — 方法学自查与已知偏差
- `CHANGELOG.md` — 变更记录

## 许可

私人项目，未经授权不得再分发。
