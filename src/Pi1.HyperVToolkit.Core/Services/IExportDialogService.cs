namespace Pi1.HyperVToolkit.Core.Services;

// Native SaveFileDialog behind a testable seam (tests use a fake, never a
// modal dialog). Returns the chosen path, or null when the user cancels.
public interface IExportDialogService
{
    string? ShowSaveDialog(string defaultFileName, string filter, string defaultExtension);
}
