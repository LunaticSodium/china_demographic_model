using System.Globalization;
using System.Windows.Data;

namespace ChinaDemographicModel.UI.Controls;

/// 将枚举值 ↔ bool 互转。
/// 用法（XAML）：IsChecked="{Binding SelectedX, Converter={StaticResource EnumToBool}, ConverterParameter=ValueName}"
/// 双向：选中 ToggleButton 时把 enum 改为对应值；enum 等于 parameter 时按钮显示选中。
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null && targetType.IsEnum)
            return Enum.Parse(targetType, parameter.ToString()!);
        return Binding.DoNothing;
    }
}
