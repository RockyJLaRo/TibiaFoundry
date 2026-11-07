using NAudio.Vorbis;
using NAudio.Wave;
using TibiaFoundry.Core.Diagnostics;

namespace TibiaFoundry.App.Services;

/// <summary>
/// Plays OGG (Vorbis) and WAV sound files through NAudio. One sound plays at a time; starting a
/// new one stops the previous. The original created a new reader per playback without disposing
/// it and re-initialized a playing output device, which leaked handles and could throw.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly object _sync = new();
    private WaveOutEvent? _output;
    private WaveStream? _reader;
    private float _volume = 0.8f;

    public event Action<string?>? PlaybackStateChanged;

    public string? CurrentFile { get; private set; }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (_sync)
            {
                if (_output != null) _output.Volume = _volume;
            }
        }
    }

    public void Play(string path)
    {
        Stop();
        lock (_sync)
        {
            WaveStream reader = OpenReader(path);
            var output = new WaveOutEvent { Volume = _volume };
            try
            {
                output.Init(reader);
                output.PlaybackStopped += OnStopped;
                output.Play();
            }
            catch
            {
                // No audio device, or a file the decoder rejects: release the file and the device.
                output.Dispose();
                reader.Dispose();
                throw;
            }
            _reader = reader;
            _output = output;
            CurrentFile = path;
        }
        PlaybackStateChanged?.Invoke(path);
    }

    /// <summary>
    /// Opens a decoder for an OGG Vorbis or WAV file (no playback). The format is taken from the
    /// file's first bytes ("OggS"), because files of an imported client version are stored without
    /// their names (7.3); the extension decides only when the header says nothing.
    /// </summary>
    public static WaveStream OpenReader(string path) => IsOgg(path) ? new VorbisWaveReader(path) : new WaveFileReader(path);

    private static bool IsOgg(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) == 4)
            {
                if (magic.SequenceEqual("OggS"u8)) return true;
                if (magic.SequenceEqual("RIFF"u8)) return false;
            }
        }
        return Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    public void Stop()
    {
        bool wasPlaying;
        lock (_sync)
        {
            wasPlaying = _output != null;
            if (_output != null)
            {
                _output.PlaybackStopped -= OnStopped;
                _output.Stop();
                _output.Dispose();
                _output = null;
            }
            _reader?.Dispose();
            _reader = null;
            CurrentFile = null;
        }
        if (wasPlaying) PlaybackStateChanged?.Invoke(null);
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null) AppLog.Warn("Playback stopped with an error.", e.Exception);
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _output)) return;
            _output?.Dispose();
            _output = null;
            _reader?.Dispose();
            _reader = null;
            CurrentFile = null;
        }
        PlaybackStateChanged?.Invoke(null);
    }

    public void Dispose() => Stop();
}
