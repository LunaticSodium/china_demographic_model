using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ChinaDemographicModel.UI.Controls;

/// 自定义年份 slider。
/// - 轨道按四档着色：一般年 / 普查年 / 预测年（> LastObservedYear）/ 反事实（IsCounterfactual=true 时整段染色）。
/// - 鼠标点击 / 拖动定位；松开停止；离开 canvas 仍生效。
/// - Value / Minimum / Maximum 是 DependencyProperty，可双向 binding。
public partial class YearSlider : UserControl
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(YearSlider),
        new FrameworkPropertyMetadata(1982, FrameworkPropertyMetadataOptions.AffectsRender, OnAnyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(YearSlider),
        new FrameworkPropertyMetadata(2050, FrameworkPropertyMetadataOptions.AffectsRender, OnAnyChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(YearSlider),
        new FrameworkPropertyMetadata(2020, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, OnAnyChanged));

    public static readonly DependencyProperty LastObservedYearProperty = DependencyProperty.Register(
        nameof(LastObservedYear), typeof(int), typeof(YearSlider),
        new FrameworkPropertyMetadata(2024, FrameworkPropertyMetadataOptions.AffectsRender, OnAnyChanged));

    public static readonly DependencyProperty CensusYearsProperty = DependencyProperty.Register(
        nameof(CensusYears), typeof(IEnumerable), typeof(YearSlider),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnAnyChanged));

    public static readonly DependencyProperty IsCounterfactualProperty = DependencyProperty.Register(
        nameof(IsCounterfactual), typeof(bool), typeof(YearSlider),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnAnyChanged));

    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int LastObservedYear { get => (int)GetValue(LastObservedYearProperty); set => SetValue(LastObservedYearProperty, value); }
    public IEnumerable? CensusYears { get => (IEnumerable?)GetValue(CensusYearsProperty); set => SetValue(CensusYearsProperty, value); }
    public bool IsCounterfactual { get => (bool)GetValue(IsCounterfactualProperty); set => SetValue(IsCounterfactualProperty, value); }

    private bool _dragging;

    public YearSlider()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is YearSlider s) s.Redraw();
    }

    private void TrackCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void TrackCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        TrackCanvas.CaptureMouse();
        SetValueFromMouse(e.GetPosition(TrackCanvas).X);
    }

    private void TrackCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        SetValueFromMouse(e.GetPosition(TrackCanvas).X);
    }

    private void TrackCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        TrackCanvas.ReleaseMouseCapture();
    }

    private void TrackCanvas_MouseLeave(object sender, MouseEventArgs e) { /* 仍由 capture 抓住 */ }

    private void SetValueFromMouse(double x)
    {
        double w = TrackCanvas.ActualWidth;
        if (w < 4 || Maximum <= Minimum) return;
        double t = Math.Clamp(x / w, 0, 1);
        int newVal = Minimum + (int)Math.Round(t * (Maximum - Minimum));
        if (newVal != Value) Value = newVal;
    }

    private void Redraw()
    {
        TrackCanvas.Children.Clear();
        LabelCanvas.Children.Clear();

        double w = TrackCanvas.ActualWidth;
        double h = TrackCanvas.ActualHeight;
        if (w < 10 || h < 10) return;
        if (Maximum <= Minimum) return;

        int totalYears = Maximum - Minimum + 1;
        double yearWidth = w / totalYears;
        double trackH = 8;
        double trackY = (h - trackH) / 2.0 + 4;

        // 取 census set
        var censusSet = new HashSet<int>();
        if (CensusYears is IEnumerable ce)
        {
            foreach (var o in ce)
            {
                if (o is int i) censusSet.Add(i);
                else if (int.TryParse(o?.ToString(), out int parsed)) censusSet.Add(parsed);
            }
        }

        // 4 色配色（用户 round 3 指定）：
        //   一般年   → 默认灰 (BgElevBrush)
        //   普查年   → 草绿 (CensusBrush = SuccessBrush)
        //   预测年   → 天青 (ForecastBrush)
        //   反事实段 → 鹅黄 (WarnBrush)
        Brush brushGeneral = (Brush)FindResource("BgElevBrush");
        Brush brushCensus = (Brush)FindResource("CensusBrush");
        Brush brushForecast = (Brush)FindResource("ForecastBrush");
        Brush brushCounterfactual = (Brush)FindResource("WarnBrush");
        Brush textMuted = (Brush)FindResource("TextMutedBrush");
        Brush textBright = (Brush)FindResource("TextPrimaryBrush");

        // 画每年分段
        for (int y = Minimum; y <= Maximum; y++)
        {
            Brush b;
            double thisH = trackH;
            double thisY = trackY;
            if (IsCounterfactual)
            {
                b = brushCounterfactual;
            }
            else if (y > LastObservedYear)
            {
                b = brushForecast;
            }
            else if (censusSet.Contains(y))
            {
                b = brushCensus;
                thisH = trackH + 6;
                thisY = trackY - 3;
            }
            else
            {
                b = brushGeneral;
            }

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

        // 当前 value 指示器
        double valX = (Value - Minimum) * yearWidth + yearWidth / 2.0;
        var indicator = new Polygon
        {
            Points = new PointCollection { new Point(0, 0), new Point(10, 0), new Point(5, 8) },
            Fill = textBright,
        };
        Canvas.SetLeft(indicator, valX - 5);
        Canvas.SetTop(indicator, trackY - 12);
        TrackCanvas.Children.Add(indicator);

        var thumbLine = new Rectangle
        {
            Width = 2, Height = trackH + 16,
            Fill = textBright,
            RadiusX = 1, RadiusY = 1,
        };
        Canvas.SetLeft(thumbLine, valX - 1);
        Canvas.SetTop(thumbLine, trackY - 4);
        TrackCanvas.Children.Add(thumbLine);

        // 年份刻度标签：每 10 年 + 普查年
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
                FontWeight = censusSet.Contains(y) ? FontWeights.SemiBold : FontWeights.Normal,
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, 0);
            LabelCanvas.Children.Add(tb);
        }
    }
}
