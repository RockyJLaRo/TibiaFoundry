using System.Windows;
using TibiaFoundry.App.Services;

namespace TibiaFoundry.App.Infrastructure;

/// <summary>
/// Connects each page's view-model to its view from the page registrations (see
/// <see cref="PageRegistration"/>), so a new tool needs no entry in a XAML resource file.
/// </summary>
public static class PageTemplates
{
    /// <summary>Adds a view-model → view template for every registration that does not have one yet.</summary>
    public static int Register(ResourceDictionary resources, IEnumerable<PageRegistration> pages)
    {
        int added = 0;
        foreach (var page in pages)
        {
            var key = new DataTemplateKey(page.ViewModelType);
            if (resources.Contains(key)) continue;
            resources.Add(key, new DataTemplate(page.ViewModelType) { VisualTree = new FrameworkElementFactory(page.ViewType) });
            added++;
        }
        return added;
    }
}
