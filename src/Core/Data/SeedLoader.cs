using System.Globalization;
using ChinaDemographicModel.Core.Models;

namespace ChinaDemographicModel.Core.Data;

/// 从 data/seed/*.csv 加载年度标量序列 + 普查年金字塔。
/// CSV 格式：第一行 header，后续行数据。'#' 开头的行 / 空行忽略。
public static class SeedLoader
{
    public static Dictionary<int, double> LoadYearlyScalar(string csvPath, string valueColumn)
    {
        var result = new Dictionary<int, double>();
        if (!File.Exists(csvPath)) return result;
        foreach (var (cols, dict) in ReadCsv(csvPath))
        {
            if (!dict.TryGetValue("year", out var yStr) || !int.TryParse(yStr, out int year)) continue;
            if (!dict.TryGetValue(valueColumn, out var vStr)) continue;
            if (double.TryParse(vStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                result[year] = v;
        }
        return result;
    }

    public static PopulationPyramid LoadCensusPyramid(string csvPath, int year)
    {
        var p = new PopulationPyramid { Year = year };
        if (!File.Exists(csvPath)) return p;
        foreach (var (_, dict) in ReadCsv(csvPath))
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

    private static IEnumerable<(string[] Cols, Dictionary<string, string> Dict)> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) yield break;
        var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        for (int i = 1; i < lines.Length; i++)
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
