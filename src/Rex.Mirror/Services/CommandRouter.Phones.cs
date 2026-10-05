using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>Two phones over the pipe: what is shown where, and showing, using, swapping or stopping the phone beside.</summary>
public static partial class CommandRouter
{
    /// <summary>
    /// <c>verb</c> beside (with <c>serial</c>, or the first other ready phone), stop, switch,
    /// make-main, or nothing for what is shown now.
    /// </summary>
    private static async Task<IpcResponse> PhonesAsync(AppHost host, IpcRequest request)
    {
        if (host.Window is not { } window)
        {
            return IpcResponse.Fail("The window is not available.");
        }

        var result = request.Arg("verb") switch
        {
            "" => AndroidResult.Success(window.SummaryOfPhones()),
            "beside" when request.Arg("serial") is { Length: > 0 } serial =>
                host.Session.Devices.FirstOrDefault(d => d.Serial == serial && d.IsReady) is { } device
                    ? await window.ShowBesideAsync(device).ConfigureAwait(true)
                    : AndroidResult.Failure($"{serial} is not a ready phone."),
            "beside" => window.OtherDevice is null ? await window.ToggleBesideAsync().ConfigureAwait(true) : AndroidResult.Success(window.SummaryOfPhones()),
            "stop" => Stop(window),
            "switch" => window.SwitchPhone(),
            "make-main" => window.MakeOtherTheMain(),
            var verb => AndroidResult.Failure($"Phones takes beside, stop, switch or make-main, not '{verb}'."),
        };
        return result.Ok ? IpcResponse.Success(PhonesStatus(host, window, result.Text)) : IpcResponse.Fail(result.Text);

        static AndroidResult Stop(MainWindow window)
        {
            window.StopOther(byPerson: true);
            return AndroidResult.Success(window.SummaryOfPhones());
        }
    }

    /// <summary>Every connected phone, which is main and which beside, which is in use, and the phone beside's state.</summary>
    private static JsonObject PhonesStatus(AppHost host, MainWindow window, string? words = null)
    {
        var main = host.Session.ActiveDevice?.Serial;
        var beside = window.OtherDevice?.Serial;
        var target = window.TargetPhone?.Serial;
        return new JsonObject
        {
            ["words"] = words ?? window.SummaryOfPhones(),
            ["activeSerial"] = target,
            ["beside"] = window.OtherDevice is { } other ? new JsonObject
            {
                ["serial"] = other.Serial,
                ["name"] = window.NameOf(other.Serial),
                ["state"] = window.OtherState.ToString().ToLowerInvariant(),
                ["processId"] = window.OtherProcessId,
                ["active"] = window.OtherActive,
                ["battery"] = window.OtherBattery?.Level,
                ["why"] = window.OtherWhy,
            } : null,
            ["list"] = new JsonArray([.. host.Session.Devices.Select(d => (JsonNode)new JsonObject
            {
                ["serial"] = d.Serial,
                ["name"] = window.NameOf(d.Serial),
                ["state"] = d.State,
                ["transport"] = d.Transport,
                ["role"] = d.Serial == main ? "main" : d.Serial == beside ? "beside" : "connected",
                ["active"] = d.Serial == target,
            })]),
        };
    }
}
