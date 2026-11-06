using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TibiaFoundry.App.Infrastructure;

/// <summary>true → Visible, false → Collapsed (Parameter "Invert" reverses).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter is "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) != (parameter is "Invert");
}

/// <summary>null/empty → Collapsed, otherwise Visible (Parameter "Invert" reverses).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value switch
        {
            null => false,
            string s => s.Length > 0,
            System.Collections.ICollection c => c.Count > 0,
            int i => i != 0,
            _ => true,
        };
        if (parameter is "Invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Looks up an application resource by its key (e.g. the link icons of the About page).</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Binds a RadioButton to an enum value: IsChecked="{Binding Mode, Converter=..., ConverterParameter=Value}".</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null) return Binding.DoNothing;
        return targetType.IsEnum ? Enum.Parse(targetType, parameter.ToString()!) : parameter;
    }
}

/// <summary>Converts an ARGB uint to a frozen SolidColorBrush.</summary>
public sealed class ArgbToBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not uint argb) return null;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats integers with thousands separators.</summary>
public sealed class NumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i ? i.ToString("N0", culture) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true when all bound values are equal (e.g. a view-model value and an element's Tag).</summary>
public sealed class ValuesEqualConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 1 && values.Skip(1).All(v => Equals(values[0], v));

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns an indentation in pixels into a left margin (property rows of the Assets Editor).</summary>
public sealed class IndentMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is double d ? d : 0, 2, 0, 2);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
