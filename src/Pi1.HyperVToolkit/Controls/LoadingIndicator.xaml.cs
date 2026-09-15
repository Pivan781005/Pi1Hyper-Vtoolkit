using System.Windows;
using System.Windows.Controls;

namespace Pi1.HyperVToolkit.Controls;

/// <summary>Progress state for long-running (remote) loads. Shown while a view model is busy.</summary>
public partial class LoadingIndicator : UserControl
{
    public static readonly DependencyProperty LoadingTextProperty =
        DependencyProperty.Register(nameof(LoadingText), typeof(string), typeof(LoadingIndicator), new PropertyMetadata("Načítavam…"));

    public LoadingIndicator()
    {
        InitializeComponent();
    }

    public string LoadingText
    {
        get => (string)GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }
}
