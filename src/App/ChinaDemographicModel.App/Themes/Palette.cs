using Avalonia.Media;

namespace ChinaDemographicModel.App.Themes;

/// 代码绘制（金字塔 / YearSlider / 时间序列）用的固定调色板。
/// 与 Themes/Tokens.axaml 的设计令牌数值一致；代码里直接用避免运行时资源查找。
public static class Palette
{
    private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static readonly IBrush BgBase = B("#FF0B1220");
    public static readonly IBrush BgSurface = B("#FF111827");
    public static readonly IBrush BgCard = B("#FF1E293B");
    public static readonly IBrush BgElev = B("#FF334155");
    public static readonly IBrush BorderCol = B("#FF334155");
    public static readonly IBrush BorderStrong = B("#FF475569");
    public static readonly IBrush TextPrimary = B("#FFF1F5F9");
    public static readonly IBrush TextSecondary = B("#FF94A3B8");
    public static readonly IBrush TextMuted = B("#FF64748B");
    public static readonly IBrush AccentPrimary = B("#FF38BDF8");
    public static readonly IBrush AccentTertiary = B("#FFFB7185");
    public static readonly IBrush Warn = B("#FFFBBF24");
    public static readonly IBrush Success = B("#FF4ADE80");
    public static readonly IBrush Census = B("#FF4ADE80");
    public static readonly IBrush Forecast = B("#FF67E8F9");

    // 时间序列线色
    public static readonly Color LightSteelBlue = Color.Parse("#FFB0C4DE");
    public static readonly Color LightSkyBlue = Color.Parse("#FF87CEFA");
    public static readonly Color Salmon = Color.Parse("#FFFA8072");
}
