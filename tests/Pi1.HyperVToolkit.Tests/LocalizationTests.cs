using Pi1.HyperVToolkit.Converters;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Tests;

[Collection("LanguageSerial")]
public sealed class LocalizationTests
{
    [Fact]
    public void Dictionaries_ShareIdenticalKeys()
    {
        var sk = UiStrings.Slovak.Keys.ToHashSet(StringComparer.Ordinal);
        var en = UiStrings.English.Keys.ToHashSet(StringComparer.Ordinal);
        Assert.Subset(sk, en);
        Assert.Subset(en, sk);
    }

    [Fact]
    public void Dictionaries_HaveNoEmptyValues()
    {
        Assert.All(UiStrings.Slovak, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
        Assert.All(UiStrings.English, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
    }

    [Fact]
    public void UnknownKey_FallsBackToKey()
    {
        Assert.Equal("NoSuchKey", UiStrings.Get(AppLanguage.Slovak, "NoSuchKey"));
    }

    [Fact]
    public void Service_SwitchesAndNotifies()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var events = 0;
            LocalizationService.Instance.LanguageChanged += OnChanged;
            try
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.English);
                Assert.Equal(AppLanguage.English, LocalizationService.Instance.CurrentLanguage);
                Assert.Equal("Virtual Machines", LocalizationService.Instance["Nav_VMs"]);
                Assert.Equal(1, events);
                LocalizationService.Instance.SetLanguage(AppLanguage.English);
                Assert.Equal(1, events); // no change => no event
            }
            finally
            {
                LocalizationService.Instance.LanguageChanged -= OnChanged;
            }

            void OnChanged(object? sender, EventArgs e) => events++;
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void StatusConverter_LocalizesWithoutMutatingModel()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            var converter = new StatusValueConverter();
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.Equal("Beží", converter.Convert("Running", typeof(string), null!, invariant));
            Assert.Equal("Áno", converter.Convert(true, typeof(string), null!, invariant));
            Assert.Equal("Interný", converter.Convert("Internal", typeof(string), null!, invariant));
            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            Assert.Equal("Running", converter.Convert("Running", typeof(string), null!, invariant));
            Assert.Equal("Yes", converter.Convert(true, typeof(string), null!, invariant));
            // Unknown canonical values pass through unchanged.
            Assert.Equal("Auto-Select", converter.Convert("Auto-Select", typeof(string), null!, invariant));
            Assert.Equal(string.Empty, converter.Convert(null!, typeof(string), null!, invariant));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void BoolConverter_NullIsEmpty()
    {
        var converter = new BoolYesNoConverter();
        Assert.Equal(string.Empty, converter.Convert(null!, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));
    }
}
