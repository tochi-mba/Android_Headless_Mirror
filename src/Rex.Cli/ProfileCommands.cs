using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex profile: list, show, apply, save, rename, delete, export and import profiles. While the
/// desktop app runs the command goes through it, so its window, its tray and an automatic profile
/// in effect all follow at once; without it the command works on the files themselves.
/// </summary>
public static class ProfileCommands
{
    /// <summary>The request the arguments make; file paths are made whole, since the app runs elsewhere.</summary>
    public static ProfileRequest Request(string[] args)
    {
        var positional = Arguments.Positional(args);
        string At(int index) => positional.Length > index ? positional[index] : string.Empty;
        var verb = positional.Length >= 2 ? positional[1].ToLowerInvariant() : "list";
        var groups = Arguments.Option(args, "--groups")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var all = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        var request = verb switch
        {
            "rename" => new ProfileRequest(verb, At(2), To: At(3)),
            "export" => new ProfileRequest(verb, At(2), File: Whole(At(3))),
            "import" => new ProfileRequest(verb, File: Whole(At(2))),
            _ => new ProfileRequest(verb, At(2), Groups: groups, All: all),
        };
        return request.Check();
    }

    private static string Whole(string path) => path.Length == 0 ? path : Path.GetFullPath(path);

    public static async Task<ProfileResult> RunAsync(string[] args, CliContext context)
    {
        var request = Request(args);
        var response = await context.Ipc.SendAsync(new IpcRequest("profile", request.ToFields())).ConfigureAwait(false);
        if (response is null)
        {
            return ProfileVerbs.Run(ProfileBook.ForFiles(context.Paths), request, DateTimeOffset.UtcNow);
        }

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error);
        }

        // A copy: the answer's node belongs to the answer, and machine mode wraps it in a reply of its own.
        return new ProfileResult(response.Data!["words"]!.GetValue<string>(), response.Data!["data"]!.DeepClone().AsObject());
    }

    public static async Task<int> HumanAsync(string[] args, CliContext context)
    {
        Console.WriteLine((await RunAsync(args, context).ConfigureAwait(false)).Words);
        return 0;
    }

    public static async Task<MachineResult> MachineAsync(string[] args, CliContext context) =>
        MachineMode.Success("profile", (await RunAsync(args, context).ConfigureAwait(false)).Data);
}
