namespace Rex.Core;

/// <summary>
/// Sending files to the phone: where they go, how apps are installed, when to ask first, and how
/// a transfer is shown. Files go only into the phone's shared storage, never elsewhere.
/// </summary>
public sealed record TransferSettings
{
    public const string DefaultFolder = "/sdcard/Download/";
    public static readonly IReadOnlyList<string> Roots = ["/sdcard/", "/storage/emulated/0/"];
    public static readonly IReadOnlyList<string> FolderChoices = ["send", "refuse"];
    public static readonly IReadOnlyList<string> NameChoices = ["ask", "rename", "replace", "skip"];
    public static readonly IReadOnlyList<int> SizeChoices = [0, 100, 500, 1024, 4096, 10240];
    public const int LongestFolder = 200;
    public const int MostAtOnce = 4;
    public const int MostHistory = 100;
    public const int LargestAskMb = 102400;

    /// <summary>The desktop window accepts files from drops, its controls and File Explorer.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The phone folder files go to, ending with a slash.</summary>
    public string Folder { get; set; } = DefaultFolder;

    /// <summary>Photos, videos and music go to Pictures, Movies and Music instead.</summary>
    public bool SortMedia { get; set; }

    /// <summary>send, or refuse a dropped folder.</summary>
    public string Folders { get; set; } = "send";

    /// <summary>An .apk is installed rather than copied.</summary>
    public bool InstallApks { get; set; } = true;

    /// <summary>An app already installed is updated (-r).</summary>
    public bool Replace { get; set; } = true;

    /// <summary>An older version may replace a newer one (-d).</summary>
    public bool AllowDowngrade { get; set; }

    /// <summary>Every permission the app asks for is granted (-g).</summary>
    public bool GrantPermissions { get; set; }

    /// <summary>Test builds may be installed (-t).</summary>
    public bool AllowTestApps { get; set; }

    public bool ConfirmInstall { get; set; } = true;

    public bool ConfirmDrops { get; set; }

    /// <summary>Ask before sending more than this many megabytes at once; 0 never asks.</summary>
    public int ConfirmOverMb { get; set; } = 1024;

    /// <summary>ask, rename (keep both), replace or skip when the phone has a file with that name.</summary>
    public string WhenNameExists { get; set; } = "rename";

    /// <summary>New photos and videos show in the Gallery at once.</summary>
    public bool ScanMedia { get; set; } = true;

    public bool OpenAfterInstall { get; set; }

    public bool ShowFolderAfter { get; set; }

    /// <summary>How many files are sent at the same time (1 to 4).</summary>
    public int AtOnce { get; set; } = 1;

    public bool TaskbarProgress { get; set; } = true;

    public bool NotifyWhenHidden { get; set; } = true;

    /// <summary>What is waiting is cancelled when the phone disconnects, instead of waiting for it.</summary>
    public bool CancelWhenPhoneLeaves { get; set; } = true;

    /// <summary>How many finished transfers the list keeps (0 to 100).</summary>
    public int History { get; set; } = 20;

    /// <summary>File Explorer has Send to › Android phone.</summary>
    public bool SendToMenu { get; set; }

    public TransferSettings Copy() => this with { };

    public void Normalize()
    {
        Folder = IsValidFolder(Folder) ? WithSlash(Folder.Trim()) : DefaultFolder;
        Folders = MirrorSettings.OneOf(FolderChoices, Folders, "send");
        WhenNameExists = MirrorSettings.OneOf(NameChoices, WhenNameExists, "rename");
        ConfirmOverMb = ConfirmOverMb <= 0 ? 0 : Math.Clamp(ConfirmOverMb, 10, LargestAskMb);
        AtOnce = Math.Clamp(AtOnce, 1, MostAtOnce);
        History = Math.Clamp(History, 0, MostHistory);
    }

    /// <summary>A folder in the phone's shared storage: under one of <see cref="Roots"/>, no "..", no quotes or control characters.</summary>
    public static bool IsValidFolder(string? folder)
    {
        var text = folder?.Trim();
        return text is { Length: > 0 and <= LongestFolder } &&
            Roots.Any(r => WithSlash(text).StartsWith(r, StringComparison.Ordinal)) &&
            !text.Split('/').Contains("..") &&
            !text.Any(c => char.IsControl(c) || c is '"' or '\'' or '`' or '\\' or '$');
    }

    /// <summary>Why a folder is refused, in words, or null when it is fine.</summary>
    public static string? WhyNotFolder(string? folder) =>
        IsValidFolder(folder) ? null : "Choose a folder in the phone's storage, such as /sdcard/Download/, without quotes or \"..\".";

    /// <summary>The shared storage root a folder is under.</summary>
    public static string RootOf(string folder) => Roots.FirstOrDefault(r => WithSlash(folder).StartsWith(r, StringComparison.Ordinal)) ?? Roots[0];

    private static string WithSlash(string folder) => folder.EndsWith('/') ? folder : folder + "/";
}
