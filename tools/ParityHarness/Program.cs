using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using TibiaWikiTools;

namespace ParityHarness;

internal static class Program
{
    public static readonly string AssetsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Tibia\packages\Tibia\assets") + "\\";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: ParityHarness sprites <outDir> | screens <outDir> <spritesDir> | reference <outDir> <spritesDir> [sections]");
            return 2;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // The original relies on BackgroundWorker completions being marshalled to the UI thread.
        // Without Application.Run, install the WinForms context explicitly and keep it installed.
        WindowsFormsSynchronizationContext.AutoInstall = false;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        DialogAutomation.Start();
        var sw = Stopwatch.StartNew();
        try
        {
            switch (args[0])
            {
                case "sprites": ExtractAllSprites(args[1]); break;
                case "screens": Screens.Run(args[1], args[2]); break;
                case "reference": References.Run(args[1], args[2], args.Length > 3 ? args[3] : "all"); break;
                default: Console.WriteLine("unknown command"); return 2;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL: " + ex);
            return 1;
        }
        finally
        {
            foreach (var line in DialogAutomation.Drain()) Console.WriteLine("  [dialog] " + line);
        }
        Console.WriteLine($"Done in {sw.Elapsed}.");
        return 0;
    }

    /// <summary>Creates the original main form without showing it.</summary>
    public static MainForm CreateForm(string spritesDir)
    {
        var form = new MainForm();
        ((CheckBox)Orig.Get(form, "Dat_cbSaveSettings")).Checked = false; // never write settings.json
        if (spritesDir != null)
        {
            Orig.Set(typeof(MainForm), "DatSpritesDir", spritesDir);
            ((TextBox)Orig.Get(form, "Dat_txSelectedSpriteFolder")).Text = spritesDir;
            ((TextBox)Orig.Get(form, "Monsters_SelectedSprites")).Text = spritesDir;
        }
        // Always use the live client files (never the *.dat-bak picked up by the 8.3 wildcard quirk).
        ((TextBox)Orig.Get(form, "Dat_txSelectedDat")).Text = LatestAsset("appearances-*.dat");
        return form;
    }

    public static string LatestAsset(string pattern) =>
        Directory.GetFiles(AssetsDir, pattern).Where(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                 .OrderByDescending(File.GetLastWriteTimeUtc).First();

    /// <summary>Extracts every sprite sheet using the original DumpSpriteSheet implementation.</summary>
    private static void ExtractAllSprites(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var form = CreateForm(null);
        Orig.Call(form, "PopulateItemsAll");
        var catalog = (JArray)Orig.Get(form, "catalog");
        var sheets = catalog.Where(e => e["spritetype"] != null).ToList();
        Console.WriteLine($"Extracting {sheets.Count} sheets to {outDir}");
        int done = 0;
        Parallel.ForEach(sheets, new ParallelOptions { MaxDegreeOfParallelism = 4 }, sheet =>
        {
            form.DumpSpriteSheet((string)sheet["file"], (string)sheet["spritetype"], (string)sheet["firstspriteid"],
                (string)sheet["lastspriteid"], true, false, false, outDir.TrimEnd('\\') + "\\");
            int n = System.Threading.Interlocked.Increment(ref done);
            if (n % 250 == 0) Console.WriteLine($"  {n}/{sheets.Count}");
        });
        form.Dispose();
    }

    public static void SaveImage(Image image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
