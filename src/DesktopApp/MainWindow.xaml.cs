using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopApp.Core;
using DesktopApp.Storage;

namespace DesktopApp;

public partial class MainWindow : Window
{
    private const string AllCategories = "All categories";
    private const double GridSize = 20;

    private readonly CircuitStore _store = new(Path.Combine(App.DataDirectory, "last-circuit.json"));

    private Project _project = null!; // Set by StartProject in the constructor.
    private string _currentPage = "";
    private Device? _selected;

    public MainWindow()
    {
        InitializeComponent();

        SymbolList.ItemsSource = SymbolLibrary.All;
        CategoryFilter.ItemsSource = SymbolLibrary.All.Select(s => s.Category).Distinct().Prepend(AllCategories).ToList();
        CategoryFilter.SelectedIndex = 0;
        ManufacturerCombo.ItemsSource = new[] { "", "ABB", "Eaton", "Phoenix Contact", "Schneider Electric", "Siemens" };
        SelectToolButton.IsChecked = true;
        StartProject(new Project("Untitled project"), a4: false);

        if (_store.Load() is { } last)
        {
            VoltageInput.Text = last.Voltage.ToString(CultureInfo.CurrentCulture);
            ResistanceInput.Text = last.Resistance.ToString(CultureInfo.CurrentCulture);
        }
    }

    // ---- Project and pages ----

    private void StartProject(Project project, bool a4)
    {
        _project = project;
        DeviceGrid.ItemsSource = project.Devices;
        Title = $"{project.Name} - DesktopApp";
        ProjectNode.Header = project.Name;
        TitleBlockProject.Text = project.Name;
        TitleBlockCustomer.Text = project.Customer;
        TitleBlockStandard.Text = project.Standard;
        (DrawingCanvas.Width, DrawingCanvas.Height) = a4 ? (1188, 840) : (1680, 1188); // A4 / A3 landscape at 4 px per mm
        Select(null);
        RebuildPageTree(project.Pages[0]);
        Log($"Project '{project.Name}' created.");
    }

    private void RebuildPageTree(string pageToShow)
    {
        ProjectNode.Items.Clear();
        foreach (var page in _project.Pages)
            ProjectNode.Items.Add(new TreeViewItem { Header = page, Tag = page });
        ShowPage(pageToShow);
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        foreach (TreeViewItem item in ProjectNode.Items)
            item.IsSelected = (string)item.Tag == page;
        TitleBlockPage.Text = page;
        if (_selected is { } device && device.Page != page)
            Select(null); // also refreshes
        else
            Refresh();
    }

