using System.IO;
using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Mirror.Services.Sound;

/// <summary>
/// The tests' stand-in for scrcpy's audio session: a JSON file named by <see cref="Variable"/>, read
/// on every look and written on every change. <c>available</c> false means there is no session yet;
/// <c>outsideVolume</c>/<c>outsideMuted</c> play a change made in the Windows mixer, once.
/// </summary>
internal sealed class FakePhoneSound : IPhoneSound
{
    public const string Variable = "REX_FAKE_AUDIO";

    private readonly string _path;

    private FakePhoneSound(string path) => _path = path;

    public static IPhoneSound? Find(string path) =>
        Read(path)?["available"]?.GetValue<bool>() == true ? new FakePhoneSound(path) : null;

    public bool Alive => Read(_path)?["available"]?.GetValue<bool>() == true;

    public float Volume
    {
        get => Number("volume", 1);
        set => Write(file => file["volume"] = Math.Round(value, 4));
    }

    public bool Muted
    {
        get => Read(_path)?["muted"]?.GetValue<bool>() == true;
        set => Write(file => file["muted"] = value);
    }

    public int Channels => (int)Number("channels", 2);

    public void SetChannels(float left, float right) => Write(file =>
    {
        file["left"] = Math.Round(left, 4);
        file["right"] = Math.Round(right, 4);
    });

    public float Peak => Number("peak", 0);

    public bool TryTakeOutsideChange(out float volume, out bool muted)
    {
        var file = Read(_path);
        volume = (float)(file?["outsideVolume"]?.GetValue<double>() ?? -1);
        muted = file?["outsideMuted"]?.GetValue<bool>() == true;
        if (volume < 0)
        {
            return false;
        }

        var (taken, takenMuted) = (volume, muted);
        Write(f =>
        {
            f.Remove("outsideVolume");
            f.Remove("outsideMuted");
            f["volume"] = Math.Round(taken, 4);
            f["muted"] = takenMuted;
        });
        return true;
    }

    public void Dispose()
    {
    }

    private float Number(string key, float fallback) => (float)(Read(_path)?[key]?.GetValue<double>() ?? fallback);

    private void Write(Action<JsonObject> change)
    {
        var file = Read(_path) ?? new JsonObject();
        change(file);
        AtomicFile.Write(_path, file.ToJsonString(), keepBackupAt: null, validate: null);
    }

    private static JsonObject? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
