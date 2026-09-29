namespace NMSE.Core;

/// <summary>
/// Decides how the editor reacts when a loaded file changes on disk (another program,
/// cloud sync or the game itself writes to the save or account files).
/// </summary>
internal static class ExternalChangeDecision
{
    /// <summary>The action to take for a detected external change.</summary>
    internal enum Action
    {
        /// <summary>Nothing relevant changed.</summary>
        None,
        /// <summary>The change can be applied without losing unsaved edits.</summary>
        Reload,
        /// <summary>Applying the change would discard unsaved edits; confirm first.</summary>
        Prompt
    }

    /// <summary>
    /// Evaluates an external change. <see cref="Action.Reload"/> means the change is safe to
    /// apply without data loss; <see cref="Action.Prompt"/> means unsaved edits would be lost.
    /// Deletions always prompt because the in-memory copy cannot be reloaded. The editor
    /// confirms both cases with the user.
    /// </summary>
    /// <param name="fileChanged">True when a watched file's timestamp changed on disk.</param>
    /// <param name="fileDeleted">True when a watched file no longer exists.</param>
    /// <param name="hasUnsavedChanges">True when the in-memory data differs from the saved baseline.</param>
    /// <returns>The action to take.</returns>
    internal static Action Decide(bool fileChanged, bool fileDeleted, bool hasUnsavedChanges)
    {
        if (!fileChanged && !fileDeleted)
            return Action.None;
        if (fileDeleted)
            return Action.Prompt;
        return hasUnsavedChanges ? Action.Prompt : Action.Reload;
    }
}
