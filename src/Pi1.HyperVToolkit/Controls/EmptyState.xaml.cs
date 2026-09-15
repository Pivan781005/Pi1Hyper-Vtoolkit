using System.Windows;
using System.Windows.Controls;

namespace Pi1.HyperVToolkit.Controls;

/// <summary>Empty-state message. Replaces the PowerShell Show-NoResults output.</summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public EmptyState()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
