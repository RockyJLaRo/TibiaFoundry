using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tibia.Protobuf.Shared;
using TibiaFoundry.App.Services;
using TibiaFoundry.Core.Appearances;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.ViewModels;

/// <summary>A tickable flag in the filter list.</summary>
public sealed partial class FlagOption(FlagDefinition definition) : ObservableObject
{
    public FlagDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isVisible = true;
}

/// <summary>
/// State of the "Filters" tab. Every control of the original filter tabs is represented;
/// <see cref="BuildCriteria"/> turns the ticked ones into <see cref="AppearanceCriterion"/>s.
/// </summary>
public sealed partial class AppearanceFilterViewModel : ObservableObject
{
    private readonly AppearancesViewModel _owner;
    private readonly AppServices _services;

    public AppearanceFilterViewModel(AppearancesViewModel owner, AppServices services)
    {
        _owner = owner;
        _services = services;
        Flags = new ObservableCollection<FlagOption>(AppearanceFilter.FlagDefinitions.Select(f => new FlagOption(f)));
    }

    // ------------------------------------------------------------------ mode
    [ObservableProperty] private FilterMode _mode = FilterMode.IncludeAll;
    [ObservableProperty] private bool _keepCriteria;

    // ------------------------------------------------------------------ flags
    public ObservableCollection<FlagOption> Flags { get; }
    [ObservableProperty] private string _flagSearch = "";

