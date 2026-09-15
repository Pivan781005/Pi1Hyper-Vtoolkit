using System.Windows.Data;
using System.Windows.Markup;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Localization;

// XAML localization hook: {loc:Loc Key=Nav_Dashboard} binds to the global
// language service indexer and refreshes live on language change.
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string? Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
