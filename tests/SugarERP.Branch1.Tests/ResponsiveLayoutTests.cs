using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using SugarERP.Branch1;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class ResponsiveLayoutTests
{
    [Fact]
    public async Task PosReflowsAtTargetLogicalWidths()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutTestApp));
        await session.Dispatch(() =>
        {
            foreach (var (width, height, expectedColumns) in new[]
            {
                (853d, 480d, 1),  // 1280x720 at 150% DPI
                (1024d, 576d, 1), // 1280x720 at 125% DPI
                (1280d, 720d, 3),
                (1366d, 768d, 3),
                (1600d, 900d, 3),
                (1920d, 1080d, 3)
            })
            {
                var window = new MainWindow
                {
                    WindowState = WindowState.Normal,
                    Width = width,
                    Height = height,
                    DataContext = new { IsPosVisible = true }
                };
                window.Show();
                var grid = window.FindControl<Grid>("PosColumns")!;
                Assert.Equal(expectedColumns, grid.ColumnDefinitions.Count);
                Assert.True(grid.Bounds.Width <= window.Bounds.Width);
                window.Close();
            }
        }, CancellationToken.None);
    }
}

public sealed class LayoutTestApp : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
