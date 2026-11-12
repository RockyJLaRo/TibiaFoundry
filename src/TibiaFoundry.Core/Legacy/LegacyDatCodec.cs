using System.Buffers.Binary;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Legacy;

/// <summary>Reads and writes legacy Tibia.dat files for a given <see cref="LegacyClientProfile"/>.</summary>
/// <remarks>
/// <code>
/// uint32 signature, uint16 item count (highest item ID), uint16 outfit, effect and missile counts
/// per object (items from ID 100, the others from 1):
///   attributes: byte code [+ value], ..., 0xFF
///   [outfits with frame groups: byte group count; per group: byte group type]
///   per group: byte width, byte height, [byte exact size if width or height &gt; 1], byte layers,
///              byte pattern X, byte pattern Y, [byte pattern Z (7.55+)], byte phases,
///              [enhanced animations and phases &gt; 1: byte synchronized, int32 loop count,
///               sbyte start phase, phases × (uint32 min, uint32 max) durations],
///              sprite IDs (uint16, or uint32 for extended clients)
/// </code>
/// </remarks>
public static class LegacyDatCodec
{
    /// <summary>The client refuses objects with more sprites than this.</summary>
    public const int MaxSpritesPerObject = 4096;

    public static LegacyDatFile Read(string path, LegacyClientProfile profile) => Read(File.ReadAllBytes(path), profile);

    /// <summary>
    /// Parses a file. Throws <see cref="InvalidDataException"/> naming the object where parsing
    /// failed (wrong profile or a damaged file), and when bytes are left over at the end.
    /// </summary>
    public static LegacyDatFile Read(byte[] data, LegacyClientProfile profile)
    {
        var r = new Reader(data);
        var file = new LegacyDatFile { Signature = r.U32() };
        int items = r.U16(), outfits = r.U16(), effects = r.U16(), missiles = r.U16();
        if (items < 99) throw new InvalidDataException($"The item count ({items}) is smaller than the first item ID (100); this is not a Tibia.dat file.");
        ReadCategory(r, file.Items, LegacyCategory.Item, 100, items, profile);
        ReadCategory(r, file.Outfits, LegacyCategory.Outfit, 1, outfits, profile);
        ReadCategory(r, file.Effects, LegacyCategory.Effect, 1, effects, profile);
        ReadCategory(r, file.Missiles, LegacyCategory.Missile, 1, missiles, profile);
        if (r.Position != data.Length)
            throw new InvalidDataException($"{L.Count(data.Length - r.Position, "Noun.byte")} {(data.Length - r.Position == 1 ? "is" : "are")} left after the last object; the file does not match the {profile.Name} format.");
        return file;
    }

