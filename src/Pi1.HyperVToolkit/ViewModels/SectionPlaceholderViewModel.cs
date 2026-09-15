using CommunityToolkit.Mvvm.ComponentModel;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.ViewModels;

/// <summary>
/// Placeholder for a section whose PowerShell functionality has not been
/// migrated yet. Shows no data — real view models replace these in later phases.
/// Texts refresh from localization keys so language switches apply live.
/// </summary>
public sealed partial class SectionPlaceholderViewModel : ViewModelBase
{
    public SectionPlaceholderViewModel(string titleKey, string noteKey)
    {
        TitleKey = titleKey;
        NoteKey = noteKey;
        RefreshTexts();
    }

    public string TitleKey { get; }

    public string NoteKey { get; }

    [ObservableProperty]
    private string migrationNote = string.Empty;

    public void RefreshTexts()
    {
        var loc = LocalizationService.Instance;
        Title = loc[TitleKey];
        MigrationNote = loc[NoteKey];
    }
}
