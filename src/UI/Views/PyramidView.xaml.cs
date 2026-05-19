using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ChinaDemographicModel.Core.Models;
using ChinaDemographicModel.UI.ViewModels;

namespace ChinaDemographicModel.UI.Views;

public partial class PyramidView : UserControl
{
    private MainViewModel? _vm;
    private int _selectedAge = -1;
    private bool _selectedIsMaleSide;
    private Border? _detailsPanel;

    public PyramidView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = e.NewValue as MainViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
            Redraw();
        };
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

    private void PyramidCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.CurrentPyramid == null) return;
        var pos = e.GetPosition(PyramidCanvas);
        double w = PyramidCanvas.ActualWidth;
        double h = PyramidCanvas.ActualHeight;
        if (w < 40 || h < 40) return;
        int maxAge = PopulationPyramid.MaxAge;
        double rowH = h / (maxAge + 1);
        int age = (int)Math.Round((h - pos.Y) / rowH - 0.5);
        if (age < 0 || age > maxAge) return;
        _selectedAge = age;
        _selectedIsMaleSide = pos.X < w / 2.0;
        Redraw();
    }

    private void HideDetailsPanel()
    {
        _selectedAge = -1;
        _detailsPanel = null;  // 由下次 Redraw 的 Children.Clear 实际移除
        Redraw();
    }

    private void Redraw()
    {
        _detailsPanel = null;
        PyramidCanvas.Children.Clear();
        if (_vm?.CurrentPyramid == null) return;
        var p = _vm.CurrentPyramid;
        var baseline = _vm.BaselinePyramid;

        double w = PyramidCanvas.ActualWidth;
        double h = PyramidCanvas.ActualHeight;
        if (w < 40 || h < 40) return;

        int maxAge = PopulationPyramid.MaxAge;
        double rowH = h / (maxAge + 1);

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
        double halfW = (w - 80) / 2.0;
        double xScale = halfW / maxVal;

        for (int i = 1; i <= 4; i++)
        {
            double offset = halfW * i / 4;
            AddGridLine(center - offset, 0, center - offset, h);
            AddGridLine(center + offset, 0, center + offset, h);
        }
        AddGridLine(center, 0, center, h, strong: true);

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
                    Width = maleW, Height = mh, Fill = maleBrush,
                    Opacity = 0.85, RadiusX = 1.5, RadiusY = 1.5,
                };
                Canvas.SetLeft(rect, center - maleW);
                Canvas.SetTop(rect, y);
                PyramidCanvas.Children.Add(rect);
            }
            if (femaleW > 0.5)
            {
                var rect = new Rectangle
                {
                    Width = femaleW, Height = mh, Fill = femaleBrush,
                    Opacity = 0.85, RadiusX = 1.5, RadiusY = 1.5,
                };
                Canvas.SetLeft(rect, center);
                Canvas.SetTop(rect, y);
                PyramidCanvas.Children.Add(rect);
            }

            if (baseline != null && baseline != p)
            {
                double bm = baseline.Male[a] * xScale;
                double bf = baseline.Female[a] * xScale;
                if (bm > 0.5)
                {
                    var ol = new Rectangle
                    {
                        Width = bm, Height = mh,
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
                        Width = bf, Height = mh,
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

        // X 轴刻度标签
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

        // 详情面板（点击触发）
        if (_selectedAge >= 0) RenderDetailsPanel(w, h, rowH);
    }

    private TextBlock MakeLabel(string text, double y)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 10, Width = 26,
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

    private void RenderDetailsPanel(double canvasW, double canvasH, double rowH)
    {
        if (_vm?.CurrentPyramid == null) return;
        var p = _vm.CurrentPyramid;
        var hist = _vm.Historical;
        int y = _vm.CurrentYear;
        int age = _selectedAge;
        int birthYear = y - age;

        Core.Models.DemographicInputs? inp = null;
        if (_vm.ActiveScenario != null && _vm.ActiveScenario.InputsByYear.TryGetValue(y, out var ii))
            inp = ii;

        double qaM = inp?.MortalityMale[age] ?? 0;
        double qaF = inp?.MortalityFemale[age] ?? 0;
        double mafmM = inp?.MeanAgeFirstMarriageMale ?? 26;
        double mafmF = inp?.MeanAgeFirstMarriageFemale ?? 24;

        // 已结婚比例 (cumulative normal, σ=4)
        double pMarriedM = CumulativeNormal(age + 0.5, mafmM, 4.0);
        double pMarriedF = CumulativeNormal(age + 0.5, mafmF, 4.0);

        // 已生育至少 1 子 (女): 1 - exp(-cumASFR) Poisson 近似
        double cumAsfr = 0;
        if (inp != null)
        {
            int top = Math.Min(age, inp.AgeSpecificFertility.Length - 1);
            for (int a = 15; a <= top; a++)
                cumAsfr += inp.AgeSpecificFertility[a];
        }
        double pHasChildF = age >= 15 ? 1 - Math.Exp(-cumAsfr) : 0;

        // 累计 cohort 损失
        string attritionStr = "—";
        if (hist != null && hist.BirthsByYear.TryGetValue(birthYear, out var origBirths))
        {
            hist.SexRatioAtBirthByYear.TryGetValue(birthYear, out var srbThen);
            if (srbThen <= 0) srbThen = 108;
            double maleShare = srbThen / (100.0 + srbThen);
            double origM = origBirths * maleShare;
            double origF = origBirths * (1 - maleShare);
            double atM = origM > 0 ? Math.Max(0, 1 - p.Male[age] / origM) : 0;
            double atF = origF > 0 ? Math.Max(0, 1 - p.Female[age] / origF) : 0;
            attritionStr = $"M {atM * 100:0.0}%  F {atF * 100:0.0}%";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"年龄 {age}    出生 {birthYear} 年队列");
        sb.AppendLine();
        sb.AppendLine($"男  {FormatPersons(p.Male[age])}    女  {FormatPersons(p.Female[age])}");
        sb.AppendLine($"q({age})    M = {qaM:0.0000}    F = {qaF:0.0000}");
        sb.AppendLine();
        sb.AppendLine($"已结婚 (估)        M ~{pMarriedM * 100:0}%   F ~{pMarriedF * 100:0}%");
        if (age >= 15)
            sb.AppendLine($"已育至少 1 子 (估,女)   ~{pHasChildF * 100:0}%");
        sb.AppendLine();
        sb.AppendLine($"累计 cohort 损失     {attritionStr}");
        sb.Append("(含死亡 + 净迁移; NM 忽略 → ≈ 累计死亡)");

        var text = new TextBlock
        {
            Text = sb.ToString(),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontSize = 11,
            FontFamily = (FontFamily)FindResource("UiFont"),
            TextWrapping = TextWrapping.Wrap,
        };

        var closeBtn = new TextBlock
        {
            Text = "×",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 0, 8, 0),
        };
        closeBtn.MouseLeftButtonDown += (_, e) => { e.Handled = true; HideDetailsPanel(); };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(closeBtn, 1);
        grid.Children.Add(text);
        grid.Children.Add(closeBtn);

        var border = new Border
        {
            Background = (Brush)FindResource("BgCardBrush"),
            BorderBrush = (Brush)FindResource("AccentPrimaryBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 6, 10),
            Child = grid,
            MaxWidth = 380,
        };
        PyramidCanvas.Children.Add(border);
        border.Measure(new Size(380, double.PositiveInfinity));

        // 放在选中年龄的水平条带附近，自动避开屏幕边缘
        double tx = _selectedIsMaleSide ? 50 : canvasW - border.DesiredSize.Width - 50;
        double anchorY = canvasH - (_selectedAge + 1) * rowH;
        double ty = anchorY - border.DesiredSize.Height - 8;
        if (ty < 4) ty = anchorY + rowH + 8;
        if (ty + border.DesiredSize.Height > canvasH) ty = canvasH - border.DesiredSize.Height - 4;
        if (ty < 4) ty = 4;
        Canvas.SetLeft(border, tx);
        Canvas.SetTop(border, ty);
        Canvas.SetZIndex(border, 1000);
        _detailsPanel = border;
    }

    private static double CumulativeNormal(double x, double mu, double sigma)
    {
        double z = (x - mu) / sigma;
        return 0.5 * (1 + Erf(z / Math.Sqrt(2)));
    }

    /// Abramowitz–Stegun 7.1.26 approximation, max error ~1.5e-7.
    private static double Erf(double x)
    {
        double sign = x < 0 ? -1 : 1;
        double ax = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * ax);
        double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-ax * ax);
        return sign * y;
    }

    private static string FormatPersons(double v)
    {
        if (v >= 1e8) return $"{v / 1e8:0.000} 亿";
        if (v >= 1e4) return $"{v / 1e4:0.0} 万";
        return v.ToString("0");
    }
}
