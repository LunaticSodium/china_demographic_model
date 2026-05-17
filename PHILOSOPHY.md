# Project Philosophy / 项目设计哲学

This document records the founding intent of this project as expressed by the originator. It is the **canonical reference** for what this tool is and is not.

---

## 0. Founding prompt (verbatim, 2026-05-17)

> 写个图形化的软件，中国人口结构的。自己扒一下历年出生人口，男女比，结婚率之类的。我们还需要平均初婚年龄，生育预期年龄概率分布等等作为输入量，并尝试匹配各个输入量和人口结构之间的关系，允许我们通过假设"篡改"过去以及预测未来。注意在建模阶段，一切数据都必须符合历史，也就是说这是个紧模型，尽管其实际上可能欠定，但必须有非常事实的分析。在根目录下面新建文件夹来解决这个问题吧，我们假设用visual studio作为测试工具，.cs或者类似的做后端，前端语言选个合适的，要求顺滑，好看，具有合适科技感和轻微的圆角造成的舒适感，注重设计语言并在操作上做到符合直觉。

### English rendering

> Build a graphical application about China's demographic structure. Gather historical data yourself — annual births, sex ratio at birth, marriage rate, and so on. We also need inputs like mean age at first marriage and the probability distribution of fertility ages. Try to fit the relationships between these inputs and the resulting demographic structure, and let us "tamper with" the past or predict the future under hypothetical inputs. Important: during modeling, **all data must be consistent with history** — this is a **tight model**. The system is in fact underdetermined, but the analysis must remain fact-grounded. Put it in a new subfolder in the project root. Assume Visual Studio as the test tool, C# (or similar) for the backend, pick a suitable frontend language. The UI should feel smooth, look good, have appropriate technological feel with mild rounded corners for comfort, attend to design language, and be intuitive to operate.

---

## 1. Design constraints extracted from the founding prompt

The prompt is dense; below is the decomposition that this codebase implements.

| Phrase from prompt | Design constraint in code |
|---|---|
| "图形化的软件" | Desktop GUI app. **WPF (.NET 8) on Windows**, chosen for VS-native tooling and mature chart libraries. |
| "中国人口结构的" | Domain = China demographics. Seed data targeted to NBS / Census / 民政部 / DSP sources, 1978–2024. |
| "自己扒一下历年出生人口，男女比，结婚率" | Self-contained data layer. `data/seed/*.csv` with provenance + caveats, replaceable by users with original source files. |
| "平均初婚年龄、生育预期年龄概率分布" | Inputs go beyond aggregate rates: `FertilityModel` builds ASFR(15..49) from (TFR, mean age at first marriage), exposes σ + first-birth lag as parameters. |
| "尝试匹配各个输入量和人口结构之间的关系" | Forward CCM engine + ScenarioBuilder reverse-engineer baseline inputs from observed totals. `Calibrator` solves the tight constraint. |
| "允许我们通过假设'篡改'过去以及预测未来" | Scenario cloning + per-year `DemographicInputs` editing. Counterfactual scenarios run on the same engine as baseline; `EditedYears` set tracks divergence. |
| "在建模阶段，一切数据都必须符合历史" | `LockToHistory = true` is the default. `RunProjection` re-applies observed `TotalBirths` / `SRB` / `CrudeMarriageRate` to inputs **after** any user edit, then rescales ASFR via `Calibrator.AlignBirthsToHistory` so model-predicted births == observed. |
| "紧模型" (tight model) | The model is structurally over-constrained relative to the data: a single value (annual births) controls a multi-dimensional unknown (ASFR shape). See §2. |
| "尽管其实际上可能欠定" | Acknowledged. The codebase does not pretend uniqueness — it picks one solution (proportional scaling) and the docs/MODEL.md says so explicitly. |
| "必须有非常事实的分析" | No fabricated time series. Where data is missing, `ScenarioBuilder.LookupOrInterp` does explicit linear interpolation between observed anchors, never extrapolates rates as constants. |
| "Visual Studio 作为测试工具" | `.sln` + 2 `.csproj` layout. Builds cleanly with `dotnet build`; opens cleanly in VS 2022+. |
| ".cs 后端 / 前端选合适的" | C# everywhere. Backend in Core class library; frontend in WPF (XAML + C# code-behind + CommunityToolkit.Mvvm). |
| "顺滑，好看，科技感，轻微圆角" | Custom dark-slate theme. 12px panel corners, 8px button corners, 4px chip corners. Slate-900 base / Sky-400 accent / Rose-400 contrast. Pyramid uses 1.5px micro-rounding on bars. |
| "注重设计语言并在操作上符合直觉" | Three-column layout enforces information hierarchy (control → visualization → metrics). Year scrubber is the primary affordance. Counterfactual toggle is explicit (checkbox), never implicit. |

