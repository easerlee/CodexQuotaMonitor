namespace CodexQuotaMonitor.Wpf;

// The panel cannot lean on WPF's own dismissal: the overlay is a WS_EX_NOACTIVATE topmost
// window that never activates, so a click in another application does not arrive as a WPF
// mouse event and the popup's capture-based "click outside to close" never fires. The window
// therefore polls the mouse and closes the panel on a fresh press outside it.
// The rule itself lives here so it can be tested without a mouse.
public static class MenuDismiss
{
    // pressed   - a mouse button is down right now
    // wasUp     - no button was down at the previous look, i.e. this press just started
    // inside    - the cursor is within the panel's own rectangle
    // A press that started inside belongs to the panel, and a press that was already down when
    // the panel opened must not close it, so both of those stay open.
    public static bool ShouldClose(bool pressed, bool wasUp, bool inside)
    {
        return pressed && wasUp && !inside;
    }
}
