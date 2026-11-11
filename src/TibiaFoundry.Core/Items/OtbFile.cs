using System.Globalization;
using System.Text;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Items;

/// <summary>Item groups of an items.otb (node types of the item nodes).</summary>
public enum OtbItemGroup : byte
{
    None = 0, Ground = 1, Container = 2, Weapon = 3, Ammunition = 4, Armor = 5, Charges = 6, Teleport = 7, MagicField = 8,
    Writable = 9, Key = 10, Splash = 11, Fluid = 12, Door = 13, Deprecated = 14, Podium = 15,
}

/// <summary>One item of an items.otb: the file's own item ID (<c>ServerId</c>, attribute 0x10), the client ID it shows as, its group and flags.</summary>
/// <param name="Name">Old OTB files can carry a name; current ones do not (names are in items.xml).</param>
public sealed record OtbItem(ushort ServerId, ushort? ClientId, OtbItemGroup Group, uint Flags, string? Name, string? Article);

/// <summary>
/// Reads a third-party items.otb item ID mapping file: a tree of nodes (0xFE start, 0xFF end, 0xFD escapes the next byte)
/// after four zero bytes. The root node carries the version (major = file format, minor = client
/// version code, build) and a description such as "OTB 3.65.62-13.10"; each child is an item with its
/// group, flags (32 bits) and attributes (file item ID 0x10, client ID 0x11, name 0x12…). Only reads.
/// </summary>
public sealed class OtbFile
{
    private OtbFile(uint major, uint minor, uint build, string description, List<OtbItem> items, List<string> problems)
    {
        MajorVersion = major;
        MinorVersion = minor;
        Build = build;
        Description = description;
        Items = items;
        Problems = problems;
        int dash = description.LastIndexOf('-');
        string tail = dash >= 0 ? description[(dash + 1)..].Trim() : "";
        ClientVersion = tail.Length > 0 && tail.All(c => char.IsDigit(c) || c == '.') ? tail : null;
    }

    public uint MajorVersion { get; }
    /// <summary>Client version code the file was made for (for example 65 or 1310, depending on the tool that wrote it).</summary>
    public uint MinorVersion { get; }
    public uint Build { get; }
    /// <summary>The root description, for example "OTB 3.65.62-13.10".</summary>
    public string Description { get; }
    /// <summary>The client version from the description ("13.10"), or null when it does not say.</summary>
    public string? ClientVersion { get; }
    public IReadOnlyList<OtbItem> Items { get; }
    /// <summary>Items that could not be used, and why.</summary>
    public IReadOnlyList<string> Problems { get; }

    public const byte FlagMultiUse = 4, FlagPickupable = 5, FlagMovable = 6, FlagStackable = 7, FlagReadable = 14, FlagHangable = 16;

    private const byte NodeStart = 0xFE, NodeEnd = 0xFF, Escape = 0xFD;
    private const byte RootVersionAttribute = 0x01;
    private const int RootVersionLength = 140;
    private const byte AttrServerId = 0x10, AttrClientId = 0x11, AttrName = 0x12, AttrArticle = 0x2F;

    public static OtbFile Read(string path) => Parse(File.ReadAllBytes(path));

    public static OtbFile Parse(byte[] data)
    {
        if (data.Length < 8 || data[0] != 0 || data[1] != 0 || data[2] != 0 || data[3] != 0)
            throw new InvalidDataException("This is not an items.otb file (it does not start with four zero bytes).");
        int pos = 4;
        var root = ReadNode(data, ref pos, depth: 0);
        if (root.Props.Length < 4 + 3 + RootVersionLength || root.Props[4] != RootVersionAttribute ||
            BitConverter.ToUInt16(root.Props, 5) != RootVersionLength)
            throw new InvalidDataException("The items.otb header has an unsupported layout (no version record of 140 bytes).");
        uint major = BitConverter.ToUInt32(root.Props, 7), minor = BitConverter.ToUInt32(root.Props, 11), build = BitConverter.ToUInt32(root.Props, 15);
        string description = Encoding.Latin1.GetString(root.Props, 19, 128).TrimEnd('\0').Split('\0')[0].Trim();

        var items = new List<OtbItem>(root.Children.Count);
        var problems = new List<string>();
        var seen = new HashSet<ushort>();
        int index = 0;
        foreach (var node in root.Children)
        {
            index++;
            try
            {
                var item = ReadItem(node);
                if (item == null) { problems.Add(L.F("OtbFile.ItemNode0HasNoItem", index)); continue; }
                if (!seen.Add(item.ServerId)) { problems.Add(L.F("OtbFile.ServerID0AppearsAgainItem", item.ServerId, index)); continue; }
                items.Add(item);
            }
            catch (InvalidDataException ex)
            {
                problems.Add(L.F("OtbFile.ItemNode0IsDamaged1", index, ex.Message));
            }
        }
        return new OtbFile(major, minor, build, description, items, problems);
    }