    partial void OnFlagSearchChanged(string value)
    {
        foreach (var f in Flags) f.IsVisible = value.Length == 0 || f.Name.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ advanced flags
    [ObservableProperty] private bool _useWrite;
    [ObservableProperty] private int _writeMin;
    [ObservableProperty] private int _writeMax = 100000;
    [ObservableProperty] private bool _useWriteOnce;
    [ObservableProperty] private int _writeOnceMin;
    [ObservableProperty] private int _writeOnceMax = 100000;
    [ObservableProperty] private bool _useHook;
    [ObservableProperty] private bool _hookEast;
    [ObservableProperty] private bool _useLight;
    [ObservableProperty] private int _lightBrightness = -1;
    [ObservableProperty] private int _lightColor = -1;
    [ObservableProperty] private bool _useBank;
    [ObservableProperty] private int _bankSpeed = -1;
    [ObservableProperty] private NumberComparison _bankComparison;
    [ObservableProperty] private bool _useShift;
    [ObservableProperty] private int _shiftX = -1;
    [ObservableProperty] private int _shiftY = -1;
    [ObservableProperty] private bool _useHeight;
    [ObservableProperty] private int _heightElevation;
    [ObservableProperty] private bool _useAutomap;
    [ObservableProperty] private int _automapColor = -1;
    [ObservableProperty] private bool _useLensHelp;
    [ObservableProperty] private int _lensHelpId = -1;
    [ObservableProperty] private bool _useCloth;
    [ObservableProperty] private int _clothSlot = GameTables.ClothesSlotAll;
    [ObservableProperty] private bool _useDefaultAction;
    [ObservableProperty] private int _defaultActionIndex = GameTables.PlayerActions.Count - 1;
    [ObservableProperty] private bool _useMarket;
    [ObservableProperty] private int _marketCategoryIndex;
    [ObservableProperty] private bool _useCurrency;
    [ObservableProperty] private string _currency = "";
    [ObservableProperty] private bool _useDescription;
    [ObservableProperty] private string _descriptionText = "";
    [ObservableProperty] private bool _useClassification;
    [ObservableProperty] private int _classificationIndex;
    [ObservableProperty] private bool _useImbueable;
    [ObservableProperty] private bool _imbueSlot1;
    [ObservableProperty] private bool _imbueSlot2;
    [ObservableProperty] private bool _imbueSlot3;
    [ObservableProperty] private bool _useProficiency;
    [ObservableProperty] private int _proficiencyId = -1;
    [ObservableProperty] private bool _useVocation;
    [ObservableProperty] private int _vocationIndex;
    [ObservableProperty] private bool _useWeaponType;
    [ObservableProperty] private int _weaponTypeIndex;
    [ObservableProperty] private bool _useMinLevel;
    [ObservableProperty] private int _minLevel = 100;
    [ObservableProperty] private NumberComparison _minLevelComparison;

    public Brush AutomapSwatch
    {
        get
        {
            uint argb = AutomapColor < 0 ? 0x00000000 : GameTables.AutomapColor((uint)AutomapColor);
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }
    }

    partial void OnAutomapColorChanged(int value) => OnPropertyChanged(nameof(AutomapSwatch));

    public IReadOnlyList<string> ClothSlots => GameTables.ClothesSlotNames;
    public IReadOnlyList<string> PlayerActions => GameTables.PlayerActions.Select(a => a.Name).ToList();
    public IReadOnlyList<string> MarketCategories => GameTables.MarketCategoryNames;
    public IReadOnlyList<string> Classifications => ["Any", "1", "2", "3", "4"];
    public IReadOnlyList<string> Vocations => GameTables.Vocations.Select(v => v.Name).ToList();
    public IReadOnlyList<string> WeaponTypes => GameTables.WeaponTypes.Select(w => w.Name).ToList();
    public IReadOnlyList<string> LoopTypes => GameTables.LoopTypes.Select(l => l.Name).ToList();
    public IReadOnlyList<string> CurrencyNames => _owner.Database?.Currencies.Select(c => c.Key).ToList() ?? new List<string>();

    // ------------------------------------------------------------------ frame group / sprite info
    [ObservableProperty] private int _targetFrameGroup;
    [ObservableProperty] private bool _useFrameGroups;
    [ObservableProperty] private int _frameGroupCount = 1;
    [ObservableProperty] private NumberComparison _frameGroupComparison;
    [ObservableProperty] private bool _useOpaque;
    [ObservableProperty] private bool _useLayers;
    [ObservableProperty] private int _layers = 1;
    [ObservableProperty] private NumberComparison _layersComparison;
    [ObservableProperty] private bool _useSprites;
    [ObservableProperty] private int _spriteCount = 1;
    [ObservableProperty] private NumberComparison _spritesComparison;
    [ObservableProperty] private bool _usePatternWidth;
    [ObservableProperty] private int _patternWidth = 1;
    [ObservableProperty] private NumberComparison _patternWidthComparison;
    [ObservableProperty] private bool _usePatternHeight;
    [ObservableProperty] private int _patternHeight = 1;
    [ObservableProperty] private NumberComparison _patternHeightComparison;
    [ObservableProperty] private bool _usePatternDepth;
    [ObservableProperty] private int _patternDepth = 1;
    [ObservableProperty] private NumberComparison _patternDepthComparison;
    [ObservableProperty] private bool _useBoundingWidth;
    [ObservableProperty] private bool _boundingWidthLarge;
    [ObservableProperty] private bool _useBoundingHeight;
    [ObservableProperty] private bool _boundingHeightLarge;
    [ObservableProperty] private bool _useAnimated;
    [ObservableProperty] private bool _useSynced;
    [ObservableProperty] private bool _useLoopType;
    [ObservableProperty] private int _loopTypeIndex;
    [ObservableProperty] private bool _useMinDuration;
    [ObservableProperty] private int _minDuration = 100;
    [ObservableProperty] private NumberComparison _minDurationComparison;
    [ObservableProperty] private bool _useMaxDuration;
    [ObservableProperty] private int _maxDuration = 100;
    [ObservableProperty] private NumberComparison _maxDurationComparison;

    // ------------------------------------------------------------------ IDs
    [ObservableProperty] private bool _useIdMin;
    [ObservableProperty] private int _idMin = 100;
    [ObservableProperty] private bool _useIdMax;
    [ObservableProperty] private int _idMax = 100000;
    [ObservableProperty] private bool _useIdList;
    [ObservableProperty] private string _idListText = "";

    public IReadOnlyList<KeyValuePair<NumberComparison, string>> ComparisonOptions { get; } =
    [
        new(NumberComparison.Equal, "="),
        new(NumberComparison.AtLeast, "≥"),
        new(NumberComparison.AtMost, "≤"),
    ];

    // ------------------------------------------------------------------ actions

    /// <summary>Builds the criteria from the ticked controls. Returns false (with a message) on invalid input.</summary>
    public bool BuildCriteria(out List<AppearanceCriterion> criteria, out string? error)
    {
        criteria = new List<AppearanceCriterion>();
        error = null;
        var db = _owner.Database;
        foreach (var f in Flags.Where(f => f.IsChecked)) criteria.Add(AppearanceFilter.Flag(f.Definition));
        if (UseWrite) criteria.Add(AppearanceFilter.Write((uint)Math.Max(0, WriteMin), (uint)Math.Max(0, WriteMax)));
        if (UseWriteOnce) criteria.Add(AppearanceFilter.WriteOnce((uint)Math.Max(0, WriteOnceMin), (uint)Math.Max(0, WriteOnceMax)));
        if (UseHook) criteria.Add(AppearanceFilter.Hook(HookEast ? HOOK_TYPE.East : HOOK_TYPE.South));
        if (UseLight) criteria.Add(AppearanceFilter.Light(LightBrightness, LightColor));
        if (UseBank) criteria.Add(AppearanceFilter.Bank(BankSpeed, BankComparison));
        if (UseShift) criteria.Add(AppearanceFilter.Shift(ShiftX, ShiftY));
        if (UseHeight) criteria.Add(AppearanceFilter.Height((uint)Math.Max(0, HeightElevation)));
        if (UseAutomap) criteria.Add(AppearanceFilter.Automap(AutomapColor));
        if (UseLensHelp) criteria.Add(AppearanceFilter.LensHelp(LensHelpId));
        if (UseCloth) criteria.Add(AppearanceFilter.Clothes(ClothSlot));
        if (UseDefaultAction) criteria.Add(AppearanceFilter.DefaultAction(GameTables.PlayerActions[DefaultActionIndex].Value));
        if (UseMarket) criteria.Add(AppearanceFilter.MarketCategory(MarketCategoryIndex == 0 ? null : GameTables.ItemCategories[MarketCategoryIndex]));
        if (UseCurrency && db != null) criteria.Add(AppearanceFilter.Currency(Currency, db));
        if (UseDescription) criteria.Add(AppearanceFilter.Description(DescriptionText));
        if (UseClassification) criteria.Add(AppearanceFilter.UpgradeClassification(ClassificationIndex));
        if (UseImbueable) criteria.Add(AppearanceFilter.Imbueable(ImbueSlot1, ImbueSlot2, ImbueSlot3));
        if (UseProficiency) criteria.Add(AppearanceFilter.Proficiency(ProficiencyId));
        if (UseVocation) criteria.Add(AppearanceFilter.Vocation(VocationIndex == 0 ? null : GameTables.Vocations[VocationIndex].Value));
        if (UseWeaponType) criteria.Add(AppearanceFilter.WeaponType(GameTables.WeaponTypes[WeaponTypeIndex].Value));
        if (UseMinLevel) criteria.Add(AppearanceFilter.MinimumLevel(MinLevel, MinLevelComparison));

        int fg = TargetFrameGroup;
        if (UseFrameGroups) criteria.Add(AppearanceFilter.FrameGroupCount(FrameGroupCount, FrameGroupComparison));
        if (UseOpaque) criteria.Add(AppearanceFilter.IsOpaque(fg));
        if (UseLayers) criteria.Add(AppearanceFilter.Layers(fg, Layers, LayersComparison));
        if (UseSprites) criteria.Add(AppearanceFilter.SpriteCount(fg, SpriteCount, SpritesComparison));
        if (UsePatternWidth) criteria.Add(AppearanceFilter.PatternWidth(fg, PatternWidth, PatternWidthComparison));
        if (UsePatternHeight) criteria.Add(AppearanceFilter.PatternHeight(fg, PatternHeight, PatternHeightComparison));
        if (UsePatternDepth) criteria.Add(AppearanceFilter.PatternDepth(fg, PatternDepth, PatternDepthComparison));
        if (UseBoundingWidth) criteria.Add(AppearanceFilter.BoundingWidth(fg, BoundingWidthLarge));
        if (UseBoundingHeight) criteria.Add(AppearanceFilter.BoundingHeight(fg, BoundingHeightLarge));
        if (UseAnimated) criteria.Add(AppearanceFilter.Animated(fg));
        if (UseSynced) criteria.Add(AppearanceFilter.Synchronized(fg));
        if (UseLoopType) criteria.Add(AppearanceFilter.LoopType(fg, GameTables.LoopTypes[LoopTypeIndex].Value));
        if (UseMinDuration) criteria.Add(AppearanceFilter.MinDuration(fg, MinDuration, MinDurationComparison));
        if (UseMaxDuration) criteria.Add(AppearanceFilter.MaxDuration(fg, MaxDuration, MaxDurationComparison));

        if (UseIdMin) criteria.Add(AppearanceFilter.IdAtLeast((uint)Math.Max(0, IdMin)));
        if (UseIdMax) criteria.Add(AppearanceFilter.IdAtMost((uint)Math.Max(0, IdMax)));
        if (UseIdList)
        {
            if (!AppearanceFilter.TryParseIdList(IdListText, out var ids, out var bad))
            {
                error = L.F("AppearanceFilter.InvalidListOfIDs0Is", bad);
                return false;
            }
            criteria.Add(AppearanceFilter.IdIn(ids));
        }
        return true;
    }

    public int ActiveCount
    {
        get
        {
            BuildCriteria(out var c, out _);
            return c.Count;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!_owner.IsLoaded) { _services.Dialogs.Show(L.T("AppearanceFilter.LoadAnAppearancesFileFirst")); return; }
        if (!BuildCriteria(out var criteria, out var error))
        {
            _services.Dialogs.Show(error!, MessageKind.Warning, L.T("Common.Filters"));
            return;
        }
        if (criteria.Count == 0)
        {
            _services.Dialogs.Show(L.T("AppearanceFilter.CheckAtLeastOneCriterionTo"), MessageKind.Info, L.T("Common.Filters"));
            return;
        }
        string mode = Mode switch
        {
            FilterMode.IncludeAny => L.T("AppearanceFilter.AnyOf"),
            FilterMode.ExcludeAll => L.T("AppearanceFilter.NotAllOf"),
            FilterMode.ExcludeAny => L.T("AppearanceFilter.NoneOf"),
            _ => L.T("AppearanceFilter.AllOf"),
        };
        string description = criteria.Count == 1 && Mode == FilterMode.IncludeAll
            ? criteria[0].Description
            : $"{mode} [{string.Join(", ", criteria.Select(c => c.Description))}]";
        bool applied = await _owner.ApplyCriteriaAsync(criteria, Mode, description);
        if (applied && !KeepCriteria) UncheckAll();
    }

    [RelayCommand]
    private void Clear()
    {
        UncheckAll();
        _owner.ClearFilters();
    }

    /// <summary>Unticks every criterion (fixed: the original left the "Adv. Flags 2" tab ticked).</summary>
    [RelayCommand]
    public void UncheckAll()
    {
        foreach (var f in Flags) f.IsChecked = false;
        foreach (var prop in GetType().GetProperties().Where(p => p.Name.StartsWith("Use", StringComparison.Ordinal) && p.PropertyType == typeof(bool) && p.CanWrite))
            prop.SetValue(this, false);
    }

    public void RefreshCurrencies() => OnPropertyChanged(nameof(CurrencyNames));
}
