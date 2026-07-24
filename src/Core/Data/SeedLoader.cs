using System.Globalization;
using System.Reflection;
using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Data;

/// 从 data/seed/*.csv 加载年度标量序列 + 普查年金字塔。
/// CSV 格式：第一行 header，后续行数据。'#' 开头的行 / 空行忽略。
///
/// 两条加载路径，共用同一套解析逻辑：
///   - 文件路径版（LoadYearlyScalar / LoadCensusPyramid）—— 桌面 / WPF，从 data/seed 目录读盘。
///   - 文本版（...FromText）+ 内嵌资源版（ReadEmbeddedText）—— Android / iOS 等无相邻文件系统的平台，
///     CSV 以 EmbeddedResource 形式打进 Core.dll，随引擎走到每个平台。
public static class SeedLoader
{
    public static Dictionary<int, double> LoadYearlyScalar(string csvPath, string valueColumn)
    {
        if (!File.Exists(csvPath)) return new Dictionary<int, double>();
        return LoadYearlyScalarFromLines(File.ReadAllLines(csvPath), valueColumn);
    }

    public static PopulationPyramid LoadCensusPyramid(string csvPath, int year)
    {
        if (!File.Exists(csvPath)) return new PopulationPyramid { Year = year };
        return LoadCensusPyramidFromLines(File.ReadAllLines(csvPath), year);
    }

    // ---- 文本 / 行数组版（平台无关）----

    public static Dictionary<int, double> LoadYearlyScalarFromText(string csvText, string valueColumn)
        => LoadYearlyScalarFromLines(SplitLines(csvText), valueColumn);

    public static PopulationPyramid LoadCensusPyramidFromText(string csvText, int year)
        => LoadCensusPyramidFromLines(SplitLines(csvText), year);

    private static Dictionary<int, double> LoadYearlyScalarFromLines(string[] lines, string valueColumn)
    {
        var result = new Dictionary<int, double>();
        foreach (var (_, dict) in ReadCsv(lines))
        {
            if (!dict.TryGetValue("year", out var yStr) || !int.TryParse(yStr, out int year)) continue;
            if (!dict.TryGetValue(valueColumn, out var vStr)) continue;
            if (double.TryParse(vStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                result[year] = v;
        }
        return result;
    }

    private static PopulationPyramid LoadCensusPyramidFromLines(string[] lines, int year)
    {
        var p = new PopulationPyramid { Year = year };
        foreach (var (_, dict) in ReadCsv(lines))
        {
            if (!dict.TryGetValue("age", out var aStr) || !int.TryParse(aStr, out int age)) continue;
            if (age < 0 || age > PopulationPyramid.MaxAge) continue;
            if (dict.TryGetValue("male", out var mStr) && double.TryParse(mStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double m))
                p.Male[age] = m;
            if (dict.TryGetValue("female", out var fStr) && double.TryParse(fStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double f))
                p.Female[age] = f;
        }
        return p;
    }

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    // ---- 内嵌资源（Android / iOS 及任意平台通用）----

    /// 读取打进 Core.dll 的 seed CSV。logicalName 形如 "seed.births_yearly.csv"
    /// 或 "seed.census.pyramid_2020.csv"（见 Core.csproj 的 EmbeddedResource LogicalName）。
    /// 找不到返回 null。
    public static string? ReadEmbeddedText(string logicalName)
    {
        var asm = typeof(SeedLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(logicalName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// 枚举所有内嵌普查金字塔资源名（"seed.census.*.csv"）。
    public static IEnumerable<string> EmbeddedCensusResourceNames()
        => typeof(SeedLoader).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("seed.census.", StringComparison.Ordinal) && n.EndsWith(".csv", StringComparison.Ordinal));

    private static IEnumerable<(string[] Cols, Dictionary<string, string> Dict)> ReadCsv(string[] lines)
    {
        if (lines.Length < 2) yield break;

        // 找到第一行非 # 注释、非空白行作为 header。
        // 之前的实现把 lines[0] 当 header，结果所有 CSV 顶部的 # 注释块把 header 识别错了，
        // 导致所有 data 字典加载为空——这是 round 1-3 隐藏的致命 bug，
        // 只在 round 3 用户报告"2024 显示 12亿"时才暴露。
        int headerIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
            headerIdx = i;
            break;
        }
        if (headerIdx < 0) yield break;

        // 移除 UTF-8 BOM（如有）
        var headerLine = lines[headerIdx];
        if (headerLine.Length > 0 && headerLine[0] == '﻿') headerLine = headerLine.Substring(1);

        var header = SplitCsv(headerLine).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        for (int i = headerIdx + 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
            var parts = SplitCsv(raw);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < header.Length && j < parts.Length; j++)
                dict[header[j]] = parts[j].Trim();
            yield return (parts, dict);
        }
    }

    private static string[] SplitCsv(string line)
    {
        // 极简 split — 不处理引号转义。seed CSV 简单，能用即可。
        return line.Split(',');
    }

    /// 在程序输出目录下找 data/seed/ 路径。
    public static string ResolveSeedDir()
    {
        string baseDir = AppContext.BaseDirectory;
        string p1 = Path.Combine(baseDir, "data", "seed");
        if (Directory.Exists(p1)) return p1;
        // dev 环境：bin/Debug/net8.0-windows → ../../../../../data/seed
        var probe = baseDir;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(probe, "data", "seed");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(probe);
            if (parent == null) break;
            probe = parent.FullName;
        }
        return p1; // fallback
    }
}