    private sealed record Node(byte Type, byte[] Props, List<Node> Children);

    private static Node ReadNode(byte[] data, ref int pos, int depth)
    {
        if (depth > 8) throw new InvalidDataException("The node tree is nested too deeply.");
        if (pos >= data.Length || data[pos] != NodeStart) throw new InvalidDataException($"Expected the start of a node at byte {pos}.");
        pos++;
        if (pos >= data.Length) throw new InvalidDataException("The file ends inside a node.");
        byte type = data[pos++];
        var props = new List<byte>(16);
        var children = new List<Node>();
        while (true)
        {
            if (pos >= data.Length) throw new InvalidDataException("The file ends before a node is closed.");
            byte b = data[pos];
            if (b == Escape)
            {
                if (pos + 1 >= data.Length) throw new InvalidDataException("The file ends after an escape byte.");
                props.Add(data[pos + 1]);
                pos += 2;
            }
            else if (b == NodeStart)
            {
                children.Add(ReadNode(data, ref pos, depth + 1));
            }
            else if (b == NodeEnd)
            {
                pos++;
                return new Node(type, props.ToArray(), children);
            }
            else
            {
                props.Add(b);
                pos++;
            }
        }
    }

    private static OtbItem? ReadItem(Node node)
    {
        byte[] p = node.Props;
        if (p.Length < 4) throw new InvalidDataException("no flags");
        uint flags = BitConverter.ToUInt32(p, 0);
        int i = 4;
        ushort? server = null, client = null;
        string? name = null, article = null;
        while (i < p.Length)
        {
            if (i + 3 > p.Length) throw new InvalidDataException("an attribute is cut off");
            byte attribute = p[i];
            int length = BitConverter.ToUInt16(p, i + 1);
            if (i + 3 + length > p.Length) throw new InvalidDataException($"attribute 0x{attribute:X2} is longer than the item");
            var value = new ReadOnlySpan<byte>(p, i + 3, length);
            switch (attribute)
            {
                case AttrServerId when length == 2: server = BitConverter.ToUInt16(value); break;
                case AttrClientId when length == 2: client = BitConverter.ToUInt16(value); break;
                case AttrName: name = Encoding.Latin1.GetString(value).Trim(); break;
                case AttrArticle: article = Encoding.Latin1.GetString(value).Trim(); break;
            }
            i += 3 + length;
        }
        return server == null ? null : new OtbItem(server.Value, client is 0 ? null : client, (OtbItemGroup)node.Type, flags,
            string.IsNullOrEmpty(name) ? null : name, string.IsNullOrEmpty(article) ? null : article);
    }

    /// <summary>The properties of an OTB item that matter for comparisons (group and selected flags).</summary>
    public static IReadOnlyList<string>? FlagsOf(OtbItem item)
    {
        var flags = new List<string>();
        switch (item.Group)
        {
            case OtbItemGroup.Container: flags.Add("container"); break;
            case OtbItemGroup.Door: flags.Add("door"); break;
            case OtbItemGroup.Teleport: flags.Add("teleport"); break;
            case OtbItemGroup.MagicField: flags.Add(ItemFlags.MagicField); break;
            case OtbItemGroup.Key: flags.Add("key"); break;
            case OtbItemGroup.Writable: flags.Add("writable"); break;
            case OtbItemGroup.Fluid: flags.Add("liquid container"); break;
            case OtbItemGroup.Splash: flags.Add("splash"); break;
            case OtbItemGroup.Podium: flags.Add("podium"); break;
        }
        bool Has(byte bit) => (item.Flags & (1u << bit)) != 0;
        if (Has(FlagPickupable)) flags.Add("pickupable");
        if (Has(FlagStackable)) flags.Add("stackable");
        if (Has(FlagMultiUse)) flags.Add("usable");
        if (Has(FlagReadable) && !flags.Contains("writable")) flags.Add("readable");
        if (Has(FlagHangable)) flags.Add("hangable");
        if (!Has(FlagMovable)) flags.Add("unmovable");
        return flags.Count == 0 ? null : flags;
    }

    public override string ToString() => L.F("OtbFile.0Format1ClientCode2", Description, MajorVersion, MinorVersion, Build, Items.Count);
}
