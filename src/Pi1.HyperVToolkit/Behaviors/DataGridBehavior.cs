using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Pi1.HyperVToolkit.Clipboard;
using Pi1.HyperVToolkit.Core.State;
using Pi1.HyperVToolkit.ViewModels;

namespace Pi1.HyperVToolkit.Behaviors;

// Central DataGrid behavior, enabled for every grid through the shared
// DataGrid style (no per-grid code, no copy/paste handlers):
//  1. Centering: DataGridTextColumns without an explicit ElementStyle get the
//     shared CenteredText style. (Centering DataGridCell content alone does
//     NOT center text: the generated TextBlock stretches across the cell and
//     keeps left-aligned text. TextAlignment=Center on the TextBlock itself
//     is the correct fix; explicit LeftAlignedText columns are preserved.)
//  2. Right-click: the clicked cell/row becomes the selection first, so the
//     context menu never operates on a stale selection. Works for both
//     FullRow and Cell selection units.
//  3. Context menu: Copy value (exact displayed text of the clicked cell) and
//     Copy row (visible columns in DisplayIndex order, tab-separated).
//  4. Ctrl+C copies the current cell value through the same infrastructure.
public static class DataGridBehavior
{
    public static IClipboardService Clipboard { get; set; } = new WindowsClipboardService();

    public static readonly DependencyProperty EnableSharedBehaviorProperty =
        DependencyProperty.RegisterAttached(
            "EnableSharedBehavior",
            typeof(bool),
            typeof(DataGridBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnableSharedBehavior(DependencyObject target) =>
        (bool)target.GetValue(EnableSharedBehaviorProperty);

    public static void SetEnableSharedBehavior(DependencyObject target, bool value) =>
        target.SetValue(EnableSharedBehaviorProperty, value);

    private static readonly DependencyProperty AttachedProperty =
        DependencyProperty.RegisterAttached(
            "Attached",
            typeof(bool),
            typeof(DataGridBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty ClickedCellProperty =
        DependencyProperty.RegisterAttached(
            "ClickedCell",
            typeof(DataGridCell),
            typeof(DataGridBehavior),
            new PropertyMetadata(null));

    private static void OnEnableChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not DataGrid grid || e.NewValue is not true)
        {
            return;
        }

        if (grid.IsLoaded)
        {
            Attach(grid);
        }
        else
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) =>
            {
                grid.Loaded -= loaded;
                Attach(grid);
            };
            grid.Loaded += loaded;
        }
    }

    private static void Attach(DataGrid grid)
    {
        if (grid.GetValue(AttachedProperty) is true)
        {
            return;
        }

        grid.SetValue(AttachedProperty, true);
        ApplyElementStyles(grid);
        grid.Columns.CollectionChanged += (_, _) => ApplyElementStyles(grid);
        grid.PreviewMouseRightButtonDown += OnPreviewRightButtonDown;
        grid.PreviewKeyDown += OnPreviewKeyDown;

        if (grid.ContextMenu is null)
        {
            var menu = new ContextMenu();
            var copyValue = new MenuItem();
            copyValue.SetBinding(MenuItem.HeaderProperty, LocBinding("Ctx_CopyValue"));
            copyValue.Click += (_, _) => CopyValue(grid);
            var copyRow = new MenuItem();
            copyRow.SetBinding(MenuItem.HeaderProperty, LocBinding("Ctx_CopyRow"));
            copyRow.Click += (_, _) => CopyRow(grid);
            menu.Items.Add(copyValue);
            menu.Items.Add(copyRow);
            menu.Opened += (_, _) =>
            {
                var hasTarget = GetClickedCell(grid) is not null || HasCurrentCell(grid);
                copyValue.IsEnabled = hasTarget;
                copyRow.IsEnabled = hasTarget;
            };
            menu.Closed += (_, _) => SetClickedCell(grid, null);
            grid.ContextMenu = menu;
        }
    }

    private static Binding LocBinding(string key) =>
        new($"[{key}]") { Source = LocalizationService.Instance, Mode = BindingMode.OneWay };

    // Columns always carry EITHER an explicit XAML ElementStyle OR the WPF
    // framework default (a Margin 2,0,2,0 TextBlock style) — ElementStyle is
    // therefore never null in practice, so a null-coalescing check would
    // never fire. The explicit LeftAlignedText opt-out is preserved by
    // reference; everything else gets the shared centered style.
    internal static void ApplyElementStyles(DataGrid grid)
    {
        if (grid.TryFindResource("CenteredText") is not Style centered)
        {
            return;
        }

        var left = grid.TryFindResource("LeftAlignedText") as Style;
        foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
        {
            if (!ReferenceEquals(column.ElementStyle, left))
            {
                column.ElementStyle = centered;
            }
        }
    }

