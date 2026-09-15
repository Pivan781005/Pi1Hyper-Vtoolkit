using System.Runtime.InteropServices;

namespace Pi1.HyperVToolkit.Clipboard;

// Injectable clipboard boundary so grid copy behavior stays deterministic in
// tests and never touches static Clipboard calls directly.
public interface IClipboardService
{
    void SetText(string text);
}

// Production implementation with a small bounded retry: the Windows clipboard
// can be briefly locked by another process. Non-recoverable or repeated
// failures throw to the caller, which reports without crashing.
public sealed class WindowsClipboardService : IClipboardService
{
    private const int MaxAttempts = 3;

    public void SetText(string text)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text ?? string.Empty);
                return;
            }
            catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
            {
                last = ex;
                Thread.Sleep(50);
            }
        }

        throw last ?? new InvalidOperationException("Clipboard unavailable.");
    }
}
