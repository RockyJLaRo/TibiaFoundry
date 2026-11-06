using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.Diagnostics;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.Core.FileOps;

/// <summary>What to do when an output file already exists (Settings → File safety).</summary>
public enum ExistingFilePolicy
{
    /// <summary>Ask each time (with "apply to all" for batches). The default.</summary>
    Ask,
    /// <summary>Keep the existing file and write the new one as "name (2).ext".</summary>
    KeepBoth,
    /// <summary>Replace the existing file (it is backed up first when backups are on).</summary>
    Overwrite,
    /// <summary>Keep the existing file and do not write the new one.</summary>
    Skip,
}

/// <summary>The answer to "this file already exists".</summary>
public enum ConflictChoice { Overwrite, KeepBoth, Skip, Cancel }

/// <param name="ApplyToAll">Use the same answer for the remaining files of this operation.</param>
public readonly record struct ConflictAnswer(ConflictChoice Choice, bool ApplyToAll);

/// <summary>Asks the user about an existing file. Called from worker threads; implementations marshal to the UI.</summary>
public delegate ConflictAnswer ConflictPrompt(string existingPath, string operation, int remainingHint);

/// <summary>The safety settings that apply to one operation (taken from Settings when it starts).</summary>
public sealed record FileSafetyOptions
{
    public ExistingFilePolicy WhenExists { get; init; } = ExistingFilePolicy.Ask;
    public bool BackupBeforeOverwrite { get; init; } = true;
    public bool ValidateOutputs { get; init; } = true;

    public static FileSafetyOptions Default { get; } = new();
}

/// <summary>
/// The one place where tools decide how an output file is written. It applies the existing-file
/// policy (asking when configured), never lets two outputs of the same operation overwrite each
/// other, refuses to overwrite the operation's own input files, backs up files before replacing
/// them, writes through a temporary file, validates the result before it replaces anything, and
/// counts what happened for the result summary.
/// </summary>
public sealed class OutputSession : IDisposable
{
    private readonly BackupStore? _backups;
    private readonly ConflictPrompt? _prompt;
    private readonly HashSet<string> _planned = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private ConflictChoice? _all;
    private BackupSet? _backupSet;

    public OutputSession(string operation, FileSafetyOptions options, BackupStore? backups = null, ConflictPrompt? prompt = null)
    {
        Operation = operation;
        Options = options;
        _backups = backups;
        _prompt = prompt;
    }

    public string Operation { get; }
    public FileSafetyOptions Options { get; }

    public int Written { get; private set; }
    public int Overwritten { get; private set; }
    public int Renamed { get; private set; }
    public int Skipped { get; private set; }
    public List<string> BackedUp { get; } = [];

    /// <summary>The backup set used by this operation (null when nothing needed a backup).</summary>
    public string? BackupFolder => _backupSet?.Folder;

    /// <summary>Marks input files that this operation must never overwrite.</summary>
    public void Protect(IEnumerable<string> inputs)
    {
        lock (_sync)
            foreach (var p in inputs) _protected.Add(PathGuard.Normalize(p));
    }

