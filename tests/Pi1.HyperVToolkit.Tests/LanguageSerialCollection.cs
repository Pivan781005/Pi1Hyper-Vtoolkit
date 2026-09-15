namespace Pi1.HyperVToolkit.Tests;

// Serializes test classes that depend on the GLOBAL UI language
// (LocalizationService.Instance): same-collection tests never run in parallel
// with each other, so language switches cannot interleave with assertions on
// a specific language. All other classes are language-agnostic.
[CollectionDefinition("LanguageSerial")]
public sealed class LanguageSerialCollection
{
}