---

## 2. The tight-model commitment

This is the most load-bearing constraint. Restating it:

**Historical years (1978–2024 in the current seed) must reproduce observed totals exactly.** The user can edit anything they want at the input layer, but when `LockToHistory = true`, the projection routine will overwrite their edits to known-observable fields before stepping the CCM. What the user *can* counterfactually change while staying tight:

- The **shape** of the fertility schedule (TFR allocation across ages 15..49)
- The **shape** of the mortality schedule (life-table form, e0)
- Behavioral mediators not yet wired in (marriage rate, mean age at first marriage)
- Anything in **post-observed years** (2025+, fully free)

What the user **cannot** change while staying tight:

- Annual total births (1978–2024 NBS series)
- Annual sex ratio at birth (census + 1% sample anchors)
- Annual crude marriage rate (民政部 series, 2002+)
- Any census-year age structure (when re-anchoring is enabled)

To cross those lines, **clone the baseline scenario**. Cloned scenarios default to `LockToHistory = false`. This is the explicit fork between "modeling within history" and "modeling against history" — both legitimate, but never confused.

---

## 3. What this project is **not**

- **Not a real-time data scraper.** Seed CSVs are static. Re-running against fresh data is a manual replace-and-rebuild loop. (Future: add a `DataFetcher` that pulls from NBS or WPP, but only behind an explicit user action.)
- **Not a forecast competition entry.** No comparison to UN WPP, World Bank, or Wittgenstein Centre is performed automatically. Users can manually load alternative series as new scenarios.
- **Not a microsimulation.** Cohort-component is aggregate; we never model individuals.
- **Not a regional/urban-rural model.** Single national aggregate. Provincial / hukou splits are out of MVP scope. (Acknowledged loss of signal — see `docs/MODEL.md` §6.)
- **Not a substitute for raw census reading.** Underlying CSV values are the project's reading of NBS publications; users should treat them as a starting point, not as authoritative.

---

## 4. Relationship to parent project

This tool lives inside a larger research project on Chinese class structure circa 2025 (one directory up). The parent project's PHILOSOPHY.md establishes:

- **Default distrust of official / semi-official sources** on platform / labor / class topics.
- **Visibility-inverse + curve-fitting extrapolation** as the methodological core of Stage 3.

This demographic model is **Stage 3 tooling**. It treats published demographic data as a **lower bound on truth**, not as truth itself — but for fertility and mortality specifically, the public series have higher independent-cross-validation than e.g. household income or class-share data. So this tool implements the tight-model commitment with public data as the anchor, and exposes counterfactual editing as the analytic move.

A user who believes the official 2017–2019 birth numbers are inflated can clone the baseline, edit those years downward, and observe the structural consequence in 2025 working-age population. That is the intended workflow.

---

## 5. Data: 口径质疑 / 数值采纳

A refinement of the broader "official sources are a lower bound, not truth" stance for **demographic / macro data specifically**:

- **At the methodology level** (口径) —— skepticism is appropriate. Examples: NBS year-end estimates vs census enumeration differ by 0.5–3% (the census often finds more people); 1980s–2000s SRB published values likely undercount female-infant underreporting; 2017–2019 birth totals are widely suspected of inflation; the 2023 death total of 1110万 is treated by independent estimators as a lower bound on COVID excess mortality.
- **At the value level** (数值) —— **fully adopted**. There is no alternative source with comparable credibility. Independent reconstructions (Yi Fuxian, Liang Jianzhang) have standard errors larger than the published series.

Operational consequence:

- Any methodology-adjustment code must be **explicitly named**: classes / methods carry words like `Alignment`, `Correction`, `Adjustment`. No magic numbers buried in private helpers.
- Docs (this file, `docs/MODEL.md`, `data/sources.md`, `README.md`) must state the adjustment exists, its inputs, its assumptions.
- UI shows the **adjusted** result. The original / pre-adjustment value is not displayed alongside (to avoid forcing readers to mediate two numbers).
- Form of adjustment: take two officially-published numbers under different 口径 for the same population (e.g., NBS year-end estimate vs census enumeration), compute the ratio, apply as a multiplicative correction to the year range.

This is the principle behind `src/Core/Engine/PopulationAlignment.cs` (NBS year-end alignment of CCM-projected pyramid totals).

See also: parent project's `data_official_value_adoption` memory; the distinction from `sources_official_distrust_platform_labor` (which targets platform / labor data where political incentives to suppress are stronger).

## 6. Versioning of this document

This file is **append-only** for the founding prompt section (§0). The interpretation sections (§1–§4) may evolve as the codebase does, but the original prompt is preserved verbatim as the historical record of intent.
