using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Localization;

/// <summary>
/// Applies the display language at run time. The text itself lives in the string tables read
/// by <see cref="L"/>; this class makes WPF follow a language switch without a restart:
/// <list type="bullet">
/// <item>XAML text uses <c>{loc:Tr Key}</c>, a binding to <see cref="LocalizedStrings.Instance"/>,
/// which announces every key as changed when the language changes;</item>
/// <item>the active table (with the English fallback applied) is also merged into the application's
/// resources, so <c>{DynamicResource Key}</c> works too;</item>
/// <item>view-models re-read their computed text on <see cref="L.LanguageChanged"/> (the main
/// view-model refreshes every page), so the current page, its settings and entered data stay as they are.</item>
/// </list>
/// Only the UI culture changes: <see cref="CultureInfo.CurrentCulture"/> stays as it is, so parsing
/// and writing files never depends on the selected language.
/// </summary>
public static class LocalizationManager
{
    private static ResourceDictionary? _dictionary;

    /// <summary>Selects a language (unknown codes select English) and updates the interface.</summary>
    public static LanguageDefinition Apply(string? code)
    {
        var language = L.SetLanguage(code);
        CultureInfo.DefaultThreadCurrentUICulture = language.Culture;
        Thread.CurrentThread.CurrentUICulture = language.Culture;
        if (Application.Current is { } app)
        {
            var next = new ResourceDictionary();
            foreach (var (key, text) in L.Effective) next[key] = text;
            var merged = app.Resources.MergedDictionaries;
            if (_dictionary != null) merged.Remove(_dictionary);
            merged.Add(next);
            _dictionary = next;
            foreach (Window window in app.Windows) window.Language = XmlLanguage.GetLanguage(language.Culture.IetfLanguageTag);
        }
        LocalizedStrings.Instance.Refresh();
        AppLog.Info($"Interface language: {language.EnglishName} ({language.Code}).", source: "Localization");
        return language;
    }
}

/// <summary>The bindable source of the interface text: <c>{Binding [Key], Source={x:Static loc:LocalizedStrings.Instance}}</c>.</summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    public static LocalizedStrings Instance { get; } = new();

    private LocalizedStrings() { }

    public string this[string key] => L.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Announces every key as changed (after a language switch).</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
}

/// <summary>
/// <c>{loc:Tr Key}</c>: the text of a key in the selected language, updated when the language
/// changes. Works on any dependency property, also on DataGrid and GridView columns, in styles
/// (setter values) and in templates, where a DynamicResource would not.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = LocalizedStrings.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Formats a bound value with a translated pattern: <c>{Binding X, Converter={StaticResource LocFormat}, ConverterParameter=Key}</c>
/// fills <c>{0}</c> of the key's text. Used where XAML would otherwise need a literal StringFormat.
/// </summary>
/// <summary>
/// The bound value, or the text of the key in <c>ConverterParameter</c> when it is null (a translated
/// <c>TargetNullValue</c>; page view models refresh their bindings when the language changes).
/// </summary>
public sealed class LocOrConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value ?? L.T(parameter as string ?? "");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class LocFormatConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => L.F(parameter as string ?? "", value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    /// <summary>Multi-binding form: the first value is filled in; further bindings only make it refresh (bind one to the language).</summary>
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) => L.F(parameter as string ?? "", values.Length > 0 ? values[0] : null);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}
