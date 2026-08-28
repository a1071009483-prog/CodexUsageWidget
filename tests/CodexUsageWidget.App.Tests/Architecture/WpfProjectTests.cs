using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using CodexUsageWidget.Core.Abstractions;
using Xunit;

namespace CodexUsageWidget.App.Tests.Architecture;

public sealed class WpfProjectTests
{
    [Fact]
    public void AppProjectBuildsAWindowsPresentationApplication()
    {
        Assembly appAssembly = Assembly.Load("CodexUsageWidget");
        Type? applicationType = appAssembly.GetType("CodexUsageWidget.App.App");

        Assert.NotNull(applicationType);
        Assert.Equal("System.Windows.Application", applicationType.BaseType?.FullName);
        Assert.NotNull(applicationType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void MainWindowExposesAccessibleManualActivationAction()
    {
        bool actionFound = false;
        bool commandBound = false;
        bool helpTextPresent = false;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new CodexUsageWidget.App.MainWindow();
                Button? action = window.FindName("ManualActivationButton") as Button;
                actionFound = action is not null;
                if (action is not null)
                {
                    commandBound = BindingOperations.GetBindingExpression(
                        action,
                        Button.CommandProperty) is not null;
                    helpTextPresent = !string.IsNullOrWhiteSpace(
                        AutomationProperties.GetHelpText(action));
                }
                window.IsShuttingDown = true;
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(actionFound);
        Assert.True(commandBound);
        Assert.True(helpTextPresent);
    }

    [Fact]
    public void MainWindowKeepsManualActivationActionVisibleWhenRestoringLegacyHeight()
    {
        double actionBottom = double.NaN;
        double visibleHeight = double.NaN;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new CodexUsageWidget.App.MainWindow
                {
                    PlacementService = new LegacyHeightPlacementService(),
                };

                SetRepresentativeQuotaCardHeights(window);

                window.Show();
                window.UpdateLayout();

                Button action = Assert.IsType<Button>(window.FindName("ManualActivationButton"));
                actionBottom = action.TranslatePoint(new Point(0, action.ActualHeight), window).Y;
                visibleHeight = window.ActualHeight;

                window.IsShuttingDown = true;
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(
            actionBottom <= visibleHeight,
            $"Manual action bottom {actionBottom:F2} exceeded visible window height {visibleHeight:F2}.");
    }

    [Fact]
    public void MainWindowAutoSizesFreshDefaultToShowManualActivationAction()
    {
        double actionBottom = double.NaN;
        double visibleHeight = double.NaN;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new CodexUsageWidget.App.MainWindow();
                SetRepresentativeQuotaCardHeights(window);

                window.Show();
                window.UpdateLayout();

                Button action = Assert.IsType<Button>(window.FindName("ManualActivationButton"));
                actionBottom = action.TranslatePoint(new Point(0, action.ActualHeight), window).Y;
                visibleHeight = window.ActualHeight;

                window.IsShuttingDown = true;
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(
            actionBottom <= visibleHeight,
            $"Manual action bottom {actionBottom:F2} exceeded visible window height {visibleHeight:F2}.");
    }

    private static void SetRepresentativeQuotaCardHeights(DependencyObject window)
    {
        foreach (ContentControl quotaCard in FindLogicalChildren<ContentControl>(window))
        {
            quotaCard.Height = 100;
        }
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (object childValue in LogicalTreeHelper.GetChildren(parent))
        {
            if (childValue is not DependencyObject child)
            {
                continue;
            }

            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindLogicalChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class LegacyHeightPlacementService : IWindowPlacementService
    {
        private static readonly WindowPlacement LegacyPlacement = new(
            807.33,
            84,
            320,
            312.67);

        public Task SavePlacementAsync(
            WindowPlacement placement,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<WindowPlacement?> LoadPlacementAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<WindowPlacement?>(LegacyPlacement);

        public WindowPlacement ClampPlacement(
            WindowPlacement placement,
            IReadOnlyList<WindowPlacementScreen> screens,
            WindowPlacementScreen workArea) => placement;
    }
}
