using System.Windows;
using System.Windows.Controls;
using TibiaFoundry.App.ViewModels;

namespace TibiaFoundry.App.Infrastructure;

/// <summary>Chooses the editor template of a property row by its kind.</summary>
public sealed class PropertyTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Header { get; set; }
    public DataTemplate? Bool { get; set; }
    public DataTemplate? Number { get; set; }
    public DataTemplate? Enum { get; set; }
    public DataTemplate? Text { get; set; }
    public DataTemplate? Presence { get; set; }
    public DataTemplate? RepeatedValues { get; set; }
    public DataTemplate? ListHeader { get; set; }
    public DataTemplate? ItemHeader { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is not PropertyNode node ? null : node.Kind switch
    {
        PropertyNodeKind.Header => Header,
        PropertyNodeKind.Bool => Bool,
        PropertyNodeKind.Number => Number,
        PropertyNodeKind.Enum => Enum,
        PropertyNodeKind.Text => Text,
        PropertyNodeKind.Presence => Presence,
        PropertyNodeKind.RepeatedValues => RepeatedValues,
        PropertyNodeKind.ListHeader => ListHeader,
        PropertyNodeKind.ItemHeader => ItemHeader,
        _ => Header,
    };
}
