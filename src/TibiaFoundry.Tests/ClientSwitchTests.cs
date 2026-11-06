using System.Reflection;
using TibiaFoundry.App.Services;
using TibiaFoundry.App.ViewModels;
using TibiaFoundry.Core.Locations;
using TibiaFoundry.Core.Settings;

namespace TibiaFoundry.Tests;

/// <summary>
/// 8.0 safe switching: with every page opened once on one client, switching to another client leaves
/// no page showing that client's folder or files (the pages that list both servers on purpose aside),
/// and a switch waits while an operation runs.
/// </summary>
public sealed class ClientSwitchTests
{
    /// <summary>Pages that show both clients on purpose: the client list, the settings of both servers, and the diagnostics of both.</summary>
    private static readonly HashSet<Type> ShowBoth = [typeof(ClientsViewModel), typeof(SettingsViewModel), typeof(DiagnosticsViewModel)];

    private static (AppServices Services, MainViewModel Main, string Live, string Test) Create()
    {
        string dir = TestData.NewTempDirectory("switch");
        string packages = Path.Combine(dir, "packages");
        string live = ClientIntegrationTests.RealisticPackage(Path.Combine(packages, "Tibia"), "15.33.9d148f", "live interface");
        string test = ClientIntegrationTests.RealisticPackage(Path.Combine(packages, "TibiaExternal"), "15.25.0e418e", "test interface");
        var settings = new AppSettings { LoadDataAutomatically = false };
        settings.Normalize();
        settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.Live, packages);
        settings.SetChosenLocation(LocationIds.PackagesFolder, ClientScope.TestServer, packages);
        var services = new AppServices(new SettingsStore(Path.Combine(dir, "data", "settings.json")), settings) { Paths = TestData.PathsIn(Path.Combine(dir, "data")) };
        services.Dialogs = new FakeDialogs();
        services.Clients.LauncherActive = _ => false;
        return (services, new MainViewModel(services), live, test);
    }

    /// <summary>Every readable string property of a page that names a path inside <paramref name="folder"/>.</summary>
    private static IEnumerable<string> Mentions(object page, string folder)
    {
        foreach (var p in page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.PropertyType != typeof(string)) continue;
            string? value;
            try { value = p.GetValue(page) as string; }
            catch (TargetInvocationException) { continue; }
            if (value != null && (value.Contains(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || value.EndsWith(folder, StringComparison.OrdinalIgnoreCase)))
                yield return $"{page.GetType().Name}.{p.Name} = {value}";
        }
    }

    private static async Task WaitUntilIdle(AppServices services)
    {
        for (int i = 0; i < 200 && services.Status.IsBusy; i++) await Task.Delay(50);
        Assert.False(services.Status.IsBusy, "Still busy: " + services.Status.Message);
    }

    [Fact]
    public async Task AfterASwitchNoPageShowsThePreviousClient()
    {
        await Sta.Run(async () =>
        {
            var (services, main, live, test) = Create();
            using var _ = services;
            foreach (var page in main.Pages)
            {
                main.SelectedPage = page;
                await page.OnNavigatedToAsync();
            }
            Assert.Contains(main.Pages, p => !ShowBoth.Contains(p.GetType()) && Mentions(p, live).Any());
            await WaitUntilIdle(services);   // pages that load on opening finish first (a switch waits for them)   // the test can see the live client in pages

            main.UseTestServer = true;
            Assert.True(services.Settings.UseTestServer, services.Status.Message);
            Assert.Equal("15.25.0e418e", services.Data.ClientVersion);
            foreach (var page in main.Pages)
            {
                main.SelectedPage = page;
                await page.OnNavigatedToAsync();
            }
            await WaitUntilIdle(services);
            var stale = main.Pages.Where(p => !ShowBoth.Contains(p.GetType())).SelectMany(p => Mentions(p, live)).ToList();
            Assert.True(stale.Count == 0, "Still showing the live client after switching:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
            Assert.Contains(main.Pages, p => Mentions(p, test).Any());
            Assert.Equal("15.25.0e418e", services.Data.ClientVersion);
        });
    }

    [Fact]
    public async Task ASwitchWaitsWhileAnOperationRuns()
    {
        await Sta.Run(() =>
        {
            var (services, main, _, _) = Create();
            using var _ = services;
            using (services.Status.BeginOperation("Working"))
            {
                Assert.NotNull(services.WhyNotSwitchClient());
                main.UseTestServer = true;
                Assert.False(services.Settings.UseTestServer);
                Assert.Contains("operation", services.Status.Message, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Null(services.WhyNotSwitchClient());
            main.UseTestServer = true;
            Assert.True(services.Settings.UseTestServer);
            return Task.CompletedTask;
        });
    }
}
