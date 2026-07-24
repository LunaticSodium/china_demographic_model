using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ChinaDemographicModel.App.Themes;

namespace ChinaDemographicModel.App.Controls;

/// 自定义年份 slider（port 自 WPF）。
/// - 轨道按四档着色：一般年 / 普查年 / 预测年（> LastObservedYear）/ 反事实（IsCounterfactual=true 整段染色）。
/// - 指针点击 / 拖动定位；松开停止；离开仍生效（pointer capture）。
/// - Value / Minimum / Maximum 是 StyledProperty，可双向 binding。
public partial class YearSlider : UserControl
{
    public static readonly StyledProperty<int> MinimumProperty =
        AvaloniaProperty.Register<YearSlider, int>(nameof(Minimum), 1982);
    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<YearSlider, int>(nameof(Maximum), 2050);
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<YearSlider, int>(nameof(Value), 2020, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> LastObservedYearProperty =
        AvaloniaProperty.Register<YearSlider, int>(nameof(LastObservedYear), 2024);
    public static readonly StyledProperty<IEnumerable?> CensusYearsProperty =
        AvaloniaProperty.Register<YearSlider, IEnumerable?>(nameof(CensusYears));
    public static readonly StyledProperty<bool> IsCounterfactualProperty =
        AvaloniaProperty.Register<YearSlider, bool>(nameof(IsCounterfactual));

    public int Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int LastObservedYear { get => GetValue(LastObservedYearProperty); set => SetValue(LastObservedYearProperty, value); }
    public IEnumerable? CensusYears { get => GetValue(CensusYearsProperty); set => SetValue(CensusYearsProperty, value); }
    public bool IsCounterfactual { get => GetValue(IsCounterfactualProperty); set => SetValue(IsCounterfactualProperty, value); }

    private bool _dragging;

    public YearSlider()
    {
        InitializeComponent();
        TrackCanvas.Cursor = new Cursor(StandardCursorType.Hand);
        TrackCanvas.PointerPressed += OnPointerPressed;
        TrackCanvas.PointerMoved += OnPointerMoved;
        TrackCanvas.PointerReleased += OnPointerReleased;
        TrackCanvas.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MinimumProperty || change.Property == MaximumProperty ||
            change.Property == ValueProperty || change.Property == LastObservedYearProperty ||
            change.Property == CensusYearsProperty || change.Property == IsCounterfactualProperty)
        {
            Redraw();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        e.Pointer.Capture(TrackCanvas);
        SetValueFromPointer(e.GetPosition(TrackCanvas).X);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        SetValueFromPointer(e.GetPosition(TrackCanvas).X);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void SetValueFromPointer(double x)
    {
        double w = TrackCanvas.Bounds.Width;
        if (w < 4 || Maximum <= Minimum) return;
        double t = Math.Clamp(x / w, 0, 1);
        int newVal = Minimum + (int)Math.Round(t * (Maximum - Minimum));
        if (newVal != Value) Value = newVal;
    }

    private void Redraw()
    {
        TrackCanvas.Children.Clear();
        LabelCanvas.Children.Clear();

        double w = TrackCanvas.Bounds.Width;
        double h = TrackCanvas.Bounds.Height;
        if (w < 10 || h < 10) return;
        if (Maximum <= Minimum) return;

        int totalYears = Maximum - Minimum + 1;
        double yearWidth = w / totalYears;
        double trackH = 8;
        double trackY = (h - trackH) / 2.0 + 4;

        var censusSet = new HashSet<int>();
        if (CensusYears is IEnumerable ce)
        {
            foreach (var o in ce)
            {
                if (o is int i) censusSet.Add(i);
                else if (int.TryParse(o?.ToString(), out int parsed)) censusSet.Add(parsed);
            }
        }

        IBrush brushGeneral = Palette.BgElev;
        IBrush brushCensus = Palette.Census;
        IBrush brushForecast = Palette.Forecast;
        IBrush brushCounterfactual = Palette.Warn;
        IBrush textMuted = Palette.TextMuted;
        IBrush textBright = Palette.TextPrimary;

        for (int y = Minimum; y <= Maximum; y++)
        {
            IBrush b;
            double thisH = trackH;
            double thisY = trackY;
            if (IsCounterfactual) b = brushCounterfactual;
            else if (y > LastObservedYear) b = brushForecast;
            else if (censusSet.Contains(y)) { b = brushCensus; thisH = trackH + 6; thisY = trackY - 3; }
            else b = brushGeneral;

            var rect = new Rectangle
            {
                Width = Math.Max(1, yearWidth + 0.5),
                Height = thisH,
                Fill = b,
                Opacity = censusSet.Contains(y) && !IsCounterfactual ? 1.0 : 0.85,
            };
            Canvas.SetLeft(rect, (y - Minimum) * yearWidth);
            Canvas.SetTop(rect, thisY);
            TrackCanvas.Children.Add(rect);
        }

        double valX = (Value - Minimum) * yearWidth + yearWidth / 2.0;
        var indicator = new Polygon
        {
            Points = new List<Point> { new(0, 0), new(10, 0), new(5, 8) },
            Fill = textBright,
        };
        Canvas.SetLeft(indicator, valX - 5);
        Canvas.SetTop(indicator, trackY - 12);
        TrackCanvas.Children.Add(indicator);

        var thumbLine = new Rectangle
        {
            Width = 2, Height = trackH + 16,
            Fill = textBright, RadiusX = 1, RadiusY = 1,
        };
        Canvas.SetLeft(thumbLine, valX - 1);
        Canvas.SetTop(thumbLine, trackY - 4);
        TrackCanvas.Children.Add(thumbLine);

        var ticks = new HashSet<int>(censusSet) { Minimum, Maximum, LastObservedYear };
        for (int y = ((Minimum / 10) + 1) * 10; y < Maximum; y += 10) ticks.Add(y);
        foreach (int y in ticks)
        {
            if (y < Minimum || y > Maximum) continue;
            double x = (y - Minimum) * yearWidth + yearWidth / 2.0;
            var tb = new TextBlock
            {
                Text = y.ToString(),
                Foreground = censusSet.Contains(y) ? textBright : textMuted,
                FontSize = 10,
                FontWeight = censusSet.Contains(y) ? FontWeight.SemiBold : FontWeight.Normal,
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, 0);
            LabelCanvas.Children.Add(tb);
        }
    }
}
