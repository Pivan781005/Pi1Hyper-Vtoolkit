using Pi1.HyperVToolkit.Core.Export;
using Pi1.HyperVToolkit.Core.Services;

namespace Pi1.HyperVToolkit.Dialogs;

// Native WPF SaveFileDialog. Default folder is the Desktop (legacy parity);
// the file name already carries {SanitizedReport}_{yyyyMMdd_HHmmss}.{ext}.
public sealed class WindowsSaveFileDialogService : IExportDialogService
{
    public string? ShowSaveDialog(string defaultFileName, string filter, string defaultExtension)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = filter,
            DefaultExt = defaultExtension,
            InitialDirectory = ExportFileName.DefaultFolder,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
