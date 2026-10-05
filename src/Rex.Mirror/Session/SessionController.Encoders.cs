using Rex.Core;

namespace Rex.Mirror.Session;

/// <summary>
/// The phone's video encoders, read with scrcpy when the Settings tab asks for them and kept for
/// each phone until the app closes. The read takes <see cref="ServerStart"/> like every other
/// server start, so it never races the mirror or a copy starting.
/// </summary>
public sealed partial class SessionController
{
    private readonly Dictionary<string, IReadOnlyList<VideoEncoder>> _encoders = new(StringComparer.Ordinal);
    private Task<EncodersRead>? _encodersRead;

    /// <summary>True while a phone's encoders are being read.</summary>
    public bool ReadingEncoders => _encodersRead is { IsCompleted: false };

    /// <summary>The encoders read from a phone, or null when they have not been read since the app started.</summary>
    public IReadOnlyList<VideoEncoder>? EncodersOf(string serial) => _encoders.GetValueOrDefault(serial);

    /// <summary>Reads a phone's video encoders; asking again while a read runs joins it.</summary>
    public Task<EncodersRead> ReadEncodersAsync(string serial)
    {
        if (_encodersRead is { IsCompleted: false } running)
        {
            return running;
        }

        _encodersRead = ReadEncodersNowAsync(serial);
        return _encodersRead;
    }

    private async Task<EncodersRead> ReadEncodersNowAsync(string serial)
    {
        if (Tools is null)
        {
            return EncodersRead.Failed("The phone tools are not installed yet.");
        }

        EncodersRead read;
        using (await ServerStart.EnterAsync().ConfigureAwait(true))
        {
            read = await EncoderList.ReadAsync(_host.Runner, Tools.Scrcpy, serial).ConfigureAwait(true);
        }

        if (read.Ok)
        {
            _encoders[serial] = read.Encoders;
            _host.Log.Info($"Read {read.Encoders.Count} video encoders from {serial}.");
        }
        else
        {
            _host.Log.Warn($"Could not read the video encoders of {serial}: {read.Error}");
        }

        return read;
    }
}
