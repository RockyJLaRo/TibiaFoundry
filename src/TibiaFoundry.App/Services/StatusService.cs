using CommunityToolkit.Mvvm.ComponentModel;
using TibiaFoundry.Core.Common;
using TibiaFoundry.Core.FileOps;
using TibiaFoundry.Core.Localization;

namespace TibiaFoundry.App.Services;

public enum StatusKind { Info, Success, Warning, Error }

/// <summary>
/// A file or folder named in a status message. The status bar shows it as a link that opens the
/// folder in File Explorer (selecting the file when it is a file).
/// </summary>
/// <param name="Path">The full path of the saved file or folder.</param>
/// <param name="IsFile">True for a file (Explorer selects it), false for a folder (Explorer opens it).</param>
public sealed record StatusLink(string Path, bool IsFile)
{
    /// <summary>The visible text: the path, shortened in the middle when it is long.</summary>
    public string Label => OutputLocations.ShortenForDisplay(Path, 80);

    public string ToolTip => (IsFile ? L.T("Status.ShowInFileExplorer") : L.T("Status.OpenInFileExplorer")) + Path;
}

/// <summary>
/// Application status bar model: the current message and the progress of running operations.
/// Long operations report through <see cref="BeginOperation"/> so the UI stays responsive and
/// the user always sees what is happening. A message can name a saved file or folder
/// (<see cref="Link"/>), which the status bar shows as a clickable path.
/// </summary>
public sealed partial class StatusService : ObservableObject
{
    private int _running;

    [ObservableProperty] private string _message = L.T("Status.Ready");
    [ObservableProperty] private StatusKind _kind = StatusKind.Info;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";

    /// <summary>The saved file or folder of the current message (shown after <see cref="Message"/>), or null.</summary>
    [ObservableProperty] private StatusLink? _link;

    /// <summary>Text after the link (for example a count or a note), or "".</summary>
    [ObservableProperty] private string _messageAfterLink = "";

    /// <summary>True while the running operations can be cancelled (some cannot, by their nature).</summary>
    [ObservableProperty] private bool _canCancel;

    /// <summary>The whole message as one line (for tooltips, logs and tests).</summary>
    public string FullText => Link == null ? Message : Message + Link.Path + MessageAfterLink;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(FullText));
    partial void OnLinkChanged(StatusLink? value) => OnPropertyChanged(nameof(FullText));
    partial void OnMessageAfterLinkChanged(string value) => OnPropertyChanged(nameof(FullText));

    public void Show(string message, StatusKind kind = StatusKind.Info)
    {
        Link = null;
        MessageAfterLink = "";
        Message = message;
        Kind = kind;
    }

    /// <summary>
    /// "Saved to &lt;path&gt;": shown only after the file was written. The path is the exact file (or
    /// folder) that was created; the status bar makes it clickable.
    /// </summary>
    /// <param name="before">Text before the path, e.g. "Saved to " or "Saved 12 GIFs to ".</param>
    public void ShowSaved(string path, bool isFile, string? before = null, string after = "", StatusKind kind = StatusKind.Success)
    {
        Message = before ?? L.T("Status.SavedTo");
        MessageAfterLink = after;
        Link = new StatusLink(path, isFile);
        Kind = kind;
        Saved?.Invoke(Link);
    }

    /// <summary>Raised by <see cref="ShowSaved"/>: a file or folder was written (the Home dashboard lists recent exports).</summary>
    public event Action<StatusLink>? Saved;

    /// <summary>Starts a busy scope; dispose it when the operation ends.</summary>
    public OperationScope BeginOperation(string message, bool canCancel = true)
    {
        _running++;
        IsBusy = true;
        IsIndeterminate = true;
        Progress = 0;
        ProgressText = "";
        CanCancel = canCancel;
        Show(message);
        return new OperationScope(this, message);
    }

    private void End()
    {
        _running = Math.Max(0, _running - 1);
        if (_running == 0)
        {
            IsBusy = false;
            IsIndeterminate = false;
            Progress = 0;
            ProgressText = "";
            CanCancel = false;
        }
    }

    /// <summary>Progress sink usable from worker threads (marshals via <see cref="Progress{T}"/>).</summary>
    public sealed class OperationScope : IDisposable
    {
        private readonly StatusService _owner;
        private bool _disposed;

        internal OperationScope(StatusService owner, string title)
        {
            _owner = owner;
            Title = title;
            Reporter = new Progress<OperationProgress>(p =>
            {
                if (_disposed) return;
                _owner.IsIndeterminate = p.Total <= 0;
                _owner.Progress = p.Fraction * 100;
                _owner.ProgressText = p.Total > 0 ? $"{p.Done:N0} / {p.Total:N0}" : "";
                if (!string.IsNullOrEmpty(p.Message)) _owner.Show($"{Title} — {p.Message}");
                Reported?.Invoke(p);
            });
        }

        public string Title { get; }
        public IProgress<OperationProgress> Reporter { get; }

        /// <summary>Raised on the UI thread for every progress report (the operation tracker listens).</summary>
        public event Action<OperationProgress>? Reported;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.End();
        }
    }
}
