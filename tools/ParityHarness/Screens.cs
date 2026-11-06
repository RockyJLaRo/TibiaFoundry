using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TibiaWikiTools;

namespace ParityHarness;

/// <summary>Captures off-screen screenshots of every screen of the original application.</summary>
internal static class Screens
{
    private static string _out;
    private static int _seq;

    public static void Run(string outDir, string spritesDir)
    {
        _out = outDir;
        Directory.CreateDirectory(outDir);
        var form = Program.CreateForm(spritesDir);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.ShowInTaskbar = false;
        form.Show();
        Orig.Pump(500);

        var main = Orig.Ctl<TabControl>(form, "tabControl1");
        Snap(form, "start_appearances_empty");

        // --- Load every data set the way a user would (Load buttons).
        Orig.Click(form, "Dat_LoadDAT_Click");
        Orig.PumpUntil(() => (bool)Orig.Get(form, "datfileFinishedLoading"));
        Orig.Pump(300);
        Orig.Click(form, "StaticData_LoadDAT_Click");
        Orig.Click(form, "Map_LoadDAT_Click");
        Orig.Click(form, "Sounds_LoadDAT_Click");
        Orig.Pump(300);

        // Appearances viewer and its sub tabs.
        main.SelectedTab = Orig.Ctl<TabPage>(form, "datViewer");
        var appTabs = Orig.Ctl<TabControl>(form, "Dat_TabAppearances");
        var datTabs = Orig.Ctl<TabControl>(form, "Dat_Tabs");
        form.selectObjectbyId("3031");
        Orig.Pump(200);
        Snap(form, "appearances_objects_data");
        foreach (var (page, name) in new[] { ("tabPage2", "filters_basic"), ("tabPage4", "export"), ("tabPage8", "tools") })
        {
            datTabs.SelectedTab = Orig.Ctl<TabPage>(form, page);
            Snap(form, "appearances_" + name);
        }
        datTabs.SelectedTab = Orig.Ctl<TabPage>(form, "tabPage2");
        var filterTabs = Orig.Ctl<TabControl>(form, "Dat_FilterTabs");
        var flagTabs = Orig.Ctl<TabControl>(form, "Dat_FilterTabsFlags");
        flagTabs.SelectedIndex = 1; Snap(form, "appearances_filters_adv1");
        flagTabs.SelectedIndex = 2; Snap(form, "appearances_filters_adv2");
        filterTabs.SelectedIndex = 1; Snap(form, "appearances_filters_framegroup");
        filterTabs.SelectedIndex = 2; Snap(form, "appearances_filters_ids");
        datTabs.SelectedIndex = 0;
        appTabs.SelectedIndex = 1;
        form.selectOutfitbyId("128");
        Snap(form, "appearances_outfits_data");
        datTabs.SelectedTab = Orig.Ctl<TabPage>(form, "tabPage20");
        Orig.Call(form, "UpdateOutfiter", null, EventArgs.Empty);
        Orig.Pump(400);
        Snap(form, "appearances_outfitter");
        datTabs.SelectedIndex = 0;
        appTabs.SelectedIndex = 2; Snap(form, "appearances_effects_data");
        appTabs.SelectedIndex = 3; Snap(form, "appearances_missiles_data");
        appTabs.SelectedIndex = 0;

        // Static data.
        main.SelectedTab = Orig.Ctl<TabPage>(form, "staticViewer");
        var staticTabs = Orig.Ctl<TabControl>(form, "tabStatic");
        string[] staticNames = { "monsters", "achievements", "houses", "bosses", "quests" };
        for (int i = 0; i < staticNames.Length; i++)
        {
            staticTabs.SelectedIndex = i;
            Orig.Pump(200);
            Snap(form, "static_" + staticNames[i]);
        }
        staticTabs.SelectedIndex = 0;

        // Map tools.
        main.SelectedTab = Orig.Ctl<TabPage>(form, "mapTools");
        var mapTabs = Orig.Ctl<TabControl>(form, "Map_Tabs");
        var mapRight = Orig.Ctl<TabControl>(form, "tabControl3");
        string[] mapNames = { "areas", "npcs", "files" };
        for (int i = 0; i < 3; i++) { mapTabs.SelectedIndex = i; Snap(form, "map_" + mapNames[i]); }
        mapTabs.SelectedIndex = 0;
        mapRight.SelectedIndex = 1; Snap(form, "map_tools");
        mapRight.SelectedIndex = 2; Snap(form, "map_export");
        mapRight.SelectedIndex = 0;

        // House images. NOTE: loading this data while the tab is hidden makes the original crash on the
        // first tab switch (dataGridStaticMap_SelectionChanged reads SelectedRows[0] with no selection),
        // so the data is loaded from inside the tab like a user would.
        main.SelectedTab = Orig.Ctl<TabPage>(form, "staticMapData");
        Orig.Click(form, "StaticMap_LoadDAT_Click");
        Orig.Pump(300);
        var houseGrid = Orig.Ctl<DataGridView>(form, "dataGridStaticMap");
        // ClearSelection() would crash the original (empty selection is not guarded), so move the
        // current cell instead.
        try { if (houseGrid.Rows.Count > 5) houseGrid.CurrentCell = houseGrid.Rows[5].Cells[0]; }
        catch (Exception ex) { Console.WriteLine("  original raised " + ex.GetType().Name + " while selecting a house: " + ex.Message); }
        Orig.Pump(300);
        Snap(form, "house_images");

        // Sounds.
        main.SelectedTab = Orig.Ctl<TabPage>(form, "sounds");
        var soundTabs = Orig.Ctl<TabControl>(form, "soundTabs");
        string[] soundNames = { "sounds", "effects", "ambience", "ambience_objects", "music" };
        for (int i = 0; i < soundNames.Length; i++) { soundTabs.SelectedIndex = i; Orig.Pump(150); Snap(form, "sounds_" + soundNames[i]); }
        Orig.Ctl<TabControl>(form, "tabControl2").SelectedIndex = 1; Snap(form, "sounds_export");

        // Extract sprites (visible) and the three pages the original hides at start-up.
        main.SelectedTab = Orig.Ctl<TabPage>(form, "ExtractSprites");
        Snap(form, "extract_sprites");
        foreach (var hidden in new[] { "ExportSheets", "spritesFromPNG", "tabLZMA" })
        {
            var page = Orig.Ctl<TabPage>(form, hidden);
            main.TabPages.Add(page);
            main.SelectedTab = page;
            Snap(form, "hidden_" + hidden);
        }

        // Secondary windows.
        var compare = new CompareDats { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), ShowInTaskbar = false };
        compare.Show(); SnapForm(compare, "window_compare_dats"); compare.Close();

        var outfit = MainForm.datfile.Outfit.First(o => o.Id == 128);
        var ids = outfit.FrameGroup[0].SpriteInfo.SpriteId.ToList();
        var viewAll = new ViewAllSprites(ids, spritesDir, 4) { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), ShowInTaskbar = false };
        viewAll.Show(); SnapForm(viewAll, "window_view_all_sprites"); viewAll.Close();

        var byId = new ExtractByID("") { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), ShowInTaskbar = false };
        byId.Show(); SnapForm(byId, "dialog_extract_by_id"); byId.Close();
        var list = new InsertSpriteID("") { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), ShowInTaskbar = false };
        list.Show(); SnapForm(list, "dialog_insert_sprite_ids"); list.Close();

        form.Close();
        File.WriteAllLines(Path.Combine(outDir, "dialog_log.txt"), DialogAutomation.Drain());
    }

    private static void Snap(Form form, string name) => SnapForm(form, name);

    private static void SnapForm(Form form, string name)
    {
        Orig.Pump(150);
        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        string path = Path.Combine(_out, $"{++_seq:D2}_{name}.png");
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("  screenshot " + Path.GetFileName(path));
    }
}
