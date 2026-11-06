using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;

namespace TibiaFoundry.Core.Clients;

/// <summary>
/// The only place client management writes, moves or deletes files. Every path is checked to lie
/// inside one allowed folder (the client store, or the empty folder of an export or a store move),
/// so a Tibia client folder can never be changed by mistake. Source files are only ever read
/// (<see cref="OpenSource"/>). An architecture test keeps every other file in this folder free of
/// file-system writes.
/// </summary>
internal sealed class StoreFiles(string root)
{
    public string Root { get; } = PathGuard.Normalize(root);

    private string Guard(string path)
    {
        string full = PathGuard.Normalize(path);
        if (!PathGuard.IsSameOrInside(Root, full))
            throw new UnsafePathException($"Refused to change \"{full}\": client management only writes inside \"{Root}\".");
        return full;
    }

    /// <summary>Opens a client file for reading only (shared, so the client and its launcher are never blocked).</summary>
    public static FileStream OpenSource(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    public void CreateDirectory(string path) => Directory.CreateDirectory(Guard(path));

    /// <summary>A new file for writing; fails when it exists.</summary>
    public FileStream CreateNew(string path)
    {
        string full = Guard(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
    }

    /// <summary>Replaces a file completely or not at all (temporary file in the same folder, then a move).</summary>
    public void WriteAtomic(string path, byte[] bytes) => AtomicFile.WriteAllBytes(Guard(path), bytes);

    /// <summary>Renames a file without replacing an existing one. Returns false when the destination exists.</summary>
    public bool MoveNoReplace(string source, string destination)
    {
        string from = Guard(source), to = Guard(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        try
        {
            File.Move(from, to, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(to))
        {
            return false;
        }
    }

    /// <summary>Renames a folder (inside the allowed folder); fails when the destination exists.</summary>
    public void MoveDirectory(string source, string destination)
    {
        string from = Guard(source), to = Guard(destination);
        if (Directory.Exists(to) || File.Exists(to)) throw new IOException($"{to} exists already; nothing was moved.");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
    }

    /// <summary>Sets a written file's last-write time (a rebuild restores the recorded times).</summary>
    public void RestoreWriteTime(string path, DateTime utc) => File.SetLastWriteTimeUtc(Guard(path), utc);

    /// <summary>Adds attributes (read-only, hidden) to a written file.</summary>
    public void SetAttributes(string path, FileAttributes attributes)
    {
        string full = Guard(path);
        File.SetAttributes(full, File.GetAttributes(full) | attributes);
    }

    public void SetReadOnly(string path)
    {
        string full = Guard(path);
        File.SetAttributes(full, File.GetAttributes(full) | FileAttributes.ReadOnly);
    }

    public void DeleteFile(string path)
    {
        string full = Guard(path);
        if (!File.Exists(full)) return;
        var attributes = File.GetAttributes(full);
        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(full, attributes & ~FileAttributes.ReadOnly);
        File.Delete(full);
    }

    /// <summary>Deletes a folder and everything in it (read-only objects included); links inside are removed, never followed.</summary>
    public void DeleteDirectory(string path)
    {
        string full = Guard(path);
        if (string.Equals(full, Root, StringComparison.OrdinalIgnoreCase)) throw new UnsafePathException("Refused to delete the whole store folder this way.");
        var info = new DirectoryInfo(full);
        if (!info.Exists) return;
        if (PathGuard.IsReparsePoint(info)) { info.Delete(); return; }
        foreach (var entry in info.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo d) DeleteDirectory(d.FullName);
            else DeleteFile(entry.FullName);
        }
        info.Delete();
    }

    /// <summary>Copies a file from anywhere (only read) to a new file inside the allowed folder; returns the SHA-256 of the bytes written.</summary>
    public string CopyIn(string source, string destination, CancellationToken ct)
    {
        using var input = OpenSource(source);
        using var output = CreateNew(destination);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            int n;
            while ((n = input.Read(buffer, 0, 1 << 20)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, n);
                output.Write(buffer, 0, n);
            }
            output.Flush(flushToDisk: true);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The lock file of the store, held open without sharing while one writer works.</summary>
    public FileStream OpenLock(string path) => new(Guard(path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
