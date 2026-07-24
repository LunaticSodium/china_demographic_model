using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ChinaDemographicModel.App.Themes;

namespace ChinaDemographicModel.App.Controls;

/// 轻量折线图（自绘，替代 ScottPlot；无外部依赖 → 跨平台 / trimming 友好）。
/// 用法：Clear() → SetTitle() → Add(series...) → SetMarker(year) → Commit()。
public class MiniLineChart : Control
{
    public sealed class Series
    {
        public double[] Xs = Array.Empty<double>();
        public double[] Ys = Array.Empty<double>();
        public Color Color;
        public bool Dashed;
        public bool Markers;
        public string Label = "";
    }

    private string _title = "";
    private readonly List<Series> _series = new();
    private double? _markerX;

    public void SetTitle(string t) => _title = t;
    public void Clear() { _series.Clear(); _markerX = null; }
    public void Add(Series s) => _series.Add(s);
    public void SetMarker(double x) => _markerX = x;
    public void Commit() => InvalidateVisual();

    private static readonly Typeface Face = new("Segoe UI");

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        ctx.FillRectangle(Palette.BgSurface, new Rect(0, 0, w, h));

        DrawText(ctx, _title, 46, 4, 11, Palette.TextSecondary);

        double mL = 46, mR = 10, mT = 22, mB = 20;
        double plotW = w - mL - mR, plotH = h - mT - mB;
        if (plotW < 10 || plotH < 10) return;

        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var s in _series)
            for (int i = 0; i < s.Xs.Length; i++)
            {
                xMin = Math.Min(xMin, s.Xs[i]); xMax = Math.Max(xMax, s.Xs[i]);
                yMin = Math.Min(yMin, s.Ys[i]); yMax = Math.Max(yMax, s.Ys[i]);
            }
        if (_markerX is double mkx) { xMin = Math.Min(xMin, mkx); xMax = Math.Max(xMax, mkx); }
        if (xMin == double.MaxValue) return;  // 无数据
        if (xMax <= xMin) xMax = xMin + 1;
        if (yMax <= yMin) yMax = yMin + 1;
        double yPad = (yMax - yMin) * 0.08;
        yMin -= yPad; yMax += yPad;

        double X(double x) => mL + (x - xMin) / (xMax - xMin) * plotW;
        double Y(double y) => mT + (1 - (y - yMin) / (yMax - yMin)) * plotH;

        var gridPen = new Pen(Palette.BorderCol, 0.5) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) };
        for (int i = 0; i <= 4; i++)
        {
            double yv = yMin + (yMax - yMin) * i / 4;
            double py = Y(yv);
            ctx.DrawLine(gridPen, new Point(mL, py), new Point(w - mR, py));
            DrawText(ctx, yv.ToString("0.##", CultureInfo.InvariantCulture), 2, py - 6, 9, Palette.TextMuted);
        }
        for (int i = 0; i <= 4; i++)
        {
            double xv = xMin + (xMax - xMin) * i / 4;
            double px = X(xv);
            DrawText(ctx, ((int)Math.Round(xv)).ToString(), px - 14, h - mB + 3, 9, Palette.TextMuted);
        }

        if (_markerX is double mk)
        {
            var mpen = new Pen(new SolidColorBrush(Palette.Salmon, 0.7), 1) { DashStyle = new DashStyle(new double[] { 1, 3 }, 0) };
            ctx.DrawLine(mpen, new Point(X(mk), mT), new Point(X(mk), h - mB));
        }

        foreach (var s in _series)
        {
            if (s.Xs.Length == 0) continue;
            var brush = new SolidColorBrush(s.Color);
            var pen = new Pen(brush, s.Dashed ? 1 : 2);
            if (s.Dashed) pen.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
            for (int i = 1; i < s.Xs.Length && i < s.Ys.Length; i++)
                ctx.DrawLine(pen, new Point(X(s.Xs[i - 1]), Y(s.Ys[i - 1])), new Point(X(s.Xs[i]), Y(s.Ys[i])));
            if (s.Markers)
                for (int i = 0; i < s.Xs.Length && i < s.Ys.Length; i++)
                    ctx.DrawEllipse(null, new Pen(brush, 1), new Point(X(s.Xs[i]), Y(s.Ys[i])), 2, 2);
        }

        // 图例（右上，逆序摆放）
        double lx = w - mR;
        for (int i = _series.Count - 1; i >= 0; i--)
        {
            var s = _series[i];
            var ft = Fmt(s.Label, 9, new SolidColorBrush(s.Color));
            lx -= ft.Width;
            ctx.DrawText(ft, new Point(lx, 4));
            lx -= 10;
        }
    }

    private static FormattedText Fmt(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);

    private static void DrawText(DrawingContext ctx, string text, double x, double y, double size, IBrush brush)
        => ctx.DrawText(Fmt(text, size, brush), new Point(x, y));
}