    /// <summary>
    /// Decides where an output goes: the requested path, a "(2)" variant, or nowhere (null = skip).
    /// Throws <see cref="OperationCanceledException"/> when the user cancels in the prompt.
    /// </summary>
    public string? Resolve(string path)
    {
        string full = PathGuard.Normalize(path);
        ConflictChoice choice;
        lock (_sync)
        {
            if (_protected.Contains(full))
                throw new IOException($"{Path.GetFileName(full)} is one of this operation's input files and is not overwritten. Choose another output folder.");
            bool existsOnDisk = File.Exists(full);
            bool plannedHere = _planned.Contains(full);
            if (!existsOnDisk && !plannedHere)
            {
                _planned.Add(full);
                return full;
            }
            // Two outputs of the same operation with the same name: never overwrite our own output.
            if (plannedHere)
            {
                string unique = UniqueName(full);
                _planned.Add(unique);
                Renamed++;
                return unique;
            }
            choice = Options.WhenExists switch
            {
                ExistingFilePolicy.Overwrite => ConflictChoice.Overwrite,
                ExistingFilePolicy.KeepBoth => ConflictChoice.KeepBoth,
                ExistingFilePolicy.Skip => ConflictChoice.Skip,
                _ => _all ?? ConflictChoice.Cancel,
            };
        }
        if (Options.WhenExists == ExistingFilePolicy.Ask && _all == null)
        {
            if (_prompt == null)
            {
                // Without a way to ask (for example in an unattended run) the safe answer is to keep the existing file.
                choice = ConflictChoice.Skip;
            }
            else
            {
                var answer = _prompt(full, Operation, 0);
                choice = answer.Choice;
                if (answer.ApplyToAll && choice != ConflictChoice.Cancel) _all = choice;
            }
        }
        lock (_sync)
        {
            switch (choice)
            {
                case ConflictChoice.Cancel:
                    throw new OperationCanceledException("Cancelled at an existing file: " + full);
                case ConflictChoice.Skip:
                    Skipped++;
                    return null;
                case ConflictChoice.KeepBoth:
                    string unique = UniqueName(full);
                    _planned.Add(unique);
                    Renamed++;
                    return unique;
                default:
                    _planned.Add(full);
                    return full;
            }
        }
    }

    /// <summary>
    /// Writes one output. <paramref name="writeTemp"/> writes the content to the temporary path it
    /// gets; <paramref name="validate"/> (when validation is on) checks that temporary file and
    /// throws if it is wrong. Only then is an existing file backed up and replaced.
    /// </summary>
    public void Write(string resolvedPath, Action<string> writeTemp, Action<string>? validate = null)
    {
        bool replacing = File.Exists(resolvedPath);
        AtomicFile.Write(resolvedPath, temp =>
        {
            writeTemp(temp);
            if (Options.ValidateOutputs) validate?.Invoke(temp);
            if (replacing && Options.BackupBeforeOverwrite) BackUp(resolvedPath);
        });
        lock (_sync)
        {
            Written++;
            if (replacing) Overwritten++;
        }
    }

    /// <summary>Resolves and writes in one step. Returns the path written, or null when skipped.</summary>
    public string? WriteFile(string path, Action<string> writeTemp, Action<string>? validate = null)
    {
        string? target = Resolve(path);
        if (target == null) return null;
        Write(target, writeTemp, validate);
        return target;
    }

    public string? WriteAllBytes(string path, byte[] bytes, Action<string>? validate = null) =>
        WriteFile(path, temp => File.WriteAllBytes(temp, bytes), validate);

    /// <summary>Backs up a file this operation is about to change in place (no-op when backups are off).</summary>
    public void BackUp(string path)
    {
        if (!Options.BackupBeforeOverwrite || _backups == null || !File.Exists(path)) return;
        lock (_sync)
        {
            _backupSet ??= _backups.Begin(Operation, BackupKind.Automatic);
            _backupSet.Add(path);
            BackedUp.Add(path);
        }
        AppLog.Info($"{Operation}: backed up {path} before replacing it.");
    }

    /// <summary>"name (2).ext", "name (3).ext"... not yet on disk and not planned by this session.</summary>
    private string UniqueName(string full)
    {
        string dir = Path.GetDirectoryName(full)!;
        string stem = Path.GetFileNameWithoutExtension(full);
        string ext = Path.GetExtension(full);
        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !_planned.Contains(candidate)) return candidate;
        }
    }

    /// <summary>One line for the result summary.</summary>
    public string Describe()
    {
        var parts = new List<string> { L.F("OutputSession.0N0Written", Written) };
        if (Overwritten > 0) parts.Add(L.F("OutputSession.0N0Replaced", Overwritten));
        if (Renamed > 0) parts.Add(L.F("OutputSession.0N0SavedUnderANew", Renamed));
        if (Skipped > 0) parts.Add(L.F("OutputSession.0N0SkippedBecauseTheFile", Skipped));
        if (BackedUp.Count > 0) parts.Add(L.F("OutputSession.0N0BackedUpTo1", BackedUp.Count, BackupFolder));
        return string.Join(", ", parts) + ".";
    }

    public void Dispose() => _backupSet?.Complete();
}
