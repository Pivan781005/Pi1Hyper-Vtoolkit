using System.Windows;
using System.Windows.Controls;
using Pi1.HyperVToolkit.Core.Models;

namespace Pi1.HyperVToolkit.Controls;

/// <summary>Inline warning/error banner. Replaces the yellow/red console messages of Invoke-PiSafe.</summary>
public partial class WarningBanner : UserControl
{
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(Severity), typeof(WarningBanner), new PropertyMetadata(Severity.Info));

    public static readonly DependencyProperty BannerTextProperty =
        DependencyProperty.Register(nameof(BannerText), typeof(string), typeof(WarningBanner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(WarningBanner), new PropertyMetadata(string.Empty));

    public WarningBanner()
    {
        InitializeComponent();
    }

    public Severity Severity
    {
        get => (Severity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string BannerText
    {
        get => (string)GetValue(BannerTextProperty);
        set => SetValue(BannerTextProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }
}
