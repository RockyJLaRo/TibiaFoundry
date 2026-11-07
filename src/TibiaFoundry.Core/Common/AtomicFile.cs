using System.Text;

namespace TibiaFoundry.Core.Common;

/// <summary>
/// Writes files so that the destination is either left untouched or completely replaced. Data is
/// written to a uniquely named temporary file in the destination folder (same volume, so the final
/// move is atomic) and moved over the destination only after writing succeeded. On failure or
/// cancellation the temporary file is deleted.
/// </summary>
/// <remarks>
/// Replaces ad-hoc "write directly" and fixed "&lt;file&gt;.tmp" patterns, which left partial or
/// stray files behind and collided when two exports targeted the same file.
/// </remarks>
public static class AtomicFile
{
    /// <summary>Runs <paramref name="write"/> against a temporary path, then moves it to <paramref name="path"/>.</summary>
    public static void Write(string path, Action<string> write)
    {
        string temp = PrepareTemp(path);
        try
        {
            write(temp);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public static async Task WriteAsync(string path, Func<string, Task> write)
    {
        string temp = PrepareTemp(path);
        try
        {
            await write(temp).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Writes <paramref name="bytes"/> atomically; durably (flushed to the disk before the file is replaced) unless
    /// <paramref name="durable"/> is false, for bulk or regenerable files (thumbnails, extracted sounds) where the flush only costs time.</summary>
    public static void WriteAllBytes(string path, byte[] bytes, bool durable = true) =>
        Write(path, temp => { if (durable) WriteDurable(temp, bytes); else File.WriteAllBytes(temp, bytes); });

    /// <summary>
    /// 9.0 (audit F-18): settings, catalogs, manifests and other documents are flushed to the disk before they
    /// replace the old file, so a power failure right after a save leaves the old or the new file, never an empty
    /// one (NTFS journals the rename, not the data). Bulk image exports keep <see cref="WriteVerified"/>.
    /// </summary>
    private static void WriteDurable(string temp, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> atomically and reads them back before the file is moved into
    /// place; a difference throws the exception made by <paramref name="mismatch"/> and nothing is replaced.
    /// </summary>
    /// <remarks>
    /// 8.8.1: the read-back uses the handle that wrote the file. Opening the just-closed temporary file
    /// again made Windows scan it for malware before the read could start, which was a third of the time
    /// of a sprite extraction (thousands of small PNG files); the bytes are read back the same way.
    /// </remarks>
    public static void WriteVerified(string path, ReadOnlySpan<byte> bytes, Func<Exception> mismatch)
    {
        string temp = PrepareTemp(path);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None))
            {
                stream.Write(bytes);
                stream.Flush();
                stream.Position = 0;
                byte[] check = System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Max(1, bytes.Length));
                try
                {
                    int read = stream.ReadAtLeast(check.AsSpan(0, bytes.Length), bytes.Length, throwOnEndOfStream: false);
                    if (read != bytes.Length || stream.Length != bytes.Length || !check.AsSpan(0, read).SequenceEqual(bytes)) throw mismatch();
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(check);
                }
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Writes UTF-8 text (no BOM) atomically and durably.</summary>
    public static void WriteAllText(string path, string text) => Write(path, temp => WriteDurable(temp, new UTF8Encoding(false).GetBytes(text)));

    public static Task WriteAllTextAsync(string path, string text, CancellationToken ct = default) =>
        WriteAsync(path, temp => File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), ct));

    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/> atomically.</summary>
    public static void Copy(string source, string destination) => Write(destination, temp => File.Copy(source, temp, overwrite: true));

    private static string PrepareTemp(string path)
    {
        string full = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir ?? "", $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDelete(string temp)
    {
        try
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temporary file is hidden-named and harmless; the original error matters more.
        }
    }
}
