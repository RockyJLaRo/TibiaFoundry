using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using TibiaWikiTools;
using Appearance = Tibia.Protobuf.Appearances.Appearance;

namespace ParityHarness;

/// <summary>
/// Produces reference outputs from the original binary. Each section writes into its own
/// sub-folder of the output directory and records any dialogs/exceptions in <c>log.txt</c>.
/// </summary>
internal static class References
{
    private static string _out;
    private static readonly List<string> Log = new List<string>();

    public static void Run(string outDir, string spritesDir, string sections)
    {
        _out = outDir;
        Directory.CreateDirectory(outDir);
        bool Want(string s) => sections == "all" || sections.Split(',').Contains(s);

        var form = Program.CreateForm(spritesDir);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.ShowInTaskbar = false;
        form.Show();
        Orig.Pump(300);
        Orig.Click(form, "Dat_LoadDAT_Click");
        Orig.PumpUntil(() => (bool)Orig.Get(form, "datfileFinishedLoading"));
        Orig.Pump(200);

        Section("resources", Want("resources"), () => Resources_());
        Section("json", Want("json"), () => Json(form));
        Section("lzma", Want("lzma"), () => Lzma(form));
        Section("filters", Want("filters"), () => Filters(form));
        Section("wiki", Want("wiki"), () => Wiki(form));
        Section("npc", Want("npc"), () => NpcTrades(form));
        Section("outfits", Want("outfits"), () => Outfits(form));
        Section("gifs", Want("gifs"), () => Gifs(form));
        Section("sheet", Want("sheet"), () => Sheet(form));
        Section("houses", Want("houses"), () => Houses(form));
        Section("mapextract", Want("mapextract"), () => MapExtract(form));
        Section("hrmap", Want("hrmap"), () => HrMap(form));
        Section("sounds", Want("sounds"), () => Sounds(form));
        Section("compare", Want("compare"), () => Compare());

        form.Close();
        File.WriteAllLines(Path.Combine(outDir, "log.txt"), Log);
    }