    private static void ReadCategory(Reader r, List<LegacyThing> list, LegacyCategory category, uint first, int last, LegacyClientProfile profile)
    {
        for (uint id = first; id <= last; id++)
        {
            int start = r.Position;
            try
            {
                list.Add(ReadThing(r, category, id, profile));
            }
            catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                throw new InvalidDataException($"{category} {id} (byte offset {start:N0}) could not be read as {profile.Name}: {ex.Message}", ex);
            }
        }
    }

    private static LegacyThing ReadThing(Reader r, LegacyCategory category, uint id, LegacyClientProfile profile)
    {
        var thing = new LegacyThing(category, id);
        for (int count = 0; ; count++)
        {
            if (count > 64) throw new InvalidDataException("more than 64 attributes (the attribute list never ends)");
            byte code = r.U8();
            if (code == LegacyAttributes.End) break;
            var attribute = LegacyAttributes.FromByte(profile.Layout, code)
                            ?? throw new InvalidDataException($"unknown attribute byte {code} (0x{code:X2})");
            thing.Attributes.Add(LegacyAttributes.PayloadOf(attribute, profile.Layout) switch
            {
                LegacyPayload.U16 => new LegacyAttributeValue(attribute, r.U16()),
                LegacyPayload.TwoU16 => new LegacyAttributeValue(attribute, r.U16(), r.U16()),
                LegacyPayload.Market => new LegacyAttributeValue(attribute, Market: ReadMarket(r)),
                _ => new LegacyAttributeValue(attribute),
            });
        }
        bool groups = category == LegacyCategory.Outfit && profile.FrameGroups;
        int groupCount = groups ? r.U8() : 1;
        if (groupCount == 0) throw new InvalidDataException("no frame groups");
        int total = 0;
        for (int g = 0; g < groupCount; g++)
        {
            var group = new LegacyFrameGroup { Type = groups ? r.U8() : (byte)0 };
            group.Width = r.U8();
            group.Height = r.U8();
            if (group.Width > 1 || group.Height > 1) group.ExactSize = r.U8();
            group.Layers = r.U8();
            group.PatternX = r.U8();
            group.PatternY = r.U8();
            group.PatternZ = profile.HasPatternZ ? r.U8() : (byte)1;
            group.Phases = r.U8();
            if (group.Phases > 1 && profile.EnhancedAnimations)
            {
                var animation = new LegacyAnimation { Synchronized = r.U8() != 0, LoopCount = r.I32(), StartPhase = (sbyte)r.U8() };
                for (int p = 0; p < group.Phases; p++) animation.Durations.Add((r.U32(), r.U32()));
                group.Animation = animation;
            }
            int sprites = group.SpriteCount;
            if (sprites == 0) throw new InvalidDataException("a frame group has a zero dimension");
            total += sprites;
            if (total > MaxSpritesPerObject) throw new InvalidDataException($"more than {MaxSpritesPerObject} sprites");
            group.Sprites = new uint[sprites];
            for (int i = 0; i < sprites; i++) group.Sprites[i] = profile.ExtendedSprites ? r.U32() : r.U16();
            thing.FrameGroups.Add(group);
        }
        return thing;
    }

    private static LegacyMarket ReadMarket(Reader r)
    {
        ushort category = r.U16(), tradeAs = r.U16(), showAs = r.U16();
        int length = r.U16();
        byte[] name = r.Bytes(length);
        return new LegacyMarket(category, tradeAs, showAs, name, r.U16(), r.U16());
    }

    /// <summary>
    /// Serializes a file. Attributes the profile's layout cannot store, and frame data the profile
    /// cannot represent, raise <see cref="InvalidDataException"/> instead of producing a file the
    /// client cannot read.
    /// </summary>
    public static byte[] Write(LegacyDatFile file, LegacyClientProfile profile)
    {
        using var ms = new MemoryStream(1 << 20);
        var w = new Writer(ms);
        w.U32(profile.DatSignature);
        foreach (var category in Enum.GetValues<LegacyCategory>())
        {
            var list = file.List(category);
            uint last = (uint)(LegacyDatFile.FirstId(category) + list.Count - 1);
            if (category == LegacyCategory.Item && list.Count == 0) last = 99;
            if (last > ushort.MaxValue) throw new InvalidDataException($"{category}: {last:N0} objects do not fit the 16-bit count of the format.");
            w.U16((ushort)(list.Count == 0 && category != LegacyCategory.Item ? 0 : last));
        }
        foreach (var category in Enum.GetValues<LegacyCategory>())
        {
            uint expected = LegacyDatFile.FirstId(category);
            foreach (var thing in file.List(category))
            {
                if (thing.Id != expected++) throw new InvalidDataException($"{category} IDs must be consecutive; {thing.Id} found where {expected - 1} was expected.");
                WriteThing(w, thing, profile);
            }
        }
        return ms.ToArray();
    }

    private static void WriteThing(Writer w, LegacyThing thing, LegacyClientProfile profile)
    {
        foreach (var a in thing.Attributes)
        {
            byte code = LegacyAttributes.ToByte(profile.Layout, a.Attribute)
                        ?? throw new InvalidDataException($"{thing.Category} {thing.Id}: {profile.Name} cannot store the attribute {a.Attribute}.");
            w.U8(code);
            switch (LegacyAttributes.PayloadOf(a.Attribute, profile.Layout))
            {
                case LegacyPayload.U16:
                    w.U16(a.A);
                    break;
                case LegacyPayload.TwoU16:
                    w.U16(a.A);
                    w.U16(a.B);
                    break;
                case LegacyPayload.Market:
                    var m = a.Market ?? throw new InvalidDataException($"{thing.Category} {thing.Id}: market attribute without data.");
                    w.U16(m.Category);
                    w.U16(m.TradeAs);
                    w.U16(m.ShowAs);
                    if (m.Name.Length > ushort.MaxValue) throw new InvalidDataException($"{thing.Category} {thing.Id}: market name too long.");
                    w.U16((ushort)m.Name.Length);
                    w.Bytes(m.Name);
                    w.U16(m.Vocation);
                    w.U16(m.Level);
                    break;
            }
        }
        w.U8(LegacyAttributes.End);
        bool groups = thing.Category == LegacyCategory.Outfit && profile.FrameGroups;
        if (!groups && thing.FrameGroups.Count != 1)
            throw new InvalidDataException($"{thing.Category} {thing.Id}: {profile.Name} stores exactly one frame group per object, this one has {thing.FrameGroups.Count}.");
        if (groups)
        {
            if (thing.FrameGroups.Count is 0 or > 255) throw new InvalidDataException($"{thing.Category} {thing.Id}: invalid number of frame groups.");
            w.U8((byte)thing.FrameGroups.Count);
        }
        int total = 0;
        foreach (var g in thing.FrameGroups)
        {
            if (groups) w.U8(g.Type);
            w.U8(g.Width);
            w.U8(g.Height);
            if (g.Width > 1 || g.Height > 1) w.U8(g.ExactSize);
            w.U8(g.Layers);
            w.U8(g.PatternX);
            w.U8(g.PatternY);
            if (profile.HasPatternZ) w.U8(g.PatternZ);
            else if (g.PatternZ != 1) throw new InvalidDataException($"{thing.Category} {thing.Id}: {profile.Name} has no pattern Z.");
            w.U8(g.Phases);
            if (g.Phases > 1 && profile.EnhancedAnimations)
            {
                var animation = g.Animation ?? throw new InvalidDataException($"{thing.Category} {thing.Id}: animated frame group without timing.");
                if (animation.Durations.Count != g.Phases) throw new InvalidDataException($"{thing.Category} {thing.Id}: {animation.Durations.Count} durations for {g.Phases} phases.");
                w.U8(animation.Synchronized ? (byte)1 : (byte)0);
                w.I32(animation.LoopCount);
                w.U8((byte)animation.StartPhase);
                foreach (var (min, max) in animation.Durations)
                {
                    w.U32(min);
                    w.U32(max);
                }
            }
            if (g.Sprites.Length != g.SpriteCount)
                throw new InvalidDataException($"{thing.Category} {thing.Id}: {g.Sprites.Length} sprite IDs for a frame group that needs {g.SpriteCount}.");
            total += g.SpriteCount;
            if (total > MaxSpritesPerObject) throw new InvalidDataException($"{thing.Category} {thing.Id}: more than {MaxSpritesPerObject} sprites.");
            foreach (uint s in g.Sprites)
            {
                if (profile.ExtendedSprites) w.U32(s);
                else if (s > ushort.MaxValue) throw new InvalidDataException($"{thing.Category} {thing.Id}: sprite {s} needs an extended (32-bit) client.");
                else w.U16((ushort)s);
            }
        }
    }

    /// <summary>
    /// Finds the profile that reads a file completely: tries known signatures first, then every
    /// layout with and without extended sprites, frame groups and animation timing. Returns null
    /// when none fits.
    /// </summary>
    public static (LegacyDatFile File, LegacyClientProfile Profile)? Detect(byte[] data)
    {
        if (data.Length < 12) return null;
        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var candidates = new List<LegacyClientProfile>();
        candidates.AddRange(LegacyClientProfiles.Known.Where(p => p.DatSignature == signature));
        foreach (var version in new[] { 1098, 1056, 1041, 960, 860, 854, 772, 740 })
        foreach (bool extended in new[] { false, true })
            candidates.Add(LegacyClientProfiles.ForVersion(version, extended) with { DatSignature = signature });
        foreach (var profile in candidates)
        {
            try
            {
                return (Read(data, profile), profile);
            }
            catch (InvalidDataException)
            {
                // Try the next layout.
            }
        }
        return null;
    }

    private sealed class Reader(byte[] data)
    {
        public int Position { get; private set; }

        private ReadOnlySpan<byte> Take(int n)
        {
            if (Position + n > data.Length) throw new InvalidDataException("unexpected end of file");
            var span = data.AsSpan(Position, n);
            Position += n;
            return span;
        }

        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public byte[] Bytes(int n) => Take(n).ToArray();
    }

    private sealed class Writer(Stream s)
    {
        private readonly byte[] _buf = new byte[4];

        public void U8(byte v) => s.WriteByte(v);

        public void U16(ushort v)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_buf, v);
            s.Write(_buf, 0, 2);
        }

        public void U32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buf, v);
            s.Write(_buf, 0, 4);
        }

        public void I32(int v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_buf, v);
            s.Write(_buf, 0, 4);
        }

        public void Bytes(byte[] v) => s.Write(v);
    }
}
