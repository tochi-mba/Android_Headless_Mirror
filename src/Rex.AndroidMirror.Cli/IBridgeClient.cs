using System.Text.Json;

namespace Rex.AndroidMirror.Cli;

public interface IBridgeClient
{
    Task<JsonDocument> InvokeAsync(
        string action,
        string? serial = null,
        string? name = null,
        string? ns = null,
        string? key = null,
        string? value = null,
        CancellationToken cancellationToken = default);

    Task<RexStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RexDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Key, string Value, string Risk)>> ListAndroidSettingsAsync(
        string serial,
        string ns,
        CancellationToken cancellationToken = default);
}
