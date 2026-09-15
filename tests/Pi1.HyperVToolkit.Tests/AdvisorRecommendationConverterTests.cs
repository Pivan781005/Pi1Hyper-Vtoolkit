using Pi1.HyperVToolkit.Converters;
using Pi1.HyperVToolkit.Core.Localization;
using Pi1.HyperVToolkit.Core.Models;
using Pi1.HyperVToolkit.Core.State;

namespace Pi1.HyperVToolkit.Tests;

[Collection("LanguageSerial")]
public sealed class AdvisorRecommendationConverterTests
{
    private static string Convert(AdvisorRule rule, bool sensitive)
    {
        var converter = new AdvisorRecommendationConverter();
        return (string)converter.Convert(
            [rule, sensitive], typeof(string), null!,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Converter_Slovak_Texts()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Slovak);
            Assert.Equal(
                "Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie.",
                Convert(AdvisorRule.Pressure, false));
            Assert.Equal(
                "Demand je vyšší ako Assigned. Sledovať RAM / zvážiť navýšenie.",
                Convert(AdvisorRule.Pressure, true));
            Assert.Equal(
                "Veľká RAM rezerva. Kandidát na zníženie po sledovaní.",
                Convert(AdvisorRule.LargeReserve, false));
            Assert.Equal(
                "Špecifický workload. Neznižovať bez dlhšieho sledovania.",
                Convert(AdvisorRule.LargeReserve, true));
            Assert.Equal("RAM rezerva > 8 GB. Sledovať.", Convert(AdvisorRule.Reserve, false));
            Assert.Equal(
                "Špecifický workload. Neznižovať bez dlhšieho sledovania.",
                Convert(AdvisorRule.Reserve, true));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void Converter_English_Texts()
    {
        var previous = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            Assert.Equal(
                "Demand is higher than Assigned. Monitor RAM / consider increasing.",
                Convert(AdvisorRule.Pressure, false));
            Assert.Equal(
                "Large RAM reserve. Candidate for reduction after monitoring.",
                Convert(AdvisorRule.LargeReserve, false));
            Assert.Equal(
                "Specific workload. Do not reduce without extended monitoring.",
                Convert(AdvisorRule.Reserve, true));
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }
}