    private static void Section(string name, bool enabled, Action action)
    {
        if (!enabled) return;
        Console.WriteLine($"== {name}");
        Log.Add($"== {name}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { action(); }
        catch (Exception ex) { Log.Add($"  EXCEPTION in {name}: {ex.GetType().Name}: {ex.Message}"); Console.WriteLine("  EXCEPTION " + ex); }
        foreach (var d in DialogAutomation.Drain()) Log.Add("  [dialog] " + d);
        Log.Add($"  ({sw.Elapsed.TotalSeconds:F1}s)");
    }

    private static string Dir(string name)
    {
        string d = Path.Combine(_out, name);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Runs a handler that opens a Save dialog, answering it with <paramref name="path"/>.</summary>
    private static void SaveVia(object form, string handler, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        DialogAutomation.FileNames.Enqueue(path);
        Orig.Click(form, handler);
        Orig.Pump(200);
        while (DialogAutomation.FileNames.TryDequeue(out _)) { } // drop unused names
        Log.Add($"  {handler} -> {Path.GetFileName(path)} exists={File.Exists(path)} size={(File.Exists(path) ? new FileInfo(path).Length : 0)}");
    }

    private static string ClipboardText()
    {
        Orig.Pump(50);
        return Clipboard.ContainsText() ? Clipboard.GetText() : "";
    }

    // ------------------------------------------------------------------ sections

    private static void Resources_()
    {
        string d = Dir("resources");
        var rm = new System.Resources.ResourceManager("TibiaWikiTools.Properties.Resources", typeof(MainForm).Assembly);
        foreach (var name in new[] { "tw", "clientbg", "pbbg", "pbbg1" })
            ((Bitmap)rm.GetObject(name)).Save(Path.Combine(d, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        var icon = Icon.ExtractAssociatedIcon(typeof(MainForm).Assembly.Location);
        using (var fs = File.Create(Path.Combine(d, "app_extracted.ico"))) icon.Save(fs);
    }

    private static void Json(MainForm form)
    {
        string d = Dir("json");
        SaveVia(form, "Dat_ExportJSON_Click", Path.Combine(d, "appearances.json"));
        Orig.Click(form, "StaticData_LoadDAT_Click");
        SaveVia(form, "Monsters_ExportJSON_Click", Path.Combine(d, "staticdata.json"));
        Orig.Click(form, "StaticMap_LoadDAT_Click");
        SaveVia(form, "StaticMap_ExportJSON_Click", Path.Combine(d, "staticmapdata.json"));
        Orig.Click(form, "Map_LoadDAT_Click");
        SaveVia(form, "Map_ExportJSON_Click", Path.Combine(d, "map.json"));
        var mapTabs = Orig.Ctl<TabControl>(form, "Map_Tabs");
        mapTabs.SelectedIndex = 0;
        SaveVia(form, "Map_exportTypeJSON_Click", Path.Combine(d, "map_areas.json"));
        SaveVia(form, "Map_exportTypeTable_Click", Path.Combine(d, "map_areas.txt"));
        mapTabs.SelectedIndex = 1;
        SaveVia(form, "Map_exportTypeJSON_Click", Path.Combine(d, "map_npcs.json"));
        SaveVia(form, "Map_exportTypeTable_Click", Path.Combine(d, "map_npcs.txt"));
        mapTabs.SelectedIndex = 0;
        Orig.Click(form, "Sounds_LoadDAT_Click");
        SaveVia(form, "Sounds_ExportJSON_Click", Path.Combine(d, "sounds.json"));

        // Filtered exports after a simple "take" filter on objects.
        Orig.Click(form, "Dat_FiltersClear_Click");
        Orig.Ctl<CheckBox>(form, "Dat_Filter_take").Checked = true;
        Orig.Click(form, "Dat_FiltersApply_Click");
        SaveVia(form, "Dat_ExportFilteredJSON_Click", Path.Combine(d, "filtered_take.json"));
        SaveVia(form, "Dat_ExportFilteredTable_Click", Path.Combine(d, "filtered_take.txt"));
        Orig.Click(form, "Dat_ExportFilteredTableClip_Click");
        File.WriteAllText(Path.Combine(d, "filtered_take_clipboard.txt"), ClipboardText());
        Orig.Click(form, "Dat_FiltersClear_Click");
    }

    private static void Lzma(MainForm form)
    {
        string d = Dir("lzma");
        Orig.Call(form, "PopulateItemsAll");
        var catalog = (Newtonsoft.Json.Linq.JArray)Orig.Get(form, "catalog");
        foreach (var type in new[] { "0", "1", "2", "3" })
        {
            var sheet = catalog.First(e => (string)e["spritetype"] == type);
            var ms = (MemoryStream)Orig.Call(form, "CipLZMAdecode", Program.AssetsDir + (string)sheet["file"]);
            File.WriteAllBytes(Path.Combine(d, $"sheet_type{type}_{sheet["firstspriteid"]}.bmp"), ms.ToArray());
            string sheetDir = Dir($"lzma/split_type{type}");
            form.DumpSpriteSheet((string)sheet["file"], type, (string)sheet["firstspriteid"], (string)sheet["lastspriteid"], true, false, false, sheetDir + "\\");
            form.DumpSpriteSheet((string)sheet["file"], type, (string)sheet["firstspriteid"], (string)sheet["lastspriteid"], false, false, false, d + "\\");
            Log.Add($"  sheet type {type}: {sheet["file"]} {sheet["firstspriteid"]}-{sheet["lastspriteid"]}");
        }
        File.WriteAllText(Path.Combine(d, "catalog_summary.txt"),
            $"nSheets={Orig.Ctl<TextBox>(form, "nSheets").Text}\nnSprites={Orig.Ctl<TextBox>(form, "nSprites").Text}\n" +
            $"newest3232={Orig.Ctl<TextBox>(form, "newest3232").Text}\nnewest6432={Orig.Ctl<TextBox>(form, "newest6432").Text}\n" +
            $"newest3264={Orig.Ctl<TextBox>(form, "newest3264").Text}\nnewest6464={Orig.Ctl<TextBox>(form, "newest6464").Text}\n");
    }

    private sealed class FilterCase
    {
        public string Name;
        public int Tab;
        public Action<MainForm> Setup;
        public string Kind = "other";
    }

    private static void Filters(MainForm form)
    {
        string d = Dir("filters");
        CheckBox Cb(string n) => Orig.Ctl<CheckBox>(form, n);
        RadioButton Rb(string n) => Orig.Ctl<RadioButton>(form, n);
        NumericUpDown Num(string n) => Orig.Ctl<NumericUpDown>(form, n);
        ComboBox Combo(string n) => Orig.Ctl<ComboBox>(form, n);
        TextBox Tx(string n) => Orig.Ctl<TextBox>(form, n);

        var cases = new List<FilterCase>
        {
            new FilterCase { Name = "take", Setup = f => Cb("Dat_Filter_take").Checked = true },
            new FilterCase { Name = "take_and_cumulative", Setup = f => { Cb("Dat_Filter_take").Checked = true; Cb("Dat_Filter_cumulative").Checked = true; } },
            new FilterCase { Name = "take_or_container", Setup = f => { Rb("radioBat_Filter_Include_Or").Checked = true; Cb("Dat_Filter_take").Checked = true; Cb("Dat_Filter_container").Checked = true; } },
            new FilterCase { Name = "exclude_and_unpass_unmove", Setup = f => { Rb("Dat_Filter_Exclude_And").Checked = true; Cb("Dat_Filter_unpass").Checked = true; Cb("Dat_Filter_unmove").Checked = true; } },
            new FilterCase { Name = "exclude_or_take_unpass", Setup = f => { Rb("Dat_Filter_Exclude_Or").Checked = true; Cb("Dat_Filter_take").Checked = true; Cb("Dat_Filter_unpass").Checked = true; } },
            new FilterCase { Name = "market_rings", Setup = f => { Cb("Dat_Filter_marketDataCat").Checked = true; Combo("Dat_Filter_marketDataCatList").SelectedIndex = 11; } },
            new FilterCase { Name = "market_any", Setup = f => { Cb("Dat_Filter_marketDataCat").Checked = true; Combo("Dat_Filter_marketDataCatList").SelectedIndex = 0; } },
            new FilterCase { Name = "npcsaledata", Setup = f => Cb("Dat_Filter_npcsaledata").Checked = true },
            new FilterCase { Name = "light_any", Setup = f => Cb("Dat_Filter_light").Checked = true },
            new FilterCase { Name = "light_brightness3", Setup = f => { Cb("Dat_Filter_light").Checked = true; Num("Dat_Filter_lightBrightness").Value = 3; } },
            new FilterCase { Name = "automap_any", Setup = f => Cb("Dat_Filter_minimap").Checked = true },
            new FilterCase { Name = "automap_color86", Setup = f => { Cb("Dat_Filter_minimap").Checked = true; Num("Dat_Filter_minimapColor").Value = 86; } },
            new FilterCase { Name = "cloth_head", Setup = f => { Cb("Dat_Filter_cloth").Checked = true; Combo("Dat_Filter_clothSlot").SelectedIndex = 1; } },
            new FilterCase { Name = "cloth_all", Setup = f => { Cb("Dat_Filter_cloth").Checked = true; Combo("Dat_Filter_clothSlot").SelectedIndex = 12; } },
            new FilterCase { Name = "write_range", Setup = f => { Cb("Dat_Filter_write").Checked = true; Num("Dat_Filter_writableMin").Value = 0; Num("Dat_Filter_writableMax").Value = 1000; } },
            new FilterCase { Name = "write_once", Setup = f => Cb("Dat_Filter_write_once").Checked = true },
            new FilterCase { Name = "hook_south", Setup = f => { Cb("Dat_Filter_hook").Checked = true; Rb("Dat_Filter_hookSouth").Checked = true; } },
            new FilterCase { Name = "hook_east", Setup = f => { Cb("Dat_Filter_hook").Checked = true; Rb("Dat_Filter_hookEast").Checked = true; } },
            new FilterCase { Name = "bank_eq20", Setup = f => { Cb("Dat_Filter_bank").Checked = true; Num("Dat_Filter_bankSpeed").Value = 20; } },
            new FilterCase { Name = "bank_any", Setup = f => Cb("Dat_Filter_bank").Checked = true },
            new FilterCase { Name = "shift", Setup = f => Cb("Dat_Filter_elevation").Checked = true },
            new FilterCase { Name = "height_8", Setup = f => { Cb("Dat_Filter_height").Checked = true; Num("Dat_Filter_heightElevation").Value = 8; } },
            new FilterCase { Name = "lenshelp_any", Setup = f => Cb("Dat_Filter_lensHelpType").Checked = true },
            new FilterCase { Name = "default_action_any", Setup = f => Cb("Dat_Filter_defaultAction").Checked = true },
            new FilterCase { Name = "default_action_use", Setup = f => { Cb("Dat_Filter_defaultAction").Checked = true; Combo("Dat_Filter_defaultActionPlayerAction").SelectedIndex = 2; } },
            new FilterCase { Name = "changedtoexpire", Setup = f => Cb("Dat_Filter_changedtoexpire").Checked = true },
            new FilterCase { Name = "cyclopedia", Setup = f => Cb("Dat_Filter_cyclopedia").Checked = true },
            new FilterCase { Name = "has_name", Setup = f => Cb("Dat_Filter_name").Checked = true },
            new FilterCase { Name = "description_imbu", Setup = f => { Cb("Dat_Filter_description").Checked = true; Tx("Dat_Filter_descriptionTx").Text = "imbu"; } },
            new FilterCase { Name = "currency_any", Setup = f => Cb("Dat_Filter_currency").Checked = true },
            new FilterCase { Name = "ammo", Setup = f => Cb("Dat_Filter_ammo").Checked = true },
            new FilterCase { Name = "proficiency_any", Setup = f => Cb("Dat_Filter_proficiency").Checked = true },
            new FilterCase { Name = "vocation_knight", Setup = f => { Cb("Dat_Filter_restrictVocation").Checked = true; Combo("Dat_Filter_restrictVocList").SelectedIndex = 2; } },
            new FilterCase { Name = "weapon_sword", Setup = f => { Cb("Dat_Filter_weaponType").Checked = true; Combo("Dat_Filter_weaponTypeList").SelectedIndex = 1; } },
            new FilterCase { Name = "min_level_ge100", Setup = f => { Cb("Dat_Filter_minimumLevel").Checked = true; Num("Dat_Filter_minimumLevelVal").Value = 100; Rb("Dat_Filter_minLevelGt").Checked = true; } },
            new FilterCase { Name = "classification_any", Setup = f => { Cb("Dat_Filter_hasClassification").Checked = true; Combo("Dat_Filter_Classification").SelectedIndex = 0; } },
            new FilterCase { Name = "classification_2", Setup = f => { Cb("Dat_Filter_hasClassification").Checked = true; Combo("Dat_Filter_Classification").SelectedIndex = 2; } },
            new FilterCase { Name = "imbueable_slots123", Setup = f => { Cb("Dat_Filter_imbueable").Checked = true; Cb("Dat_Filter_imbuSlots1").Checked = true; Cb("Dat_Filter_imbuSlots2").Checked = true; Cb("Dat_Filter_imbuSlots3").Checked = true; } },
            new FilterCase { Name = "framegroups_eq1", Setup = f => { Cb("Dat_Filter_frameGroups").Checked = true; Num("Dat_Filter_frameGroupNum").Value = 1; } },
            new FilterCase { Name = "animation", Setup = f => Cb("Dat_Filter_animation").Checked = true },
            new FilterCase { Name = "is_opaque", Setup = f => Cb("Dat_Filter_IsOpaque").Checked = true },
            new FilterCase { Name = "layers_eq2", Setup = f => { Cb("Dat_Filter_layers").Checked = true; Num("Dat_Filter_layersNum").Value = 2; } },
            new FilterCase { Name = "sprites_ge8", Setup = f => { Cb("Dat_Filter_sprites").Checked = true; Num("Dat_Filter_spritesNum").Value = 8; Rb("Dat_Filter_spritesGt").Checked = true; } },
            new FilterCase { Name = "pattern_width_eq4", Setup = f => { Cb("Dat_Filter_patWidth").Checked = true; Num("Dat_Filter_patWidthNum").Value = 4; } },
            new FilterCase { Name = "looptype_pingpong", Setup = f => { Cb("Dat_Filter_loopType").Checked = true; Combo("Dat_Filter_loopTypeType").SelectedIndex = 2; } },
            new FilterCase { Name = "synced", Setup = f => Cb("Dat_Filter_synced").Checked = true },
            new FilterCase { Name = "width_64", Setup = f => { Cb("Dat_Filter_width").Checked = true; Rb("Dat_Filter_W64").Checked = true; } },
            new FilterCase { Name = "id_range", Setup = f => { Cb("Dat_Filter_IDgt").Checked = true; Num("Dat_Filter_IDgtNum").Value = 3000; Cb("Dat_Filter_IDlt").Checked = true; Num("Dat_Filter_IDltNum").Value = 3100; } },
            new FilterCase { Name = "id_list", Setup = f => { Cb("Dat_Filter_IDList").Checked = true; Tx("Dat_Filter_IDList_tx").Text = "3031,3035,3043,999999"; } },
            new FilterCase { Name = "search_name_sword", Kind = "name", Setup = f => Tx("Dat_txSearchName").Text = "sword" },
            new FilterCase { Name = "search_sprite", Kind = "sprite", Setup = f => Tx("Dat_txSearchSprite").Text = MainForm.datfile.Object.First(o => o.Id == 3031).FrameGroup[0].SpriteInfo.SpriteId[0].ToString() },
            new FilterCase { Name = "outfits_name_citizen", Tab = 1, Kind = "name", Setup = f => Tx("Dat_txSearchName").Text = "citizen" },
            new FilterCase { Name = "effects_id_ge50", Tab = 2, Setup = f => { Cb("Dat_Filter_IDgt").Checked = true; Num("Dat_Filter_IDgtNum").Value = 100; } },
        };

        var appTabs = Orig.Ctl<TabControl>(form, "Dat_TabAppearances");
        var results = new Dictionary<string, object>();
        // The original "Clear Filters" does not uncheck the "Adv. Flags 2" tab (min_level,
        // weapon_type), so reset every check box and comparison radio explicitly per case.
        void ResetAll(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is CheckBox cb) cb.Checked = false;
                if (c is RadioButton rb && (rb.Name.EndsWith("Eq") || rb.Name == "Dat_Filter_hookSouth" || rb.Name == "Dat_Filter_W32" || rb.Name == "Dat_Filter_H32")) rb.Checked = true;
                ResetAll(c);
            }
        }
        foreach (var c in cases)
        {
            appTabs.SelectedIndex = c.Tab;
            Orig.Click(form, "Dat_FiltersClear_Click");
            ResetAll(Orig.Ctl<TabPage>(form, "tabPage2"));
            Rb("Dat_Filter_IncludeAnd").Checked = true;
            Orig.Set(form, "FirstFilter", true);
            try
            {
                c.Setup(form);
                Orig.Call(form, "Dat_PerformSearch", c.Kind);
                var filtered = (List<Appearance>)Orig.Get(form, "FilteredDat");
                string listField = c.Tab switch { 1 => "FilteredOutfitIDs", 2 => "FilteredEffectIDs", 3 => "FilteredMissileIDs", _ => "FilteredObjectIDs" };
                results[c.Name] = new
                {
                    tab = c.Tab,
                    filteredDatIds = filtered.Select(a => a.Id).ToList(),
                    gridIds = ((List<int>)Orig.Get(form, listField)).ToList(),
                    visibleCount = Tx("Dat_NVisible").Text,
                };
            }
            catch (Exception ex)
            {
                results[c.Name] = new { tab = c.Tab, exception = ex.GetType().Name + ": " + ex.Message };
            }
            foreach (var dl in DialogAutomation.Drain()) Log.Add($"  [{c.Name}] {dl}");
        }
        appTabs.SelectedIndex = 0;
        Orig.Click(form, "Dat_FiltersClear_Click");
        File.WriteAllText(Path.Combine(d, "filter_results.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
    }

    private static void Wiki(MainForm form)
    {
        string d = Dir("wiki");
        if (Orig.Get(form, "mapdat") == null) Orig.Click(form, "Map_LoadDAT_Click");
        Clipboard.Clear();
        Orig.Click(form, "Map_GenWikiAreaList_Click");
        File.WriteAllText(Path.Combine(d, "wiki_area_list.txt"), ClipboardText());
        Clipboard.Clear();
        Orig.Click(form, "Map_GenWikiLabelList_Click");
        File.WriteAllText(Path.Combine(d, "wiki_coords_labels.txt"), ClipboardText());
        Clipboard.Clear();
        Orig.Click(form, "Map_GenNoCoordsLabelList_Click");
        File.WriteAllText(Path.Combine(d, "wiki_nocoords_labels.txt"), ClipboardText());
    }

    private static void NpcTrades(MainForm form)
    {
        string d = Dir("npc");
        var names = MainForm.datfile.Object.SelectMany(o => o.Flags.Npcsaledata).Select(n => n.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        File.WriteAllLines(Path.Combine(d, "npc_names.txt"), names);
        var pick = names.Where((n, i) => i % Math.Max(1, names.Count / 12) == 0).Take(12).ToList();
        foreach (var known in new[] { "Rashid", "Yasir", "Grizzly Adams", "Haroun", "Nah'Bob" })
            if (names.Contains(known) && !pick.Contains(known)) pick.Add(known);
        foreach (var npc in pick)
        {
            Orig.Ctl<TextBox>(form, "Dat_NPCTradeName").Text = npc;
            foreach (var (radio, suffix) in new[] { ("Dat_NPCTradeGenSortNm", "byname"), ("Dat_NPCTradeGenSortID", "byid") })
            {
                Orig.Ctl<RadioButton>(form, radio).Checked = true;
                Clipboard.Clear();
                Orig.Click(form, "Dat_NPCTradeGen_Click");
                File.WriteAllText(Path.Combine(d, $"{Safe(npc)}_{suffix}.lua"), ClipboardText());
            }
        }
        Orig.Ctl<RadioButton>(form, "Dat_NPCTradeGenSortNm").Checked = true;
    }

    private static readonly int[][] OutfitCases =
    {
        // outfitId, frameGroup, direction, mounted(0/1), addons, onlyFirst(0/1), mountId
        new[] { 128, 0, 2, 0, 0, 0, 0 }, new[] { 128, 1, 2, 0, 0, 0, 0 }, new[] { 128, 1, 1, 0, 3, 0, 0 },
        new[] { 136, 0, 0, 0, 1, 0, 0 }, new[] { 136, 1, 3, 0, 2, 0, 0 }, new[] { 136, 0, 2, 0, 3, 1, 0 },
        new[] { 128, 1, 2, 1, 0, 0, 0 }, new[] { 128, 1, 2, 1, 3, 0, 1599 }, new[] { 1599, 1, 2, 0, 0, 0, 0 },
    };

    private static void Outfits(MainForm form)
    {
        string d = Dir("outfits");
        var helper = (Helper)Orig.Get(form, "helper");
        int[] colors = { 78, 69, 58, 76 };
        int[] colorsAlt = { 0, 94, 114, 19 };
        foreach (var c in OutfitCases)
        {
            string name = $"o{c[0]}_fg{c[1]}_d{c[2]}_m{c[3]}_a{c[4]}_f{c[5]}_mt{c[6]}";
            try
            {
                var frames = helper.GetOutfitFrames((uint)c[0], c[1], c[2], c[3] == 1, c[4] == 2 ? colorsAlt : colors, (uint)c[4], c[5] == 1, (uint)c[6], colorsAlt);
                for (int i = 0; i < frames.Count; i++) Program.SaveImage(frames[i], Path.Combine(d, name, $"frame_{i:D2}.png"));
                Log.Add($"  {name}: {frames.Count} frames");
            }
            catch (Exception ex) { Log.Add($"  {name}: EXCEPTION {ex.GetType().Name}: {ex.Message}"); }
        }
        // Monster rendering exactly as Static Data does it (first 10 outfit-based monsters).
        if (Orig.Get(form, "staticdat") == null) Orig.Click(form, "StaticData_LoadDAT_Click");
        var staticdat = (Tibia.Protobuf.Staticdata.StaticData)Orig.Get(form, "staticdat");
        foreach (var m in staticdat.Monster.Where(x => x.Outfit != null && x.Outfit.OutfitId != 0).Take(10))
        {
            var oc = m.Outfit.Outfitcolors;
            int[] mc = oc == null ? new int[4] : new[] { (int)oc.HeadColor, (int)oc.TorsoColor, (int)oc.LegsColor, (int)oc.DetailColor };
            try
            {
                var frames = helper.GetOutfitFrames(m.Outfit.OutfitId, 1, 2, false, mc, m.Outfit.Addons);
                for (int i = 0; i < frames.Count; i++) Program.SaveImage(frames[i], Path.Combine(d, "monster_" + Safe(m.Name), $"frame_{i:D2}.png"));
            }
            catch (Exception ex) { Log.Add($"  monster {m.Name}: EXCEPTION {ex.Message}"); }
        }
    }

    private static void Gifs(MainForm form)
    {
        string d = Dir("gifs");
        Orig.Ctl<CheckBox>(form, "Gif_AutoName").Checked = true;
        Orig.Ctl<TextBox>(form, "Gif_OutputDir").Text = d;
        Orig.Ctl<TextBox>(form, "Gif_Author").Text = "ParityHarness";
        var appTabs = Orig.Ctl<TabControl>(form, "Dat_TabAppearances");
        var dat = MainForm.datfile;

        var objectIds = new List<uint> { 3031, 3035, 3043, 3264, 2853 };
        objectIds.Add(dat.Object.First(o => o.FrameGroup.Count > 0 && o.FrameGroup[0].SpriteInfo.Animation != null && o.FrameGroup[0].SpriteInfo.Animation.LoopType == Tibia.Protobuf.Shared.ANIMATION_LOOP_TYPE.Pingpong && o.FrameGroup[0].SpriteInfo.PatternWidth == 1).Id);
        objectIds.Add(dat.Object.First(o => o.FrameGroup.Count > 0 && o.FrameGroup[0].SpriteInfo.Animation != null && o.FrameGroup[0].SpriteInfo.Animation.LoopType == Tibia.Protobuf.Shared.ANIMATION_LOOP_TYPE.Infinite && o.FrameGroup[0].SpriteInfo.PatternWidth == 1 && o.Id > 2000).Id);
        objectIds.Add(dat.Object.First(o => o.FrameGroup.Count > 0 && o.FrameGroup[0].SpriteInfo.Animation == null && o.FrameGroup[0].SpriteInfo.PatternWidth == 2 && o.FrameGroup[0].SpriteInfo.PatternHeight == 2).Id);
        var objectRuns = new List<(int tab, uint id, string variant)>();
        foreach (var id in objectIds.Distinct()) objectRuns.Add((0, id, "default"));
        objectRuns.Add((0, 3031, "pattern0"));
        objectRuns.Add((0, objectIds[5], "delaymin_extraframe"));
        objectRuns.Add((1, 128, "idle_south"));
        objectRuns.Add((1, 128, "moving_east"));
        foreach (var e in dat.Effect.Take(3)) objectRuns.Add((2, e.Id, "default"));
        foreach (var m in dat.Missile.Take(2)) objectRuns.Add((3, m.Id, "default"));

        foreach (var (tab, id, variant) in objectRuns)
        {
            appTabs.SelectedIndex = tab;
            Orig.Pump(50);
            switch (tab)
            {
                case 0: form.selectObjectbyId(id.ToString()); break;
                case 1: form.selectOutfitbyId(id.ToString()); break;
                case 2: form.selectEffectbyId(id.ToString()); break;
                default: form.selectMissilebyId(id.ToString()); break;
            }
            Orig.Pump(100);
            var dirCombo = Orig.Ctl<ComboBox>(form, "Gif_OutfitDirection");
            var fgCombo = Orig.Ctl<ComboBox>(form, "Gif_OutfitFrameGroup");
            Orig.Ctl<RadioButton>(form, "Gif_DelayAvg").Checked = true;
            Orig.Ctl<CheckBox>(form, "Gif_ExtraFrame").Checked = false;
            dirCombo.SelectedIndex = 2; fgCombo.SelectedIndex = 1;
            if (variant == "pattern0") Orig.Ctl<ComboBox>(form, "Gif_PatternsUse").SelectedIndex = 0;
            if (variant == "delaymin_extraframe") { Orig.Ctl<RadioButton>(form, "Gif_DelayMin").Checked = true; Orig.Ctl<CheckBox>(form, "Gif_ExtraFrame").Checked = true; }
            if (variant == "idle_south") { fgCombo.SelectedIndex = 0; dirCombo.SelectedIndex = 2; }
            if (variant == "moving_east") { fgCombo.SelectedIndex = 1; dirCombo.SelectedIndex = 1; }
            var before = new HashSet<string>(Directory.GetFiles(d));
            try { Orig.Click(form, "Gif_Export_Click"); }
            catch (Exception ex) { Log.Add($"  gif tab{tab} id{id} {variant}: EXCEPTION {ex.GetType().Name}: {ex.Message}"); }
            var created = Directory.GetFiles(d).Where(f => !before.Contains(f)).ToList();
            foreach (var f in created)
            {
                string target = Path.Combine(d, $"tab{tab}_id{id}_{variant}{Path.GetExtension(f)}");
                if (File.Exists(target)) File.Delete(target);
                File.Move(f, target);
                Log.Add($"  gif tab{tab} id{id} {variant}: created {Path.GetFileName(f)} -> {Path.GetFileName(target)}");
            }
        }
        appTabs.SelectedIndex = 0;
    }

    private static void Sheet(MainForm form)
    {
        string d = Dir("sheet");
        var appTabs = Orig.Ctl<TabControl>(form, "Dat_TabAppearances");
        appTabs.SelectedIndex = 1;
        form.selectOutfitbyId("128");
        Orig.Pump(100);
        Orig.Ctl<RadioButton>(form, "Dat_SheetOutBase64").Checked = true;
        Clipboard.Clear();
        Orig.Click(form, "Dat_GenerateSheet_Click");
        File.WriteAllText(Path.Combine(d, "outfit128_base64.txt"), ClipboardText());
        // PNG file mode: documents the original bug (writes "mgk.png" to the working directory).
        Orig.Ctl<RadioButton>(form, "Dat_SheetOutPNG").Checked = true;
        string mgk = Path.Combine(Environment.CurrentDirectory, "mgk.png");
        if (File.Exists(mgk)) File.Delete(mgk);
        SaveVia(form, "Dat_GenerateSheet_Click", Path.Combine(d, "outfit128_pngfile_mode.png"));
        Log.Add($"  PNG-file mode wrote mgk.png in CWD: {File.Exists(mgk)}");
        if (File.Exists(mgk)) File.Copy(mgk, Path.Combine(d, "outfit128_mgk_from_cwd.png"), true);
        Orig.Ctl<RadioButton>(form, "Dat_SheetOutBase64").Checked = true;
        appTabs.SelectedIndex = 0;
    }

    private static void Houses(MainForm form)
    {
        string d = Dir("houses");
        Orig.Click(form, "StaticMap_LoadDAT_Click");
        var grid = Orig.Ctl<DataGridView>(form, "dataGridStaticMap");
        var pb = Orig.Ctl<PictureBox>(form, "StaticMap_housePb");
        var viewAll = Orig.Ctl<CheckBox>(form, "StaticMap_viewAllFloors");
        var crop = Orig.Ctl<CheckBox>(form, "StaticMap_crop");
        var bg = Orig.Ctl<ComboBox>(form, "StaticMap_bgColor");
        var floor = Orig.Ctl<NumericUpDown>(form, "StaticMap_numFloor");
        var staticmap = (Tibia.Protobuf.Staticmapdata.StaticMapData)Orig.Get(form, "staticmapdat");
        var rows = new List<int> { 0, 5, 25, 100 };
        // the house with the most floors
        int maxFloorsRow = -1; uint maxFloors = 0;
        for (int i = 0; i < grid.Rows.Count; i++)
        {
            uint hid = uint.Parse(grid.Rows[i].Cells[0].Value.ToString());
            var hm = staticmap.HouseMaps.FirstOrDefault(h => h.HouseId == hid);
            if (hm != null && hm.MapInfo.Dimension.TilesZ > maxFloors) { maxFloors = hm.MapInfo.Dimension.TilesZ; maxFloorsRow = i; }
        }
        if (maxFloorsRow >= 0) rows.Add(maxFloorsRow);
        foreach (int r in rows.Where(r => r < grid.Rows.Count).Distinct())
        {
            string hid = grid.Rows[r].Cells[0].Value.ToString();
            try
            {
                viewAll.Checked = true; crop.Checked = false; bg.SelectedIndex = 4;
                grid.ClearSelection(); grid.Rows[r].Selected = true; Orig.Pump(50);
                Program.SaveImage(pb.Image, Path.Combine(d, $"house{hid}_all_transparent.png"));
                crop.Checked = true; Orig.Pump(50);
                Program.SaveImage(pb.Image, Path.Combine(d, $"house{hid}_all_crop_transparent.png"));
                bg.SelectedIndex = 1; Orig.Pump(50);
                Program.SaveImage(pb.Image, Path.Combine(d, $"house{hid}_all_crop_magenta.png"));
                crop.Checked = false; viewAll.Checked = false; bg.SelectedIndex = 2; floor.Value = floor.Minimum; Orig.Pump(50);
                Program.SaveImage(pb.Image, Path.Combine(d, $"house{hid}_floor{floor.Value}_black.png"));
                Log.Add($"  house {hid}: floors {floor.Minimum}-{floor.Maximum}");
            }
            catch (Exception ex) { Log.Add($"  house {hid}: EXCEPTION {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private static void MapExtract(MainForm form)
    {
        string d = Dir("mapextract");
        if (Orig.Get(form, "mapdat") == null) Orig.Click(form, "Map_LoadDAT_Click");
        var map = (Tibia.Protobuf.Map.Map)Orig.Get(form, "mapdat");
        File.WriteAllLines(Path.Combine(_out, "map_scale_factors.txt"),
            map.ResourceFiles.GroupBy(f => f.FileType + " " + f.ScaleFactor.ToString("R") + " " + f.FieldsWidth + "x" + f.FieldsHeight)
               .Select(g => g.Key + " count=" + g.Count()));
        Orig.Ctl<TextBox>(form, "Map_ExtInputTx").Text = Program.AssetsDir;
        Orig.Ctl<TextBox>(form, "Map_ExtOutputTx").Text = d + "\\";
        Orig.Click(form, "Map_Extract_Click");
        Log.Add($"  extracted {Directory.GetFiles(d).Length} files");
    }

    private static void HrMap(MainForm form)
    {
        string d = Dir("hrmap");
        string input = Path.Combine(_out, "mapextract");
        Orig.Ctl<TextBox>(form, "Map_HRInputTx").Text = input;
        var type = Orig.Ctl<ComboBox>(form, "Map_HRType");
        var zoom = Orig.Ctl<ComboBox>(form, "Map_HRZoom");
        var top = Orig.Ctl<ComboBox>(form, "Map_HRTopFloor");
        var cumulative = Orig.Ctl<CheckBox>(form, "Map_HRCumulative");
        var offset = Orig.Ctl<CheckBox>(form, "Map_HRFloorOffset");
        var mark = Orig.Ctl<CheckBox>(form, "Map_HRAddMark");
        var runs = new[]
        {
            (type: 0, zoom: 0, top: 0, cum: false, off: true, mark: false, name: "satellite_z1_ground"),
            (type: 0, zoom: 0, top: 3, cum: true, off: true, mark: true, name: "satellite_z1_top3_cumulative_mark"),
            (type: 1, zoom: 0, top: 0, cum: false, off: false, mark: false, name: "minimap_z1_ground"),
        };
        foreach (var r in runs)
        {
            type.SelectedIndex = r.type; Orig.Pump(30);
            zoom.SelectedIndex = r.zoom; top.SelectedIndex = r.top;
            cumulative.Checked = r.cum; offset.Checked = r.off; mark.Checked = r.mark;
            try { SaveVia(form, "Map_BuildHR_Click", Path.Combine(d, r.name + ".png")); }
            catch (Exception ex) { Log.Add($"  hr {r.name}: EXCEPTION {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private static void Sounds(MainForm form)
    {
        string d = Dir("sounds");
        Orig.Click(form, "Sounds_LoadDAT_Click");
        Orig.Ctl<TextBox>(form, "Sounds_txExtractDir").Text = d;
        Orig.Click(form, "Sounds_btExtract_Click");
        Log.Add($"  extracted {Directory.GetFiles(d).Length} sound files");
    }

    private static void Compare()
    {
        string d = Dir("compare");
        string oldFile = Directory.GetFiles(Program.AssetsDir, "appearances-*.dat-bak").FirstOrDefault();
        if (oldFile == null) { Log.Add("  no appearances .dat-bak available; skipped"); return; }
        var cmp = new CompareDats { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), ShowInTaskbar = false };
        cmp.Show();
        Orig.Ctl<TextBox>(cmp, "tx_OldFile").Text = oldFile;
        Orig.Ctl<TextBox>(cmp, "tx_NewFile").Text = Program.LatestAsset("appearances-*.dat");
        foreach (var ignore in new[] { true, false })
        {
            Orig.Ctl<CheckBox>(cmp, "ignoreSpriteIds").Checked = ignore;
            Orig.Click(cmp, "bt_CompareFiles_Click");
            Orig.Pump(500);
            Orig.PumpUntil(() => !((System.ComponentModel.BackgroundWorker)Orig.Get(cmp, "CompareBW")).IsBusy);
            Orig.Pump(500);
            var result = new Dictionary<string, object>();
            foreach (var kind in new[] { "Objects", "Outfits", "Effects", "Missiles" })
            {
                result[kind] = new
                {
                    changed = Orig.Get(cmp, "changed" + kind),
                    added = Orig.Get(cmp, "new" + kind),
                    changes = Orig.Get(cmp, "changes" + kind),
                };
            }
            File.WriteAllText(Path.Combine(d, $"appearances_compare_ignoreSprites_{ignore}.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        }
        cmp.Close();
    }

    private static string Safe(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}
