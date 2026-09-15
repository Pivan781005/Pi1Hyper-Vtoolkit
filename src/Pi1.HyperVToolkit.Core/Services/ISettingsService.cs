using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Core.Services;

/// <summary>JSON settings persistence (System.Text.Json). Missing/corrupt files yield defaults.</summary>
public interface ISettingsService
{
    AppSettings Load();
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
