using Avalonia;
using Avalonia.Controls;

namespace ChinaDemographicModel.App.Views;

public partial class ControlsPanel : UserControl
{
    /// 是否显示「操作」卡（重跑投影 / 重置基线）。
    /// 宽屏时这两个操作在顶栏，故默认关闭；窄屏（手机）顶栏放不下，由本卡承载。
    public static readonly StyledProperty<bool> ShowActionsProperty =
        AvaloniaProperty.Register<ControlsPanel, bool>(nameof(ShowActions));

    public bool ShowActions
    {
        get => GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }

    public ControlsPanel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowActionsProperty)
            ActionsCard.IsVisible = ShowActions;
    }
}