    // Right-click selects the clicked cell/row FIRST (tunneling preview runs
    // before the context menu opens), for both selection units. Public so the
    // selection contract is directly testable.
    public static void HandleCellRightClick(DataGrid grid, DependencyObject? source)
    {
        var cell = FindAncestor<DataGridCell>(source);
        if (cell is null)
        {
            return;
        }

        var row = DataGridRow.GetRowContainingElement(cell);
        var item = row?.Item ?? cell.DataContext;
        if (item is null || item == CollectionView.NewItemPlaceholder)
        {
            return;
        }

        try
        {
            if (grid.SelectionUnit != DataGridSelectionUnit.FullRow)
            {
                grid.SelectedCells.Clear();
                grid.SelectedCells.Add(new DataGridCellInfo(cell));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DataGrid cell selection best-effort failed: {ex.Message}");
        }

        try
        {
            grid.SelectedItem = item;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DataGrid row selection best-effort failed: {ex.Message}");
        }

        try
        {
            cell.Focus();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DataGrid cell focus best-effort failed: {ex.Message}");
        }

        SetClickedCell(grid, cell);
    }

    private static void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            HandleCellRightClick(grid, e.OriginalSource as DependencyObject);
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CopyValue(grid);
            e.Handled = true;
        }
    }

    // Exact displayed text of one cell. TextBlocks (all text/size/status
    // columns) contribute their rendered text with no added whitespace;
    // checkbox columns contribute localized Yes/No; anything else is empty.
    public static string GetCellValueText(DataGridCell? cell) =>
        ContentText(cell?.Content);

    public static string ContentText(object? content)
    {
        if (content is TextBlock text)
        {
            return text.Text ?? string.Empty;
        }

        if (content is CheckBox checkBox)
        {
            return checkBox.IsChecked switch
            {
                true => LocalizationService.Instance["V_Yes"],
                false => LocalizationService.Instance["V_No"],
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    // Visible columns in current DisplayIndex order, hidden columns excluded.
    // Realized cells contribute displayed text; unrealized ones fall back to
    // the simple bound property value (never a crash, never a guess).
    public static IReadOnlyList<string> GetRowValueTexts(DataGrid grid, object? item)
    {
        var cells = new List<string>();
        if (item is null)
        {
            return cells;
        }

        foreach (var column in grid.Columns
                     .Where(c => c.Visibility == Visibility.Visible)
                     .OrderBy(c => c.DisplayIndex))
        {
            string text = string.Empty;
            try
            {
                var element = column.GetCellContent(item) as FrameworkElement;
                if (element is not null)
                {
                    text = ContentText(element);
                }
                else
                {
                    text = BoundPropertyText(column, item);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DataGrid row text fallback failed: {ex.Message}");
            }

            cells.Add(text);
        }

        return cells;
    }

    public static string BuildRowText(IEnumerable<string?> cells) =>
        string.Join("\t", cells.Select(c => c ?? string.Empty));

    internal static string BoundPropertyText(DataGridColumn column, object item)
    {
        if (column is DataGridBoundColumn bound &&
            bound.Binding is Binding binding &&
            !string.IsNullOrEmpty(binding.Path?.Path) &&
            !binding.Path.Path.Contains('.'))
        {
            return item.GetType().GetProperty(binding.Path.Path)?.GetValue(item)?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    public static void CopyValue(DataGrid grid)
    {
        var cell = GetClickedCell(grid) ?? CellFromCurrent(grid);
        if (cell is null && !HasCurrentCell(grid))
        {
            return;
        }

        var text = cell is not null
            ? GetCellValueText(cell)
            : ContentText(grid.CurrentCell.Column?.GetCellContent(grid.CurrentCell.Item));
        CopyToClipboard(grid, text);
    }

    public static void CopyRow(DataGrid grid)
    {
        var cell = GetClickedCell(grid);
        // Prefer the clicked/current cell's row (correct under both FullRow
        // and Cell selection units), fall back to the selected item.
        var item = (cell is not null ? DataGridRow.GetRowContainingElement(cell)?.Item : null)
            ?? (grid.CurrentCell.Item is not null && grid.CurrentCell.Column is not null
                ? grid.CurrentCell.Item
                : null)
            ?? grid.SelectedItem;
        if (item is null)
        {
            return;
        }

        CopyToClipboard(grid, BuildRowText(GetRowValueTexts(grid, item)));
    }

    private static void CopyToClipboard(DataGrid grid, string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Clipboard copy failed: {ex}");
            if (grid.DataContext is ViewModelBase viewModel)
            {
                viewModel.StatusText = LocalizationService.Instance["Ctx_ClipboardFailed"];
            }
        }
    }

    private static bool HasCurrentCell(DataGrid grid) =>
        grid.CurrentCell.Column is not null && grid.CurrentCell.Item is not null;

    private static DataGridCell? CellFromCurrent(DataGrid grid)
    {
        try
        {
            var info = grid.CurrentCell;
            if (info.Column is null || info.Item is null)
            {
                return null;
            }

            return info.Column.GetCellContent(info.Item) is FrameworkElement element
                ? FindAncestor<DataGridCell>(element)
                : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Current cell lookup failed: {ex.Message}");
            return null;
        }
    }

    private static DataGridCell? GetClickedCell(DataGrid grid) =>
        grid.GetValue(ClickedCellProperty) as DataGridCell;

    private static void SetClickedCell(DataGrid grid, DataGridCell? cell) =>
        grid.SetValue(ClickedCellProperty, cell);

    internal static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    internal static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
            {
                return match;
            }

            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return null;
    }
}
