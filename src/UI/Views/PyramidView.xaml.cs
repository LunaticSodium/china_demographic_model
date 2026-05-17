using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ChinaDemographicModel.Core.Models;
using ChinaDemographicModel.UI.ViewModels;

namespace ChinaDemographicModel.UI.Views;

public partial class PyramidView : UserControl
{
    private MainViewModel? _vm;

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
            or nameof(MainViewModel.BaselinePyramid))
        {
            Dispatcher.BeginInvoke(new Action(Redraw));
        }
    }

    private void PyramidCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        PyramidCanvas.Children.Clear();
        if (_vm?.CurrentPyramid == null) return;
        var p = _vm.CurrentPyramid;
        var baseline = _vm.BaselinePyramid;

        double w = PyramidCanvas.ActualWidth;
        double h = PyramidCanvas.ActualHeight;
        if (w < 40 || h < 40) return;

        int maxAge = PopulationPyramid.MaxAge;
        double rowH = h / (maxAge + 1);

        // find horizontal max
        double maxVal = 0;
        for (int a = 0; a <= maxAge; a++)
        {
            maxVal = Math.Max(maxVal, Math.Max(p.Male[a], p.Female[a]));
            if (baseline != null && baseline != p)
                maxVal = Math.Max(maxVal, Math.Max(baseline.Male[a], baseline.Female[a]));
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