    private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string page } && page != _currentPage)
            ShowPage(page);
    }

    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        var page = _project.AddPage();
        RebuildPageTree(page);
        Log($"Added {page}.");
    }

    private void RemovePage_Click(object sender, RoutedEventArgs e)
    {
        if (_project.Pages.Count == 1)
        {
            Log("A project needs at least one page.");
            return;
        }

        var page = _currentPage;
        var count = _project.DevicesOn(page).Count();
        if (count > 0 && MessageBox.Show(this, $"Remove {page} and its {count} device(s)?", "Remove page",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        Select(null);
        RebuildPageTree(_project.RemovePage(page));
        Log($"Removed {page}.");
    }

    private void NewProject_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var count = _project.Devices.Count;
        if (count > 0 && MessageBox.Show(this, $"Discard the current project and its {count} device(s)?",
                "New project", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var dialog = new NewProjectDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        StartProject(new Project(dialog.ProjectNameInput.Text.Trim(), dialog.CustomerInput.Text.Trim(), dialog.StandardCombo.Text,
                dialog.CoverSheetCheckBox.IsChecked == true),
            a4: dialog.PageFormatA4Radio.IsChecked == true);
    }

    // ---- Symbols and devices ----

    private void SymbolFilter_Changed(object sender, RoutedEventArgs e)
    {
        var category = CategoryFilter.SelectedItem as string;
        var text = SymbolSearchInput.Text.Trim();
        SymbolList.Items.Filter = o => o is Symbol s
            && (category is null or AllCategories || s.Category == category)
            && s.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void SymbolList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InsertSymbolButton.IsEnabled = SymbolList.SelectedItem is not null;

    private void InsertSymbol_Click(object sender, RoutedEventArgs e)
    {
        if (SymbolList.SelectedItem is not Symbol symbol)
            return;

        var n = _project.DevicesOn(_currentPage).Count();
        PlaceDevice(symbol, 40 + n % 8 * 120, 40 + n / 8 * 80);
    }

    private void PlaceDevice(Symbol symbol, double x, double y)
    {
        var device = _project.Place(symbol, _currentPage, x, y);
        Log($"Placed {device.Designation} ({symbol.Name}) on {_currentPage}.");
        Select(device);
    }

    private void Select(Device? device)
    {
        if (device is not null && device.Page != _currentPage)
            ShowPage(device.Page);

        _selected = device;
        PropertiesFields.IsEnabled = DeleteMenuItem.IsEnabled = device is not null;
        DesignationInput.Text = device?.Designation ?? "";
        SymbolNameText.Text = device?.Symbol.Name ?? "";
        DescriptionInput.Text = device?.Description ?? "";
        ManufacturerCombo.SelectedItem = device?.Manufacturer ?? "";
        ArticleNumberInput.Text = device?.ArticleNumber ?? "";
        PropertiesError.Text = "";
        Refresh();
        DeviceGrid.SelectedItem = device;
    }

    private void DeviceGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is Device device && device != _selected)
            Select(device);
    }

    private void ApplyProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } device)
            return;

        if (_project.Update(device, DesignationInput.Text, DescriptionInput.Text,
                ManufacturerCombo.SelectedItem as string ?? "", ArticleNumberInput.Text) is { } error)
        {
            PropertiesError.Text = error;
            return;
        }

        Log($"Updated {device.Designation}.");
        Select(device);
    }

    private void DeleteDevice_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Preview, because a read-only DataGrid swallows Delete. Limited to the drawing and the grid
        // (no text boxes there), so Delete in the properties panel never removes a device.
        if (e.Key == Key.Delete && _selected is not null &&
            (DrawingScrollViewer.IsKeyboardFocusWithin || DeviceGrid.IsKeyboardFocusWithin))
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void DeleteSelected()
    {
        if (_selected is not { } device)
            return;

        _project.Delete(device);
        Log($"Deleted {device.Designation}.");
        Select(null);
    }

    /// <summary>Redraws the current page and the device list.</summary>
    private void Refresh()
    {
        foreach (var old in DrawingCanvas.Children.OfType<Button>().ToList())
            DrawingCanvas.Children.Remove(old);

        foreach (var device in _project.DevicesOn(_currentPage))
        {
            var button = new Button
            {
                Content = new TextBlock { Text = $"{device.Designation}\n{device.Symbol.Name}", TextAlignment = TextAlignment.Center },
                Width = 110,
                Height = 44,
                Background = Brushes.White,
                BorderBrush = device == _selected ? Brushes.Red : Brushes.Black,
                BorderThickness = new Thickness(device == _selected ? 2 : 1),
            };
            AutomationProperties.SetAutomationId(button, "Device" + device.Designation);
            AutomationProperties.SetName(button, device.Designation);
            button.Click += (_, _) => Select(device);
            Canvas.SetLeft(button, device.X);
            Canvas.SetTop(button, device.Y);
            DrawingCanvas.Children.Add(button);
        }

        DeviceGrid.Items.Refresh();
        DeviceCountText.Text = $"Devices: {_project.Devices.Count}";
    }

    // ---- Drawing area ----

    private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PlaceToolButton.IsChecked != true)
        {
            Select(null);
            return;
        }

        if (SymbolList.SelectedItem is not Symbol symbol)
        {
            Log("Select a symbol in the library first.");
            return;
        }

        var p = e.GetPosition(DrawingCanvas);
        PlaceDevice(symbol, Snap(p.X), Snap(p.Y));
    }

    private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(DrawingCanvas);
        CursorPositionText.Text = $"X: {Snap(p.X):0}  Y: {Snap(p.Y):0}";
    }

    private double Snap(double value) =>
        SnapToGridCheckBox.IsChecked == true ? Math.Round(value / GridSize) * GridSize : value;

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        ActiveToolText.Text = $"Tool: {((RadioButton)sender).Content}";
        if (sender == WireToolButton)
            NotAvailable("Wire drawing");
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value *= 1.25;
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value /= 1.25;
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = 1;

    // ---- Calculator ----

    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(VoltageInput.Text, CultureInfo.CurrentCulture, out var voltage) ||
            !double.TryParse(ResistanceInput.Text, CultureInfo.CurrentCulture, out var resistance) ||
            !double.IsFinite(voltage) || !double.IsFinite(resistance) || resistance <= 0) // TryParse accepts "NaN" and "∞"
        {
            ResultText.Text = "Enter a voltage and a resistance greater than zero.";
            return;
        }

        ResultText.Text = $"{OhmsLaw.Current(voltage, resistance):0.###} A";
        try
        {
            _store.Save(new Circuit(voltage, resistance));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not remember the values: {ex.Message}");
        }
    }

    // ---- Misc ----

    private void NotAvailable_Executed(object sender, ExecutedRoutedEventArgs e) => NotAvailable(((RoutedUICommand)e.Command).Text);
    private void NotAvailable_Click(object sender, RoutedEventArgs e) => NotAvailable((string)((FrameworkElement)sender).Tag);
    private void NotAvailable(string feature) => Log($"{feature} is not available in this version.");

    private void Log(string message)
    {
        MessagesList.Items.Add(message);
        StatusText.Text = message;
    }

    private void ClearMessages_Click(object sender, RoutedEventArgs e) => MessagesList.Items.Clear();

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, $"DesktopApp {Assembly.GetExecutingAssembly().GetName().Version}\nElectrical schematic editor (demo).",
            "About DesktopApp", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>A canvas that shows up in UI Automation (plain panels do not), so tests can find it and click on it.</summary>
public sealed class SchematicCanvas : Canvas
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
