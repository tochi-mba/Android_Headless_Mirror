namespace Rex.Core;

/// <summary>Where recording a shortcut has got to.</summary>
public enum ChordState
{
    /// <summary>Nothing but modifiers pressed yet.</summary>
    Waiting,

    /// <summary>A key is down with its modifiers: the chord shows, and is kept when the key comes up.</summary>
    Holding,

    /// <summary>The key came up: the chord is chosen.</summary>
    Done,

    /// <summary>Backspace or Delete: the shortcut is turned off.</summary>
    Cleared,

    /// <summary>Esc: what was there before is put back.</summary>
    Cancelled,
}

/// <summary>
/// What a shortcut box does with the keys pressed into it: it shows the chord while it is held and
/// keeps it when the key comes up. Backspace or Delete on their own turn the shortcut off, Esc puts
/// back what was there, and Tab on its own is left to move focus on.
/// </summary>
public sealed class ChordRecorder
{
    public ChordState State { get; private set; } = ChordState.Waiting;

    /// <summary>The chord pressed, once a key has been.</summary>
    public KeyChord? Chord { get; private set; }

    /// <summary>The modifiers down while waiting for a key, so the box can show them.</summary>
    public KeyMods Held { get; private set; }

    /// <summary>Begins again, as when the box takes focus.</summary>
    public void Start()
    {
        State = ChordState.Waiting;
        Chord = null;
        Held = KeyMods.None;
    }

    /// <summary>A key went down with <paramref name="mods"/> held. False when the key is not the box's to take.</summary>
    public bool Press(int key, KeyMods mods)
    {
        if (KeyChord.IsModifier(key))
        {
            if (State == ChordState.Waiting)
            {
                Held = mods;
            }

            return true;
        }

        if (mods == KeyMods.None)
        {
            switch (key)
            {
                case KeyChord.Tab:
                    return false;
                case KeyChord.Backspace or KeyChord.Delete:
                    State = ChordState.Cleared;
                    Chord = null;
                    return true;
                case KeyChord.Escape:
                    State = ChordState.Cancelled;
                    return true;
            }
        }

        if (KeyChord.IsNamed(key))
        {
            Chord = new KeyChord(mods, key);
            State = ChordState.Holding;
        }

        return true;
    }

    /// <summary>A key came up; <paramref name="mods"/> are the modifiers still held.</summary>
    public void Release(int key, KeyMods mods)
    {
        if (State == ChordState.Holding && Chord?.Key == key)
        {
            State = ChordState.Done;
        }
        else if (State == ChordState.Waiting)
        {
            Held = mods;
        }
    }
}
