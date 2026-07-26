# 中国人口结构 · 工作台

[![Release](https://img.shields.io/github/v/release/LunaticSodium/china_demographic_model)](https://github.com/LunaticSodium/china_demographic_model/releases)

用 cohort-component 方法推演中国人口年龄结构（1978–2050），可以修改任意年份的输入做反事实对比。

C# / Avalonia，支持 Windows、Android、macOS。

## 下载

[Releases](https://github.com/LunaticSodium/china_demographic_model/releases)

- **Windows** — exe，双击运行
- **Android** — apk，侧载
- **macOS** — dmg，仅 Apple Silicon。首次打开在 `系统设置 → 隐私与安全性` 点「仍要打开」

## 从源码运行

需要 .NET 8 SDK 或更高。

```sh
dotnet run --project src/App/ChinaDemographicModel.App.Desktop
```

## 数据

NBS 统计年鉴、五次全国人口普查、民政部婚姻统计。观测年锁定官方公布值，缺失年份线性插值。

## 文档

- `PHILOSOPHY.md` — 设计立场
- `docs/MODEL.md` — 模型公式与时间约定
- `docs/AUDIT.md` — 方法学自查与已知偏差
- `CHANGELOG.md` — 变更记录

## License

私人项目，未授权重发。
