using System.Windows;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit;

/// <summary>Shell window. The DataContext is supplied by DI; no business logic lives here.</summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    // Never open larger than the available work area (multi-monitor safe).
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        if (Width > area.Width)
        {
            Width = area.Width;
        }

        if (Height > area.Height)
        {
            Height = area.Height;
        }

        if (Left + Width > area.Right)
        {
            Left = area.Right - Width;
        }

        if (Top + Height > area.Bottom)
        {
            Top = area.Bottom - Height;
        }
    }
}
