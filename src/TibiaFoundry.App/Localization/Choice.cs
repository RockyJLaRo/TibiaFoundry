using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Localization;

/// <summary>
/// <c>loc:Choice.Prefix="Choice.Sort"</c> on a ComboBox or ListBox whose items are identifiers (enum
/// values, fixed English strings): each item is shown as the text of the key
/// <c>{Prefix}.{item}</c> (letters and digits of the item), or as the item itself when the table has
/// no such key. The items, the selection and everything the program compares stay identifiers;
/// only the display is translated, and it follows a language switch.
/// </summary>
public static class Choice
{
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.RegisterAttached(
        "Prefix", typeof(string), typeof(Choice), new PropertyMetadata(null, OnPrefixChanged));

    public static string? GetPrefix(DependencyObject element) => (string?)element.GetValue(PrefixProperty);

    public static void SetPrefix(DependencyObject element, string? value) => element.SetValue(PrefixProperty, value);

    /// <summary>The display text of an item under a prefix.</summary>
    public static string Display(string prefix, object? item)
    {
        string text = item?.ToString() ?? "";
        string part = ToolText.KeyPart(text);
        return part.Length == 0 ? text : L.TOr(prefix + "." + part, text);
    }

    private static void OnPrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl items || e.NewValue is not string prefix) return;
        var text = new FrameworkElementFactory(typeof(TextBlock));
        var binding = new MultiBinding { Converter = DisplayConverter.Instance, ConverterParameter = prefix };
        binding.Bindings.Add(new Binding());
        binding.Bindings.Add(new Binding("[Main.Language]") { Source = LocalizedStrings.Instance }); // refreshes after a language switch
        text.SetBinding(TextBlock.TextProperty, binding);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        items.ItemTemplate = new DataTemplate { VisualTree = text };
    }

    private sealed class DisplayConverter : IMultiValueConverter
    {
        public static DisplayConverter Instance { get; } = new();

        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
            Display(parameter as string ?? "", values.Length > 0 ? values[0] : null);

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
    }
}

/// <summary>The <see cref="Choice"/> display for a single bound identifier: <c>Converter={StaticResource LocChoice}, ConverterParameter=Prefix</c>.</summary>
public sealed class LocChoiceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Choice.Display(parameter as string ?? "", value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
