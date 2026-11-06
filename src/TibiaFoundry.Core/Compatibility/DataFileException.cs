using TibiaFoundry.Core.Localization;
namespace TibiaFoundry.Core.Compatibility;

/// <summary>Why a client data file could not be used.</summary>
public enum DataProblemKind
{
    /// <summary>The file does not exist (client not installed, incomplete update, wrong folder).</summary>
    Missing,
    /// <summary>The file exists but could not be read (locked, permissions, I/O error).</summary>
    Unreadable,
    /// <summary>The bytes could not be decoded with the schema this version knows: the file is damaged, or the client changed its format.</summary>
    Undecodable,
}

/// <summary>
/// A client data file that could not be loaded, with the file and a classification the UI can
/// explain to users ("missing", "could not be read", "damaged or changed format").
/// </summary>
public sealed class DataFileException(DataProblemKind kind, string filePath, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public DataProblemKind Kind { get; } = kind;
    public string FilePath { get; } = filePath;
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Plain-language advice for the user.</summary>
    public string Advice => Kind switch
    {
        DataProblemKind.Missing => L.T("DataFileException.StartTheTibiaClientOnceSo"),
        DataProblemKind.Unreadable => L.T("DataFileException.CloseProgramsThatMayLockThe"),
        _ => L.T("DataFileException.IfTheTibiaClientWasUpdated"),
    };
}
