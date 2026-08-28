using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using CodexUsageWidget.App.ViewModels;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.App;

/// <summary>
/// Borderless, topmost, draggable widget window. Supports hide-without-exit and
/// restores its placement via <see cref="IWindowPlacementService"/>.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Placement service used to save/restore window position and size.
    /// </summary>
    public IWindowPlacementService? PlacementService { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the window is closing because the
    /// application is shutting down. When false, the close button hides the widget.
    /// </summary>
    public bool IsShuttingDown { get; set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (PlacementService is null)
        {
            return;
        }

        try
        {
            WindowPlacement? placement = await PlacementService.LoadPlacementAsync().ConfigureAwait(true);
            if (placement is not null)
            {
                double requiredContentHeight = Math.Max(MinHeight, ActualHeight);
                SizeToContent = SizeToContent.Manual;

                WindowPlacementScreen workArea = new(
                    SystemParameters.WorkArea.Left,
                    SystemParameters.WorkArea.Top,
                    SystemParameters.WorkArea.Width,
                    SystemParameters.WorkArea.Height);

                WindowPlacementScreen[] screens = Screen.AllScreens
                    .Select(s => new WindowPlacementScreen(s.Bounds.Left, s.Bounds.Top, s.Bounds.Width, s.Bounds.Height))
                    .ToArray();

                WindowPlacement contentSafePlacement = placement with
                {
                    Height = Math.Max(placement.Height, requiredContentHeight),
                };
                WindowPlacement clamped = PlacementService.ClampPlacement(contentSafePlacement, screens, workArea);

                Left = clamped.Left;
                Top = clamped.Top;
                Width = clamped.Width;
                MinHeight = Math.Min(requiredContentHeight, clamped.Height);
                Height = clamped.Height;
            }
        }
        catch
        {
            // Placement load failures are non-fatal; the window opens at its default location.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>Saves the current window placement.</summary>
    public async Task SavePlacementAsync()
    {
        if (PlacementService is null)
        {
            return;
        }

        try
        {
            double width = double.IsNaN(Width) ? ActualWidth : Width;
            double height = double.IsNaN(Height) ? ActualHeight : Height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            WindowPlacement placement = new(Left, Top, width, height);
            await PlacementService.SavePlacementAsync(placement).ConfigureAwait(false);
        }
        catch
        {
            // Placement save failures are non-fatal.
        }
    }

    private void OnBorderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowHideCommand.Execute(null);
        }
        else
        {
            Hide();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowHideCommand.Execute(null);
        }
        else
        {
            Hide();
        }
    }
}
