using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChinaDemographicModel.Core.Models;
using ChinaDemographicModel.UI.ViewModels;

namespace ChinaDemographicModel.UI.Views;

public partial class PyramidView : UserControl
{
    private MainViewModel? _vm;
    private readonly DispatcherTimer _hoverTimer;
    private Point _lastMousePos;
    private Border? _tooltipBorder;

    public PyramidView()
    {
        InitializeComponent();
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _hoverTimer.Tick += (_, _) => ShowTooltipAtLastPos();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = e.NewValue as MainViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
            Redraw();
        };
    }

    private void PyramidCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        _lastMousePos = e.GetPosition(PyramidCanvas);
        HideTooltip();
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void PyramidCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        HideTooltip();
    }

    private void HideTooltip()
    {
        if (_tooltipBorder != null)
        {
            PyramidCanvas.Children.Remove(_tooltipBorder);
            _tooltipBorder = null;
        }
    }

    private void ShowTooltipAtLastPos()
    {
        _hoverTimer.Stop();
        if (_vm?.CurrentPyramid == null) return;
        var p = _vm.CurrentPyramid;
        double w = PyramidCanvas.ActualWidth;
        double h = PyramidCanvas.ActualHeight;
        if (w < 40 || h < 40) return;

        int maxAge = PopulationPyramid.MaxAge;
        double rowH = h / (maxAge + 1);
        int age = (int)Math.Round((h - _lastMousePos.Y) / rowH - 0.5);
        if (age < 0 || age > maxAge) return;

        double center = w / 2.0;
        bool isMale = _lastMousePos.X < center;

        DemographicInputs? inp = null;
        if (_vm.ActiveScenario != null && _vm.ActiveScenario.InputsByYear.TryGetValue(_vm.CurrentYear, out var ii))
            inp = ii;
        double qaM = inp?.MortalityMale[age] ?? 0;
        double qaF = inp?.MortalityFemale[age] ?? 0;

        var sb = new StringBuilder();
        sb.AppendLine($"年龄 {age}{(isMale ? " (鼠标在男侧)" : " (鼠标在女侧)")}");
        sb.AppendLine($"男  {FormatPersons(p.Male[age])}");
        sb.AppendLine($"女  {FormatPersons(p.Female[age])}");
        sb.Append($"q({age})  M={qaM:0.0000}  F={qaF:0.0000}");

        var text = new TextBlock
        {
            Text = sb.ToString(),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("UiFont"),
            Margin = new Thickness(10, 7, 10, 7),
        };
        var border = new Border
        {
            Background = (Brush)FindResource("BgCardBrush"),
            BorderBrush = (Brush)FindResource("AccentPrimaryBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = text,
            IsHitTestVisible = false,
        };
        PyramidCanvas.Children.Add(border);
        border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tx = _lastMousePos.X + 14;
        double ty = _lastMousePos.Y - 8;
        if (tx + border.DesiredSize.Width > w) tx = _lastMousePos.X - 14 - border.DesiredSize.Width;
        if (ty + border.DesiredSize.Height > h) ty = h - border.DesiredSize.Height - 4;
        if (ty < 0) ty = 0;
        Canvas.SetLeft(border, tx);
        Canvas.SetTop(border, ty);
        Canvas.SetZIndex(border, 1000);
        _tooltipBorder = border;
    }

    private static string FormatPersons(double v)
    {
        if (v >= 1e8) return $"{v / 1e8:0.000} 亿";
        if (v >= 1e4) return $"{v / 1e4:0.0} 万";
        return v.ToString("0");
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentPyramid)
            or nameof(MainViewModel.CurrentYear)
            or nameof(MainViewModel.BaselinePyramid)
            or nameof(MainViewModel.PyramidMaxPerAge))
        {
            Dispatcher.BeginInvoke(new Action(Redraw));
        }
    }

    private void PyramidCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        _tooltipBorder = null;  // cleared by Children.Clear below
        PyramidCanvas.Children.Clear();
        if (_vm?.CurrentPyramid == null) return;
        var p = _vm.CurrentPyramid;
        var baseline = _vm.BaselinePyramid;

        double w = PyramidCanvas.ActualWidth;
        double h = PyramidCanvas.ActualHeight;
        if (w < 40 || h < 40) return;

        int maxAge = PopulationPyramid.MaxAge;
        double rowH = h / (maxAge + 1);

        // X 轴用**固定**刻度（跨所有 scenario × year × age × sex 的全局最大），让用户拖年份时
        // 不会被浮动刻度误导。VM 在 RunProjectionForScenario 完成时算并暴露 PyramidMaxPerAge。
        // 仅当 VM 尚未算出时退到 per-year max。
        double maxVal = _vm.PyramidMaxPerAge;
        if (maxVal <= 0)
        {
            for (int a = 0; a <= maxAge; a++)
            {
                maxVal = Math.Max(maxVal, Math.Max(p.Male[a], p.Female[a]));
                if (baseline != null && baseline != p)
                    maxVal = Math.Max(maxVal, Math.Max(baseline.Male[a], baseline.Female[a]));
            }
        }
        if (maxVal <= 0) maxVal = 1;

        double center = w / 2.0;
        double halfW = (w - 80) / 2.0; // 留 80px 给年龄 tick
        double xScale = halfW / maxVal;

        // grid lines (5 等分)
        for (int i = 1; i <= 4; i++)
        {
            double offset = halfW * i / 4;
            AddGridLine(center - offset, 0, center - offset, h);
            AddGridLine(center + offset, 0, center + offset, h);
        }
        // center line
        AddGridLine(center, 0, center, h, strong: true);

        // age ticks every 10
        for (int a = 0; a <= maxAge; a += 10)
        {
            double y = h - (a + 0.5) * rowH;
            var tb = new TextBlock
            {
                Text = a.ToString(),
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10,
                Width = 30,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(tb, center - 15);
            Canvas.SetTop(tb, y - 7);
            PyramidCanvas.Children.Add(tb);
        }

        var maleBrush = (Brush)FindResource("AccentPrimaryBrush");
        var femaleBrush = (Brush)FindResource("AccentTertiaryBrush");

        for (int a = 0; a <= maxAge; a++)
        {
            double y = h - (a + 1) * rowH;
            double mh = Math.Max(1, rowH - 1);

            double maleW = p.Male[a] * xScale;
            double femaleW = p.Female[a] * xScale;

            if (maleW > 0.5)
            {
                var rect = new Rectangle
                {
                    Width = maleW,
                    Height = mh,
                    Fill = maleBrush,
                    Opacity = 0.85,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                };
                Canvas.SetLeft(rect, center - maleW);
                Canvas.SetTop(rect, y);
                PyramidCanvas.Children.Add(rect);
            }
            if (femaleW > 0.5)
            {
                var rect = new Rectangle
                {
                    Width = femaleW,
                    Height = mh,
                    Fill = femaleBrush,
                    Opacity = 0.85,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                };
                Canvas.SetLeft(rect, center);
                Canvas.SetTop(rect, y);
                PyramidCanvas.Children.Add(rect);
            }

            // baseline outline 如果不同场景
            if (baseline != null && baseline != p)
            {
                double bm = baseline.Male[a] * xScale;
                double bf = baseline.Female[a] * xScale;
                if (bm > 0.5)
                {
                    var ol = new Rectangle
                    {
                        Width = bm,
                        Height = mh,
                        Stroke = (Brush)FindResource("TextSecondaryBrush"),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 2, 2 },
                        Fill = Brushes.Transparent,
                    };
                    Canvas.SetLeft(ol, center - bm);
                    Canvas.SetTop(ol, y);
                    PyramidCanvas.Children.Add(ol);
                }
                if (bf > 0.5)
                {
                    var ol = new Rectangle
                    {
                        Width = bf,
                        Height = mh,
                        Stroke = (Brush)FindResource("TextSecondaryBrush"),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 2, 2 },
                        Fill = Brushes.Transparent,
                    };
                    Canvas.SetLeft(ol, center);
                    Canvas.SetTop(ol, y);
                    PyramidCanvas.Children.Add(ol);
                }
            }
        }

        // X-axis 标签 (万人)
        double valTick = maxVal / 4;
        for (int i = 1; i <= 4; i++)
        {
            string label = (valTick * i / 10000.0).ToString("0");
            var tbL = MakeLabel(label, h - 12);
            Canvas.SetLeft(tbL, center - halfW * i / 4 - 12);
            PyramidCanvas.Children.Add(tbL);
            var tbR = MakeLabel(label, h - 12);
            Canvas.SetLeft(tbR, center + halfW * i / 4 - 12);
            PyramidCanvas.Children.Add(tbR);
        }
    }

    private TextBlock MakeLabel(string text, double y)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10,
            Width = 26,
            TextAlignment = TextAlignment.Center,
        };
        Canvas.SetTop(tb, y);
        return tb;
    }

    private void AddGridLine(double x1, double y1, double x2, double y2, bool strong = false)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = strong ? (Brush)FindResource("BorderStrongBrush") : (Brush)FindResource("BorderBrush"),
            StrokeThickness = strong ? 1 : 0.5,
            SnapsToDevicePixels = true,
        };
        if (!strong) line.StrokeDashArray = new DoubleCollection { 2, 4 };
        PyramidCanvas.Children.Add(line);
    }
}
