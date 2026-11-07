using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.Library;

/// <summary>
/// The Asset Library (8.8): a local SQLite index of the images, animations, sounds and other media a
/// fansite works with, with categories, tags, custom fields, collections, related assets and the
/// history of each file's content.
/// </summary>
/// <remarks>
/// <para>Files are referenced where they are and only read: removing an asset from the library never
/// deletes a file, and large collections are not copied. Files imported with "Copy into the library"
/// are kept in <see cref="FilesFolder"/>.</para>
/// <para>Layout of <see cref="Folder"/>: <c>library.db</c> (the index), <c>thumbnails/</c> (cache,
/// can be deleted any time), <c>files/</c> (copies), <c>profiles/</c>, <c>projects/</c> and
/// <c>workflows/</c> (JSON). Everything works offline; nothing is uploaded.</para>
/// <para>Every method opens its own pooled connection, so the library can be used from background
/// threads; SQLite's write-ahead log keeps readers and the single writer apart.</para>
/// </remarks>
public sealed class AssetLibrary
{
    public const int SchemaVersion = 1;
    public const string DatabaseFileName = "library.db";

    private readonly string _connectionString;

    private AssetLibrary(string folder)
    {
        Folder = folder;
        DatabasePath = Path.Combine(folder, DatabaseFileName);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private, Pooling = true }.ToString();
    }

    /// <summary>The library folder.</summary>
    public string Folder { get; }
    public string DatabasePath { get; }
    public string ThumbnailFolder => Path.Combine(Folder, "thumbnails");
    public string FilesFolder => Path.Combine(Folder, "files");
    public string ProfilesFolder => Path.Combine(Folder, "profiles");
    public string ProjectsFolder => Path.Combine(Folder, "projects");
    public string WorkflowsFolder => Path.Combine(Folder, "workflows");

    /// <summary>Opens (and creates or upgrades) the library in <paramref name="folder"/>.</summary>
    public static AssetLibrary Open(string folder)
    {
        string full = Path.GetFullPath(folder);
        Directory.CreateDirectory(full);
        var library = new AssetLibrary(full);
        foreach (string d in new[] { library.ThumbnailFolder, library.FilesFolder, library.ProfilesFolder, library.ProjectsFolder, library.WorkflowsFolder })
            Directory.CreateDirectory(d);
        library.Migrate();
        return library;
    }

    /// <summary>Releases pooled connections (before the database file is moved, replaced or backed up as a file).</summary>
    public static void ReleaseConnections() => SqliteConnection.ClearAllPools();

    private SqliteConnection Connect()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private void Migrate()
    {
        using var c = Connect();
        Exec(c, "PRAGMA journal_mode = WAL;");
        Exec(c, """
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS roots (
                id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE COLLATE NOCASE, recursive INTEGER NOT NULL DEFAULT 1,
                category TEXT, source TEXT, added_utc TEXT NOT NULL, last_scan_utc TEXT);
            CREATE TABLE IF NOT EXISTS assets (
                id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE COLLATE NOCASE, name TEXT NOT NULL, category TEXT NOT NULL,
                description TEXT, source TEXT, source_tool TEXT, client_version TEXT, original_id TEXT, format TEXT NOT NULL,
                width INTEGER NOT NULL DEFAULT 0, height INTEGER NOT NULL DEFAULT 0, frames INTEGER NOT NULL DEFAULT 0, size INTEGER NOT NULL DEFAULT 0,
                sha256 TEXT, dhash INTEGER, pixel_key TEXT, created_utc TEXT NOT NULL, modified_utc TEXT NOT NULL, file_modified_utc TEXT NOT NULL,
                creator TEXT, attribution TEXT, copyright TEXT, export_status TEXT NOT NULL DEFAULT 'Not exported',
                missing INTEGER NOT NULL DEFAULT 0, managed INTEGER NOT NULL DEFAULT 0,
                root_id INTEGER REFERENCES roots(id) ON DELETE SET NULL);
            CREATE INDEX IF NOT EXISTS ix_assets_sha ON assets(sha256);
            CREATE INDEX IF NOT EXISTS ix_assets_pixel ON assets(pixel_key);
            CREATE INDEX IF NOT EXISTS ix_assets_category ON assets(category);
            CREATE INDEX IF NOT EXISTS ix_assets_name ON assets(name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS ix_assets_root ON assets(root_id);
            CREATE TABLE IF NOT EXISTS asset_tags (asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE, tag TEXT NOT NULL COLLATE NOCASE, PRIMARY KEY (asset_id, tag));
            CREATE INDEX IF NOT EXISTS ix_tags_tag ON asset_tags(tag);
            CREATE TABLE IF NOT EXISTS asset_fields (asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE, key TEXT NOT NULL COLLATE NOCASE, value TEXT NOT NULL, PRIMARY KEY (asset_id, key));
            CREATE TABLE IF NOT EXISTS asset_relations (asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE, related_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE, PRIMARY KEY (asset_id, related_id));
            CREATE TABLE IF NOT EXISTS asset_versions (id INTEGER PRIMARY KEY, asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE, sha256 TEXT, size INTEGER NOT NULL, file_modified_utc TEXT NOT NULL, seen_utc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_versions_asset ON asset_versions(asset_id);
            CREATE TABLE IF NOT EXISTS categories (name TEXT PRIMARY KEY COLLATE NOCASE, description TEXT);
            CREATE TABLE IF NOT EXISTS collections (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE COLLATE NOCASE, description TEXT,
                thumbnail_asset_id INTEGER REFERENCES assets(id) ON DELETE SET NULL, created_utc TEXT NOT NULL, modified_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS collection_items (collection_id INTEGER NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
                asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE, position INTEGER NOT NULL, PRIMARY KEY (collection_id, asset_id));
            """);
        int version = int.TryParse(Scalar(c, "SELECT value FROM meta WHERE key = 'schema'") as string, out int v) ? v : 0;
        if (version > SchemaVersion)
            throw new InvalidDataException($"The Asset Library in {Folder} was written by a newer version of Tibia Foundry (schema {version}). Update Tibia Foundry to open it.");
        Exec(c, "INSERT OR REPLACE INTO meta(key, value) VALUES ('schema', $v)", ("$v", SchemaVersion.ToString(CultureInfo.InvariantCulture)));
    }

    // ------------------------------------------------------------------ assets

    private const string AssetColumns = "id, path, name, category, description, source, source_tool, client_version, original_id, format, width, height, frames, size, sha256, dhash, pixel_key, created_utc, modified_utc, file_modified_utc, creator, attribution, copyright, export_status, missing, managed";

    /// <summary>The asset at <paramref name="path"/>, or null.</summary>
    public AssetRecord? FindByPath(string path)
    {
        using var c = Connect();
        return QueryAssets(c, $"SELECT {AssetColumns} FROM assets WHERE path = $p", ("$p", Path.GetFullPath(path))).FirstOrDefault();
    }

    public AssetRecord? Get(long id)
    {
        using var c = Connect();
        return QueryAssets(c, $"SELECT {AssetColumns} FROM assets WHERE id = $id", ("$id", id)).FirstOrDefault();
    }

    public IReadOnlyList<AssetRecord> Get(IEnumerable<long> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        using var c = Connect();
        var result = new List<AssetRecord>();
        foreach (var chunk in list.Chunk(500))
            result.AddRange(QueryAssets(c, $"SELECT {AssetColumns} FROM assets WHERE id IN ({string.Join(",", chunk)})"));
        var order = list.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return result.OrderBy(a => order[a.Id]).ToList();
    }

    /// <summary>The assets whose file has exactly this content.</summary>
    public IReadOnlyList<AssetRecord> FindBySha(string sha256)
    {
        using var c = Connect();
        return QueryAssets(c, $"SELECT {AssetColumns} FROM assets WHERE sha256 = $s", ("$s", sha256));
    }

    /// <summary>
    /// Adds an asset or updates the file facts of an existing one (same path). Descriptive metadata
    /// the user edited (name, category, description, tags…) is kept; a changed content is recorded
    /// as a version. Returns the asset's ID and whether the content changed.
    /// </summary>
    public (long Id, bool Added, bool ContentChanged) Upsert(AssetRecord record, long? rootId = null) => Upsert([record], rootId)[0];

    /// <summary>Adds or updates several assets in one transaction (see <see cref="Upsert(AssetRecord, long?)"/>).</summary>
    public IReadOnlyList<(long Id, bool Added, bool ContentChanged)> Upsert(IReadOnlyList<AssetRecord> records, long? rootId = null)
    {
        var results = new List<(long, bool, bool)>(records.Count);
        using var c = Connect();
        using var tx = c.BeginTransaction();
        string now = Iso(DateTime.UtcNow);
        foreach (var r in records)
        {
            string path = Path.GetFullPath(r.Path);
            var existing = QueryAssets(c, $"SELECT {AssetColumns} FROM assets WHERE path = $p", ("$p", path)).FirstOrDefault();
            if (existing == null)
            {
                long id = Convert.ToInt64(Scalar(c, """
                    INSERT INTO assets (path, name, category, description, source, source_tool, client_version, original_id, format, width, height, frames, size, sha256, dhash, pixel_key,
                        created_utc, modified_utc, file_modified_utc, creator, attribution, copyright, export_status, missing, managed, root_id)
                    VALUES ($path, $name, $category, $description, $source, $tool, $version, $original, $format, $w, $h, $frames, $size, $sha, $dhash, $pkey,
                        $now, $now, $fmod, $creator, $attribution, $copyright, $status, 0, $managed, $root);
                    SELECT last_insert_rowid();
                    """,
                    ("$path", path), ("$name", r.Name), ("$category", r.Category), ("$description", r.Description), ("$source", r.Source), ("$tool", r.SourceTool),
                    ("$version", r.ClientVersion), ("$original", r.OriginalId), ("$format", r.Format.ToLowerInvariant()), ("$w", r.Width), ("$h", r.Height), ("$frames", r.Frames),
                    ("$size", r.SizeBytes), ("$sha", r.Sha256), ("$dhash", r.PixelHash), ("$pkey", r.PixelKey), ("$now", now), ("$fmod", Iso(r.FileModifiedUtc)),
                    ("$creator", r.Creator), ("$attribution", r.Attribution), ("$copyright", r.Copyright), ("$status", r.ExportStatus), ("$managed", r.Managed ? 1 : 0), ("$root", rootId)));
                ReplaceTags(c, id, r.Tags);
                foreach (var (k, v) in r.Fields) Exec(c, "INSERT OR REPLACE INTO asset_fields(asset_id, key, value) VALUES ($a, $k, $v)", ("$a", id), ("$k", k), ("$v", v));
                results.Add((id, true, false));
                continue;
            }
            bool changed = existing.Sha256 != null && r.Sha256 != null && !string.Equals(existing.Sha256, r.Sha256, StringComparison.OrdinalIgnoreCase);
            if (changed)
                Exec(c, "INSERT INTO asset_versions(asset_id, sha256, size, file_modified_utc, seen_utc) VALUES ($a, $s, $size, $f, $now)",
                    ("$a", existing.Id), ("$s", existing.Sha256), ("$size", existing.SizeBytes), ("$f", Iso(existing.FileModifiedUtc)), ("$now", now));
            // File facts are refreshed; metadata the user may have edited is only filled in when empty.
            Exec(c, """
                UPDATE assets SET format = $format, width = $w, height = $h, frames = $frames, size = $size, sha256 = $sha, dhash = $dhash, pixel_key = $pkey,
                    file_modified_utc = $fmod, missing = 0, root_id = COALESCE(root_id, $root),
                    modified_utc = CASE WHEN $changed = 1 THEN $now ELSE modified_utc END,
                    client_version = COALESCE(client_version, $version), original_id = COALESCE(original_id, $original), source = COALESCE(source, $source),
                    source_tool = COALESCE(source_tool, $tool), creator = COALESCE(creator, $creator), attribution = COALESCE(attribution, $attribution),
                    copyright = COALESCE(copyright, $copyright)
                WHERE id = $id
                """,
                ("$format", r.Format.ToLowerInvariant()), ("$w", r.Width), ("$h", r.Height), ("$frames", r.Frames), ("$size", r.SizeBytes), ("$sha", r.Sha256), ("$dhash", r.PixelHash),
                ("$pkey", r.PixelKey), ("$fmod", Iso(r.FileModifiedUtc)), ("$root", rootId), ("$changed", changed ? 1 : 0), ("$now", now), ("$version", r.ClientVersion),
                ("$original", r.OriginalId), ("$source", r.Source), ("$tool", r.SourceTool), ("$creator", r.Creator), ("$attribution", r.Attribution), ("$copyright", r.Copyright), ("$id", existing.Id));
            if (existing.Tags.Count == 0 && r.Tags.Count > 0) ReplaceTags(c, existing.Id, r.Tags);
            results.Add((existing.Id, false, changed));
        }
        tx.Commit();
        return results;
    }

    /// <summary>File facts of every asset (path → size, modified time, ID) for an incremental scan.</summary>
    public Dictionary<string, (long Id, long Size, DateTime FileModifiedUtc, bool Missing)> FileFacts(string? underFolder = null)
    {
        using var c = Connect();
        var result = new Dictionary<string, (long, long, DateTime, bool)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, path, size, file_modified_utc, missing FROM assets" + (underFolder != null ? " WHERE path LIKE $p ESCAPE '\\'" : "");
        if (underFolder != null) cmd.Parameters.AddWithValue("$p", EscapeLike(Path.TrimEndingDirectorySeparator(Path.GetFullPath(underFolder)) + Path.DirectorySeparatorChar) + "%");
        using var r = cmd.ExecuteReader();
        while (r.Read()) result[r.GetString(1)] = (r.GetInt64(0), r.GetInt64(2), ParseIso(r.GetString(3)), r.GetInt64(4) != 0);
        return result;
    }

    /// <summary>Marks assets whose file is gone as missing (or found again). Nothing is removed.</summary>
    public void SetMissing(IEnumerable<long> ids, bool missing)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        foreach (long id in ids) Exec(c, "UPDATE assets SET missing = $m WHERE id = $id", ("$m", missing ? 1 : 0), ("$id", id));
        tx.Commit();
    }

    /// <summary>Changes descriptive metadata of one or more assets.</summary>
    public void Update(IEnumerable<long> ids, AssetEdit edit)
    {
        if (edit.Category != null && !CategoryExists(edit.Category)) AddCategory(edit.Category);
        using var c = Connect();
        using var tx = c.BeginTransaction();
        string now = Iso(DateTime.UtcNow);
        foreach (long id in ids)
        {
            void Set(string column, string? value)
            {
                if (value != null) Exec(c, $"UPDATE assets SET {column} = $v, modified_utc = $now WHERE id = $id", ("$v", value.Length == 0 && column is not ("name" or "category") ? null : value), ("$now", now), ("$id", id));
            }
            Set("name", edit.Name is { Length: > 0 } n ? n.Trim() : null);
            Set("category", edit.Category);
            Set("description", edit.Description);
            Set("client_version", edit.ClientVersion);
            Set("original_id", edit.OriginalId);
            Set("source", edit.Source);
            Set("creator", edit.Creator);
            Set("attribution", edit.Attribution);
            Set("copyright", edit.Copyright);
            if (edit.ExportStatus != null) Set("export_status", ExportStates.All.Contains(edit.ExportStatus) ? edit.ExportStatus : ExportStates.NotExported);
            if (edit.Tags != null) ReplaceTags(c, id, edit.Tags);
            if (edit.Fields != null)
            {
                Exec(c, "DELETE FROM asset_fields WHERE asset_id = $a", ("$a", id));
                foreach (var (k, v) in edit.Fields.Where(f => f.Key.Trim().Length > 0))
                    Exec(c, "INSERT OR REPLACE INTO asset_fields(asset_id, key, value) VALUES ($a, $k, $v)", ("$a", id), ("$k", k.Trim()), ("$v", v));
            }
        }
        tx.Commit();
    }

    /// <summary>Adds tags to assets (keeps the existing ones).</summary>
    public void AddTags(IEnumerable<long> ids, IEnumerable<string> tags)
    {
        var clean = NormalizeTags(tags);
        using var c = Connect();
        using var tx = c.BeginTransaction();
        foreach (long id in ids)
            foreach (string t in clean) Exec(c, "INSERT OR IGNORE INTO asset_tags(asset_id, tag) VALUES ($a, $t)", ("$a", id), ("$t", t));
        tx.Commit();
    }

    /// <summary>Removes assets from the library. Files are never deleted (copies in the library's own folder stay too).</summary>
    public int Remove(IEnumerable<long> ids)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        int n = 0;
        foreach (long id in ids) n += Exec(c, "DELETE FROM assets WHERE id = $id", ("$id", id));
        tx.Commit();
        return n;
    }

    public void Relate(long a, long b)
    {
        if (a == b) return;
        using var c = Connect();
        Exec(c, "INSERT OR IGNORE INTO asset_relations(asset_id, related_id) VALUES ($a, $b); INSERT OR IGNORE INTO asset_relations(asset_id, related_id) VALUES ($b, $a);", ("$a", a), ("$b", b));
    }

    public void Unrelate(long a, long b)
    {
        using var c = Connect();
        Exec(c, "DELETE FROM asset_relations WHERE (asset_id = $a AND related_id = $b) OR (asset_id = $b AND related_id = $a)", ("$a", a), ("$b", b));
    }

    public IReadOnlyList<AssetRecord> Related(long id)
    {
        using var c = Connect();
        return QueryAssets(c, $"SELECT {Prefixed("a")} FROM assets a JOIN asset_relations r ON r.related_id = a.id WHERE r.asset_id = $id ORDER BY a.name", ("$id", id));
    }

    public IReadOnlyList<AssetVersion> Versions(long id)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, sha256, size, file_modified_utc, seen_utc FROM asset_versions WHERE asset_id = $id ORDER BY seen_utc DESC";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        var list = new List<AssetVersion>();
        while (r.Read()) list.Add(new AssetVersion(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2), ParseIso(r.GetString(3)), ParseIso(r.GetString(4))));
        return list;
    }

    // ------------------------------------------------------------------ search

    /// <summary>One page of the assets matching <paramref name="q"/>, and the number of all matches.</summary>
    public AssetPage Search(AssetQuery q)
    {
        var where = new List<string>();
        var args = new List<(string, object?)>();
        void Add(string condition, params (string, object?)[] values)
        {
            where.Add(condition);
            args.AddRange(values);
        }
        if (!q.IncludeMissing) where.Add("a.missing = 0");
        if (!string.IsNullOrWhiteSpace(q.Category)) Add("a.category = $category COLLATE NOCASE", ("$category", q.Category));
        if (!string.IsNullOrWhiteSpace(q.Format)) Add("a.format = $format", ("$format", q.Format.TrimStart('.').ToLowerInvariant()));
        if (!string.IsNullOrWhiteSpace(q.ClientVersion)) Add("a.client_version = $version", ("$version", q.ClientVersion));
        if (!string.IsNullOrWhiteSpace(q.Source)) Add("(a.source = $source OR a.source_tool = $source)", ("$source", q.Source));
        if (!string.IsNullOrWhiteSpace(q.Creator)) Add("a.creator LIKE $creator ESCAPE '\\'", ("$creator", "%" + EscapeLike(q.Creator) + "%"));
        if (!string.IsNullOrWhiteSpace(q.Tag)) Add("EXISTS (SELECT 1 FROM asset_tags t WHERE t.asset_id = a.id AND t.tag = $tag)", ("$tag", q.Tag.Trim()));
        if (q.Animated is { } animated) where.Add(animated ? "a.frames > 1" : "a.frames <= 1");
        if (!string.IsNullOrWhiteSpace(q.ExportStatus)) Add("a.export_status = $status", ("$status", q.ExportStatus));
        if (q.CollectionId is { } col) Add("EXISTS (SELECT 1 FROM collection_items ci WHERE ci.asset_id = a.id AND ci.collection_id = $col)", ("$col", col));
        if (q.CreatedFromUtc is { } cf) Add("a.created_utc >= $cf", ("$cf", Iso(cf)));
        if (q.CreatedToUtc is { } ct) Add("a.created_utc < $ct", ("$ct", Iso(ct)));
        if (q.ModifiedFromUtc is { } mf) Add("a.modified_utc >= $mf", ("$mf", Iso(mf)));
        if (q.ModifiedToUtc is { } mt) Add("a.modified_utc < $mt", ("$mt", Iso(mt)));
        if (q.MinWidth is { } minW) Add("a.width >= $minw", ("$minw", minW));
        if (q.MaxWidth is { } maxW) Add("a.width <= $maxw", ("$maxw", maxW));
        if (q.MinHeight is { } minH) Add("a.height >= $minh", ("$minh", minH));
        if (q.MaxHeight is { } maxH) Add("a.height <= $maxh", ("$maxh", maxH));
        if (q.Kind is { } kind)
        {
            string images = string.Join(",", MediaTypes.Images.Select(e => $"'{e}'"));
            where.Add(kind switch
            {
                MediaKind.Image => $"a.format IN ({images}) AND a.frames <= 1",
                MediaKind.Animation => $"a.format IN ({images}) AND a.frames > 1",
                MediaKind.Audio => $"a.format IN ({string.Join(",", MediaTypes.Audio.Select(e => $"'{e}'"))})",
                MediaKind.Data => $"a.format IN ({string.Join(",", MediaTypes.Data.Select(e => $"'{e}'"))})",
                _ => $"a.format NOT IN ({images},{string.Join(",", MediaTypes.Audio.Concat(MediaTypes.Data).Select(e => $"'{e}'"))})",
            });
        }
        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            // Every word must match somewhere: name, file path, description, original ID, tags or custom fields.
            int n = 0;
            foreach (string word in q.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8))
            {
                string p = "$w" + n++;
                string like = "%" + EscapeLike(word) + "%";
                string idMatch = long.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? $" OR a.id = {number} OR a.original_id = '{number}'" : "";
                Add($"(a.name LIKE {p} ESCAPE '\\' OR a.path LIKE {p} ESCAPE '\\' OR a.description LIKE {p} ESCAPE '\\' OR a.original_id LIKE {p} ESCAPE '\\' OR EXISTS (SELECT 1 FROM asset_tags t WHERE t.asset_id = a.id AND t.tag LIKE {p} ESCAPE '\\') OR EXISTS (SELECT 1 FROM asset_fields f WHERE f.asset_id = a.id AND f.value LIKE {p} ESCAPE '\\'){idMatch})", (p, like));
            }
        }
        string filter = where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where);
        string order = q.CollectionId is { } cid && q.Sort == AssetSort.Name
            ? $"(SELECT position FROM collection_items ci WHERE ci.asset_id = a.id AND ci.collection_id = {cid}), a.name COLLATE NOCASE"
            : q.Sort switch
            {
                AssetSort.Newest => "a.created_utc DESC, a.id DESC",
                AssetSort.Modified => "a.modified_utc DESC, a.id DESC",
                AssetSort.Category => "a.category COLLATE NOCASE, a.name COLLATE NOCASE",
                AssetSort.Size => "a.size DESC",
                AssetSort.Id => "CAST(a.original_id AS INTEGER), a.name COLLATE NOCASE",
                _ => "a.name COLLATE NOCASE, a.id",
            };
        using var c = Connect();
        // 8.8.1: the number of matches comes with the page (a window count), so the filter (LIKE over names,
        // paths, tags and fields) is evaluated once instead of twice; a page past the end still counts.
        long total = -1;
        var page = QueryAssets(c, $"SELECT {Prefixed("a")}, COUNT(*) OVER () FROM assets a{filter} ORDER BY {order} LIMIT {Math.Clamp(q.Limit, 1, 100_000)} OFFSET {Math.Max(0, q.Offset)}",
            r => total = r.GetInt64(AssetColumnCount), args.ToArray());
        if (total < 0) total = Convert.ToInt64(Scalar(c, "SELECT COUNT(*) FROM assets a" + filter, args.ToArray()), CultureInfo.InvariantCulture);
        return new AssetPage(page, (int)total);
    }

    /// <summary>Every asset ID matching the query (all pages), for "select all" and workflows.</summary>
    public IReadOnlyList<long> SearchIds(AssetQuery q) => Search(q with { Offset = 0, Limit = 100_000 }).Assets.Select(a => a.Id).ToList();

    public LibraryFacets Facets()
    {
        using var c = Connect();
        IReadOnlyList<(string, int)> Distinct(string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var list = new List<(string, int)>();
            while (r.Read()) if (!r.IsDBNull(0) && r.GetString(0).Length > 0) list.Add((r.GetString(0), r.GetInt32(1)));
            return list;
        }
        return new LibraryFacets(
            Distinct("SELECT format, COUNT(*) FROM assets GROUP BY format ORDER BY format"),
            Distinct("SELECT client_version, COUNT(*) FROM assets WHERE client_version IS NOT NULL GROUP BY client_version ORDER BY client_version DESC"),
            Distinct("SELECT COALESCE(source_tool, source), COUNT(*) FROM assets GROUP BY COALESCE(source_tool, source) ORDER BY 1"),
            Distinct("SELECT creator, COUNT(*) FROM assets WHERE creator IS NOT NULL GROUP BY creator ORDER BY 2 DESC LIMIT 200"),
            Distinct("SELECT tag, COUNT(*) FROM asset_tags GROUP BY tag ORDER BY 2 DESC, tag LIMIT 500"));
    }

    public LibraryStatistics Statistics()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(missing), 0), COALESCE(SUM(frames > 1), 0), COALESCE(SUM(size), 0) FROM assets";
        using var r = cmd.ExecuteReader();
        r.Read();
        int assets = r.GetInt32(0), missing = r.GetInt32(1), animated = r.GetInt32(2);
        long bytes = r.GetInt64(3);
        r.Close();
        int collections = Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM collections"), CultureInfo.InvariantCulture);
        int roots = Convert.ToInt32(Scalar(c, "SELECT COUNT(*) FROM roots"), CultureInfo.InvariantCulture);
        var last = Scalar(c, "SELECT MAX(last_scan_utc) FROM roots") as string;
        return new LibraryStatistics(assets, missing, animated, bytes, collections, roots, last == null ? null : ParseIso(last));
    }

    // ------------------------------------------------------------------ categories

    public IReadOnlyList<CategoryInfo> Categories()
    {
        using var c = Connect();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT category, COUNT(*) FROM assets GROUP BY category";
            using var r = cmd.ExecuteReader();
            while (r.Read()) counts[r.GetString(0)] = r.GetInt32(1);
        }
        var custom = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT name, description FROM categories ORDER BY name COLLATE NOCASE";
            using var r = cmd.ExecuteReader();
            while (r.Read()) custom[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
        }
        var list = AssetCategories.BuiltIn.Select(n => new CategoryInfo(n, false, null, counts.GetValueOrDefault(n))).ToList();
        list.AddRange(custom.Where(k => !AssetCategories.IsBuiltIn(k.Key)).Select(k => new CategoryInfo(k.Key, true, k.Value, counts.GetValueOrDefault(k.Key))));
        // Categories used by assets but not declared (written by an older or newer version) are listed too.
        list.AddRange(counts.Keys.Where(k => !AssetCategories.IsBuiltIn(k) && !custom.ContainsKey(k)).Select(k => new CategoryInfo(k, true, null, counts[k])));
        return list;
    }

    public bool CategoryExists(string name) => AssetCategories.IsBuiltIn(name) || Categories().Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public void AddCategory(string name, string? description = null)
    {
        name = ValidName(name, "category");
        if (AssetCategories.IsBuiltIn(name)) return;
        using var c = Connect();
        Exec(c, "INSERT OR IGNORE INTO categories(name, description) VALUES ($n, $d)", ("$n", name), ("$d", description));
    }

    /// <summary>Renames a custom category; its assets follow.</summary>
    public void RenameCategory(string name, string newName)
    {
        if (AssetCategories.IsBuiltIn(name)) throw new InvalidOperationException($"\"{name}\" is a built-in category and cannot be renamed.");
        newName = ValidName(newName, "category");
        using var c = Connect();
        using var tx = c.BeginTransaction();
        Exec(c, "DELETE FROM categories WHERE name = $o", ("$o", name));
        if (!AssetCategories.IsBuiltIn(newName)) Exec(c, "INSERT OR IGNORE INTO categories(name) VALUES ($n)", ("$n", newName));
        Exec(c, "UPDATE assets SET category = $n WHERE category = $o COLLATE NOCASE", ("$n", newName), ("$o", name));
        tx.Commit();
    }

    /// <summary>Deletes a custom category; its assets move to <see cref="AssetCategories.Other"/>. Returns the number of assets moved.</summary>
    public int DeleteCategory(string name)
    {
        if (AssetCategories.IsBuiltIn(name)) throw new InvalidOperationException($"\"{name}\" is a built-in category and cannot be deleted.");
        using var c = Connect();
        using var tx = c.BeginTransaction();
        Exec(c, "DELETE FROM categories WHERE name = $o", ("$o", name));
        int moved = Exec(c, "UPDATE assets SET category = $n WHERE category = $o COLLATE NOCASE", ("$n", AssetCategories.Other), ("$o", name));
        tx.Commit();
        return moved;
    }

    // ------------------------------------------------------------------ collections

    public IReadOnlyList<AssetCollection> Collections()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT c.id, c.name, c.description, c.thumbnail_asset_id, c.created_utc, c.modified_utc, (SELECT COUNT(*) FROM collection_items i WHERE i.collection_id = c.id) FROM collections c ORDER BY c.name COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        var list = new List<AssetCollection>();
        while (r.Read())
            list.Add(new AssetCollection(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3),
                ParseIso(r.GetString(4)), ParseIso(r.GetString(5)), r.GetInt32(6)));
        return list;
    }

    public AssetCollection? Collection(long id) => Collections().FirstOrDefault(c => c.Id == id);

    public AssetCollection? CollectionNamed(string name) => Collections().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public long CreateCollection(string name, string? description = null)
    {
        name = ValidName(name, "collection");
        if (CollectionNamed(name) != null) throw new InvalidOperationException($"A collection named \"{name}\" already exists.");
        using var c = Connect();
        string now = Iso(DateTime.UtcNow);
        return Convert.ToInt64(Scalar(c, "INSERT INTO collections(name, description, created_utc, modified_utc) VALUES ($n, $d, $now, $now); SELECT last_insert_rowid();",
            ("$n", name), ("$d", description), ("$now", now)), CultureInfo.InvariantCulture);
    }

    /// <summary>The collection with this name, created when it does not exist yet.</summary>
    public long EnsureCollection(string name, string? description = null) => CollectionNamed(name)?.Id ?? CreateCollection(name, description);

    public void RenameCollection(long id, string name)
    {
        name = ValidName(name, "collection");
        if (CollectionNamed(name) is { } other && other.Id != id) throw new InvalidOperationException($"A collection named \"{name}\" already exists.");
        using var c = Connect();
        Exec(c, "UPDATE collections SET name = $n, modified_utc = $now WHERE id = $id", ("$n", name), ("$now", Iso(DateTime.UtcNow)), ("$id", id));
    }

    public void DescribeCollection(long id, string? description, long? thumbnailAssetId)
    {
        using var c = Connect();
        Exec(c, "UPDATE collections SET description = $d, thumbnail_asset_id = $t, modified_utc = $now WHERE id = $id",
            ("$d", string.IsNullOrWhiteSpace(description) ? null : description), ("$t", thumbnailAssetId), ("$now", Iso(DateTime.UtcNow)), ("$id", id));
    }

    /// <summary>Copies a collection (its assets, order, description and thumbnail) under a new name.</summary>
    public long DuplicateCollection(long id, string newName)
    {
        var source = Collection(id) ?? throw new InvalidOperationException("The collection no longer exists.");
        long copy = CreateCollection(newName, source.Description);
        DescribeCollection(copy, source.Description, source.ThumbnailAssetId);
        AddToCollection(copy, CollectionItems(id));
        return copy;
    }

    /// <summary>Deletes a collection. Its assets stay in the library.</summary>
    public void DeleteCollection(long id)
    {
        using var c = Connect();
        Exec(c, "DELETE FROM collections WHERE id = $id", ("$id", id));
    }

    /// <summary>The asset IDs of a collection, in order.</summary>
    public IReadOnlyList<long> CollectionItems(long id)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT asset_id FROM collection_items WHERE collection_id = $id ORDER BY position";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        var list = new List<long>();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    /// <summary>Adds assets at the end of a collection (assets already in it keep their place). Returns the number added.</summary>
    public int AddToCollection(long collectionId, IEnumerable<long> assetIds)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        long next = Convert.ToInt64(Scalar(c, "SELECT COALESCE(MAX(position), -1) + 1 FROM collection_items WHERE collection_id = $c", ("$c", collectionId)), CultureInfo.InvariantCulture);
        int added = 0;
        foreach (long id in assetIds.Distinct())
        {
            int n = Exec(c, "INSERT OR IGNORE INTO collection_items(collection_id, asset_id, position) VALUES ($c, $a, $p)", ("$c", collectionId), ("$a", id), ("$p", next));
            if (n > 0) { added++; next++; }
        }
        Exec(c, "UPDATE collections SET modified_utc = $now, thumbnail_asset_id = COALESCE(thumbnail_asset_id, (SELECT asset_id FROM collection_items WHERE collection_id = $c ORDER BY position LIMIT 1)) WHERE id = $c",
            ("$now", Iso(DateTime.UtcNow)), ("$c", collectionId));
        tx.Commit();
        return added;
    }

    public int RemoveFromCollection(long collectionId, IEnumerable<long> assetIds)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        int removed = assetIds.Sum(id => Exec(c, "DELETE FROM collection_items WHERE collection_id = $c AND asset_id = $a", ("$c", collectionId), ("$a", id)));
        Exec(c, "UPDATE collections SET modified_utc = $now WHERE id = $c", ("$now", Iso(DateTime.UtcNow)), ("$c", collectionId));
        tx.Commit();
        return removed;
    }

    /// <summary>The collections an asset belongs to.</summary>
    public IReadOnlyList<string> CollectionsOf(long assetId)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT c.name FROM collections c JOIN collection_items i ON i.collection_id = c.id WHERE i.asset_id = $a ORDER BY c.name COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$a", assetId);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>
    /// Writes a collection as a portable JSON file (<c>*.tfcollection.json</c>): its name, description
    /// and each asset's file name, content hash and metadata. The files themselves are not included.
    /// </summary>
    public void ExportCollection(long id, string file)
    {
        var col = Collection(id) ?? throw new InvalidOperationException("The collection no longer exists.");
        var assets = Get(CollectionItems(id));
        var doc = new CollectionFile
        {
            Format = CollectionFile.FormatName, Version = 1, Name = col.Name, Description = col.Description, ExportedUtc = DateTime.UtcNow,
            Software = AppIdentity.NameAndVersion,
            Assets = assets.Select(a => new CollectionFileAsset
            {
                Path = a.Path, FileName = a.FileName, Sha256 = a.Sha256, Name = a.Name, Category = a.Category, Description = a.Description, ClientVersion = a.ClientVersion,
                OriginalId = a.OriginalId, Creator = a.Creator, Copyright = a.Copyright, Tags = a.Tags.ToList(), Fields = a.Fields.ToDictionary(k => k.Key, k => k.Value),
            }).ToList(),
        };
        AtomicFile.WriteAllText(file, JsonSerializer.Serialize(doc, LibraryJson.Options));
    }

    /// <summary>
    /// Reads a collection file and creates the collection from the assets of this library that match
    /// (by content hash first, then by path). Returns the new collection and the entries not found.
    /// Only metadata is read from the file; nothing in it is executed or opened.
    /// </summary>
    public (long CollectionId, string Name, int Matched, IReadOnlyList<string> NotFound) ImportCollection(string file)
    {
        var info = new FileInfo(file);
        if (info.Length > 64L << 20) throw new InvalidDataException($"{info.Name} is too large for a collection file ({info.Length / (1 << 20)} MB).");
        CollectionFile doc;
        try
        {
            doc = JsonSerializer.Deserialize<CollectionFile>(File.ReadAllText(file), LibraryJson.Options) ?? throw new InvalidDataException("The file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{info.Name} is not a Tibia Foundry collection file: {ex.Message}", ex);
        }
        if (doc.Format != CollectionFile.FormatName) throw new InvalidDataException($"{info.Name} is not a Tibia Foundry collection file.");
        string name = string.IsNullOrWhiteSpace(doc.Name) ? Path.GetFileNameWithoutExtension(file) : doc.Name.Trim();
        string unique = name;
        for (int i = 2; CollectionNamed(unique) != null; i++) unique = $"{name} ({i})";
        long id = CreateCollection(unique, doc.Description);
        var matched = new List<long>();
        var notFound = new List<string>();
        foreach (var entry in doc.Assets ?? [])
        {
            AssetRecord? hit = entry.Sha256 != null ? FindBySha(entry.Sha256).FirstOrDefault(a => !a.Missing) : null;
            if (hit == null && entry.Path != null && Path.IsPathFullyQualified(entry.Path)) hit = FindByPath(entry.Path);
            if (hit != null) matched.Add(hit.Id);
            else notFound.Add(entry.FileName ?? entry.Name ?? entry.Sha256 ?? "(unnamed)");
        }
        AddToCollection(id, matched);
        return (id, unique, matched.Count, notFound);
    }

    // ------------------------------------------------------------------ roots

    public IReadOnlyList<LibraryRoot> Roots()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, path, recursive, category, source, added_utc, last_scan_utc FROM roots ORDER BY path COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        var list = new List<LibraryRoot>();
        while (r.Read())
            list.Add(new LibraryRoot(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                ParseIso(r.GetString(5)), r.IsDBNull(6) ? null : ParseIso(r.GetString(6))));
        return list;
    }

    /// <summary>Registers a folder to index (or returns the existing registration).</summary>
    public LibraryRoot AddRoot(string folder, bool recursive = true, string? category = null, string? source = null)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        using (var c = Connect())
            Exec(c, "INSERT OR IGNORE INTO roots(path, recursive, category, source, added_utc) VALUES ($p, $r, $c, $s, $now)",
                ("$p", path), ("$r", recursive ? 1 : 0), ("$c", category), ("$s", source), ("$now", Iso(DateTime.UtcNow)));
        return Roots().First(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Stops indexing a folder. Its assets stay in the library (remove them separately).</summary>
    public void RemoveRoot(long id)
    {
        using var c = Connect();
        Exec(c, "DELETE FROM roots WHERE id = $id", ("$id", id));
    }

    public void MarkScanned(long rootId)
    {
        using var c = Connect();
        Exec(c, "UPDATE roots SET last_scan_utc = $now WHERE id = $id", ("$now", Iso(DateTime.UtcNow)), ("$id", rootId));
    }

    // ------------------------------------------------------------------ safety

    /// <summary>SQLite's own consistency check of the index ("ok" when sound).</summary>
    public string CheckDatabase()
    {
        using var c = Connect();
        return Scalar(c, "PRAGMA integrity_check") as string ?? "unknown";
    }

    /// <summary>
    /// Writes a backup ZIP: a consistent copy of the index (SQLite online backup, safe while the
    /// library is in use) and the profiles, projects and workflows. Thumbnails are left out (they are
    /// rebuilt); the indexed files themselves are not part of the library and are not copied.
    /// </summary>
    public void Backup(string zipFile)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"tf-library-{Guid.NewGuid():N}.db");
        try
        {
            using (var source = Connect())
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
            }
            AtomicFile.Write(zipFile, tempZip =>
            {
                using var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(temp, DatabaseFileName, CompressionLevel.Optimal);
                foreach (string dir in new[] { ProfilesFolder, ProjectsFolder, WorkflowsFolder })
                    foreach (string f in Directory.EnumerateFiles(dir, "*.json"))
                        zip.CreateEntryFromFile(f, $"{Path.GetFileName(dir)}/{Path.GetFileName(f)}", CompressionLevel.Optimal);
                var manifest = zip.CreateEntry("backup.json");
                using var w = new StreamWriter(manifest.Open(), new UTF8Encoding(false));
                w.Write(JsonSerializer.Serialize(new { format = "tibiafoundry-library-backup", schema = SchemaVersion, created = DateTime.UtcNow, software = AppIdentity.NameAndVersion }, LibraryJson.Options));
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Replaces the library's index, profiles, projects and workflows with those of a backup ZIP.
    /// The backup is checked first (entry names, SQLite integrity, schema); the current index is kept
    /// as <c>library.db.before-restore-*</c>, so a restore can be undone by hand.
    /// </summary>
    public static AssetLibrary Restore(string folder, string zipFile)
    {
        string full = Path.GetFullPath(folder);
        Directory.CreateDirectory(full);
        string staging = Path.Combine(full, $".restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(zipFile))
            {
                if (zip.GetEntry(DatabaseFileName) == null) throw new InvalidDataException($"{Path.GetFileName(zipFile)} is not a library backup (no {DatabaseFileName}).");
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith('/')) continue;
                    // Only the known parts of a backup are restored; any other entry is ignored, and no path can leave the folder.
                    bool known = e.FullName == DatabaseFileName || (e.FullName.Count(ch => ch == '/') == 1 && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.Split('/')[0] is "profiles" or "projects" or "workflows");
                    if (!known) continue;
                    if (e.Length > 2L << 30) throw new InvalidDataException($"{e.FullName} in the backup is too large.");
                    string target = Path.GetFullPath(Path.Combine(staging, e.FullName));
                    if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target);
                }
            }
            string db = Path.Combine(staging, DatabaseFileName);
            using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()))
            {
                check.Open();
                using var cmd = check.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check";
                if (cmd.ExecuteScalar() as string != "ok") throw new InvalidDataException("The index in the backup is damaged (SQLite integrity check failed). Nothing was changed.");
                cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema'";
                if (!int.TryParse(cmd.ExecuteScalar() as string, out int schema) || schema > SchemaVersion)
                    throw new InvalidDataException("The backup was made by a newer version of Tibia Foundry, or is not a library backup. Nothing was changed.");
            }
            SqliteConnection.ClearAllPools();
            string current = Path.Combine(full, DatabaseFileName);
            if (File.Exists(current))
            {
                string keep = current + $".before-restore-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(current, keep);
                foreach (string side in new[] { current + "-wal", current + "-shm" })
                    if (File.Exists(side)) File.Move(side, keep + side[current.Length..]);
            }
            File.Move(db, current);
            foreach (string dir in new[] { "profiles", "projects", "workflows" })
            {
                string from = Path.Combine(staging, dir);
                if (!Directory.Exists(from)) continue;
                string to = Path.Combine(full, dir);
                Directory.CreateDirectory(to);
                foreach (string f in Directory.EnumerateFiles(from)) File.Move(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
            }
            return Open(full);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string Prefixed(string alias) => string.Join(", ", AssetColumns.Split(", ").Select(c => alias + "." + c));

    private static readonly int AssetColumnCount = AssetColumns.Split(", ").Length;

    private static List<AssetRecord> QueryAssets(SqliteConnection c, string sql, params (string Name, object? Value)[] args) => QueryAssets(c, sql, null, args);

    /// <param name="extra">Reads columns after the asset columns of each row (for example a window count).</param>
    private static List<AssetRecord> QueryAssets(SqliteConnection c, string sql, Action<SqliteDataReader>? extra, params (string Name, object? Value)[] args)
    {
        var list = new List<AssetRecord>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                list.Add(new AssetRecord
                {
                    Id = r.GetInt64(0), Path = r.GetString(1), Name = r.GetString(2), Category = r.GetString(3), Description = S(4), Source = S(5), SourceTool = S(6),
                    ClientVersion = S(7), OriginalId = S(8), Format = r.GetString(9), Width = r.GetInt32(10), Height = r.GetInt32(11), Frames = r.GetInt32(12),
                    SizeBytes = r.GetInt64(13), Sha256 = S(14), PixelHash = r.IsDBNull(15) ? null : r.GetInt64(15), PixelKey = S(16), CreatedUtc = ParseIso(r.GetString(17)),
                    ModifiedUtc = ParseIso(r.GetString(18)), FileModifiedUtc = ParseIso(r.GetString(19)), Creator = S(20), Attribution = S(21), Copyright = S(22),
                    ExportStatus = r.GetString(23), Missing = r.GetInt64(24) != 0, Managed = r.GetInt64(25) != 0,
                });
                extra?.Invoke(r);
            }
        }
        if (list.Count == 0) return list;
        // Tags and custom fields of the page, in two queries.
        string ids = string.Join(",", list.Select(a => a.Id));
        var tags = new Dictionary<long, List<string>>();
        var fields = new Dictionary<long, Dictionary<string, string>>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT asset_id, tag FROM asset_tags WHERE asset_id IN ({ids}) ORDER BY tag COLLATE NOCASE";
            using var r = cmd.ExecuteReader();
            while (r.Read()) (tags.TryGetValue(r.GetInt64(0), out var l) ? l : tags[r.GetInt64(0)] = []).Add(r.GetString(1));
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT asset_id, key, value FROM asset_fields WHERE asset_id IN ({ids}) ORDER BY key COLLATE NOCASE";
            using var r = cmd.ExecuteReader();
            while (r.Read()) (fields.TryGetValue(r.GetInt64(0), out var d) ? d : fields[r.GetInt64(0)] = new(StringComparer.OrdinalIgnoreCase))[r.GetString(1)] = r.GetString(2);
        }
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (tags.TryGetValue(a.Id, out var t) || fields.ContainsKey(a.Id))
                list[i] = a with { Tags = t ?? [], Fields = fields.TryGetValue(a.Id, out var f) ? f : new Dictionary<string, string>() };
        }
        return list;
    }

    private static void ReplaceTags(SqliteConnection c, long id, IEnumerable<string> tags)
    {
        Exec(c, "DELETE FROM asset_tags WHERE asset_id = $a", ("$a", id));
        foreach (string t in NormalizeTags(tags)) Exec(c, "INSERT OR IGNORE INTO asset_tags(asset_id, tag) VALUES ($a, $t)", ("$a", id), ("$t", t));
    }

    /// <summary>Tags as stored: trimmed, without empty ones or duplicates, at most 64 characters each.</summary>
    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.SelectMany(t => t.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(t => t.Length > 64 ? t[..64] : t).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string ValidName(string name, string what)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException($"The {what} needs a name.");
        if (name.Length > 120) throw new ArgumentException($"The {what} name is too long (at most 120 characters).");
        return name;
    }

    private static int Exec(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var result = cmd.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    internal static string Iso(DateTime utc) => (utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static DateTime ParseIso(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : default;
}

/// <summary>The portable form of a collection (<c>*.tfcollection.json</c>).</summary>
public sealed class CollectionFile
{
    public const string FormatName = "tibiafoundry-collection";
    public string? Format { get; set; }
    public int Version { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTime ExportedUtc { get; set; }
    public string? Software { get; set; }
    public List<CollectionFileAsset>? Assets { get; set; }
}

public sealed class CollectionFileAsset
{
    public string? Path { get; set; }
    public string? FileName { get; set; }
    public string? Sha256 { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? ClientVersion { get; set; }
    public string? OriginalId { get; set; }
    public string? Creator { get; set; }
    public string? Copyright { get; set; }
    public List<string>? Tags { get; set; }
    public Dictionary<string, string>? Fields { get; set; }
}

/// <summary>JSON settings of the library's files (indented, camelCase, enums as text).</summary>
public static class LibraryJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        MaxDepth = 32,
    };
}
