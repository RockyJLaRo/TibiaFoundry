using System.Buffers;
using System.Security.Cryptography;

namespace TibiaFoundry.Core.FileOps;

/// <summary>Hash algorithms offered for checksums.</summary>
public enum HashAlgorithmKind
{
    /// <summary>SHA-256: the default; also used by the Tibia client for its file names.</summary>
    Sha256,
    Sha512,
    /// <summary>SHA-1: only for comparing with older checksum lists (no longer collision-resistant).</summary>
    Sha1,
    /// <summary>MD5: only for comparing with older checksum lists (not collision-resistant).</summary>
    Md5,
}

/// <summary>
/// Streaming file hashes with cancellation. A matching hash shows that two files have identical
/// content; it says nothing about whether that content is a valid file of its format.
/// </summary>
public static class FileHashing
{
    private const int BufferSize = 1 << 20;

    public static string Name(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Sha512 => "SHA-512",
        HashAlgorithmKind.Sha1 => "SHA-1",
        HashAlgorithmKind.Md5 => "MD5",
        _ => "SHA-256",
    };

    /// <summary>Number of hexadecimal characters of a hash of this kind.</summary>
    public static int HexLength(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Sha512 => 128,
        HashAlgorithmKind.Sha1 => 40,
        HashAlgorithmKind.Md5 => 32,
        _ => 64,
    };

    /// <summary>True for algorithms that should only be used to compare with existing lists.</summary>
    public static bool IsWeak(HashAlgorithmKind kind) => kind is HashAlgorithmKind.Sha1 or HashAlgorithmKind.Md5;

    private static IncrementalHash Create(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Sha512 => IncrementalHash.CreateHash(HashAlgorithmName.SHA512),
        HashAlgorithmKind.Sha1 => IncrementalHash.CreateHash(HashAlgorithmName.SHA1),
        HashAlgorithmKind.Md5 => IncrementalHash.CreateHash(HashAlgorithmName.MD5),
        _ => IncrementalHash.CreateHash(HashAlgorithmName.SHA256),
    };

    /// <summary>Lower-case hexadecimal hash of a file. The file is opened for shared reading.</summary>
    /// <param name="bytesRead">Called with the number of bytes read after each block (for progress).</param>
    public static string HashFile(string path, HashAlgorithmKind kind = HashAlgorithmKind.Sha256, Action<long>? bytesRead = null, CancellationToken ct = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        return HashStream(stream, kind, bytesRead, ct);
    }

    public static string HashStream(Stream stream, HashAlgorithmKind kind = HashAlgorithmKind.Sha256, Action<long>? bytesRead = null, CancellationToken ct = default)
    {
        using var hash = Create(kind);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int n;
            while ((n = stream.Read(buffer, 0, BufferSize)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, n);
                bytesRead?.Invoke(n);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string HashBytes(ReadOnlySpan<byte> data, HashAlgorithmKind kind = HashAlgorithmKind.Sha256)
    {
        using var hash = Create(kind);
        hash.AppendData(data);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>True when the file holds exactly these bytes.</summary>
    public static bool SameContentAs(string path, ReadOnlySpan<byte> data)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == data.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(data);
    }

    /// <summary>True when both files have the same length and the same bytes (compared directly, not by hash).</summary>
    public static bool SameContent(string a, string b, CancellationToken ct = default)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        using var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        byte[] ba = ArrayPool<byte>.Shared.Rent(BufferSize);
        byte[] bb = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int na = sa.ReadAtLeast(ba.AsSpan(0, BufferSize), BufferSize, throwOnEndOfStream: false);
                int nb = sb.ReadAtLeast(bb.AsSpan(0, BufferSize), BufferSize, throwOnEndOfStream: false);
                if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
                if (na == 0) return true;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ba);
            ArrayPool<byte>.Shared.Return(bb);
        }
    }
}
