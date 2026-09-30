namespace Rex.Mirror;

/// <summary>
/// What the side panel needs to know about the window that the session does not: whether the
/// picture has been frozen. scrcpy keeps no state anyone can ask for, so the window remembers what
/// was last asked of it, and forgets it with the picture.
/// </summary>
public partial class MainWindow
{
    /// <summary>True from a successful Pause until Resume, or until the mirror ends or starts again.</summary>
    public bool MirrorPaused { get; private set; }

    /// <summary>Remembers what an action did to the picture, whoever asked for it: the panel, the HUD, a key or the command line.</summary>
    internal void NoteAction(string id, bool ok)
    {
        if (!ok || id is not ("pause" or "resume"))
        {
            return;
        }

        SetPaused(id == "pause");
    }

    private void SetPaused(bool paused)
    {
        if (MirrorPaused == paused)
        {
            return;
        }

        MirrorPaused = paused;
        ControlsPanel.Refresh();
    }
}
