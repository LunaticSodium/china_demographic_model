using Avalonia;
using Avalonia.Controls;

namespace ChinaDemographicModel.App.Views;

public partial class MainView : UserControl
{
    /// 低于此宽度（逻辑像素）视为窄屏：手机竖屏 / 小窗口。
    /// 桌面三栏 (280 + 内容 + 340) 在 900 以下会把中间挤没，故取 900。
    private const double NarrowThreshold = 900;

    private bool? _narrowApplied;

    public MainView()
    {
        InitializeComponent();
        // 尺寸变化时重算布局（含手机横竖屏切换）。
        SizeChanged += (_, _) => ApplyLayout();
        AttachedToVisualTree += (_, _) => ApplyLayout();
    }

    private void ApplyLayout()
    {
        double w = Bounds.Width;
        if (w <= 0) return;
        bool narrow = w < NarrowThreshold;
        if (_narrowApplied == narrow) return;   // 只在跨阈值时改动，避免每帧写属性
        _narrowApplied = narrow;

        // 左右栏：窄屏收起（列宽归零 + 隐藏），内容改由标签页承载。
        // 注：ColumnDefinition 不是控件，XAML 编译器不会生成字段，故按索引访问。
        MainArea.ColumnDefinitions[0].Width = narrow ? new GridLength(0) : new GridLength(280);
        MainArea.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(340);
        LeftPanel.IsVisible = !narrow;
        RightPanel.IsVisible = !narrow;
        LeftPanel.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 16, 0);
        RightPanel.Margin = narrow ? new Thickness(0) : new Thickness(16, 0, 0, 0);
        NarrowControlsTab.IsVisible = narrow;
        NarrowMetricsTab.IsVisible = narrow;

        // 顶部条：窄屏把年份滑条移到第二行整宽，隐藏副标题 / 图例 / 按钮（操作在「设置」页）
        Subtitle.IsVisible = !narrow;
        LegendPanel.IsVisible = !narrow;
        TopButtons.IsVisible = !narrow;
        YearSliderCtl.MinWidth = narrow ? 140 : 500;

        if (narrow)
        {
            Grid.SetColumn(SliderHost, 0);
            Grid.SetColumnSpan(SliderHost, 3);
            Grid.SetRow(SliderHost, 1);
            SliderHost.Margin = new Thickness(0, 10, 0, 0);
        }
        else
        {
            Grid.SetColumn(SliderHost, 1);
            Grid.SetColumnSpan(SliderHost, 1);
            Grid.SetRow(SliderHost, 0);
            SliderHost.Margin = new Thickness(20, 0, 16, 0);
        }

        RootGrid.Margin = narrow ? new Thickness(10) : new Thickness(20);
    }
}
