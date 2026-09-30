namespace NMSE.Core;

/// <summary>
/// Decides how the editor reacts when a loaded file changes on disk (another program,
/// cloud sync or the game itself writes to the save or account files).
/// </summary>
internal static class ExternalChangeDecision
{
    /// <summary>How the editor treats external changes to watched files.</summary>
    internal enum Mode
    {
        /// <summary>Always ask before reloading.</summary>
        Prompt,
        /// <summary>Reload silently; unsaved edits are discarded without asking.</summary>
        AutoReload,
        /// <summary>Do not react to external changes; the user reloads manually.</summary>
        Ignore
    }

    /// <summary>The action to take for a detected external change.</summary>
    internal enum Action
    {
        /// <summary>Nothing relevant changed, or watching is disabled.</summary>
        None,
        /// <summary>Apply the change without asking.</summary>
        Reload,
        /// <summary>Ask the user before applying the change.</summary>
        Prompt
    }

    /// <summary>
    /// Evaluates an external change for the given watching mode. Ignore never reacts,
    /// deletions always prompt (the in-memory copy cannot be reloaded) and AutoReload applies
    /// changes without asking.
    /// </summary>
    /// <param name="fileChanged">True when a watched file's timestamp changed on disk.</param>
    /// <param name="fileDeleted">True when a watched file no longer exists.</param>
    /// <param name="mode">The configured save-file watching mode.</param>
    /// <returns>The action to take.</returns>
    internal static Action Decide(bool fileChanged, bool fileDeleted, Mode mode)
    {
        if (mode == Mode.Ignore) return Action.None;
        if (!fileChanged && !fileDeleted) return Action.None;
        if (fileDeleted) return Action.Prompt;
        return mode == Mode.AutoReload ? Action.Reload : Action.Prompt;
    }
}
