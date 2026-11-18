using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SensorDeck.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDwmStyle();
        StateChanged += (_, _) => OnStateChanged();
        Closing += OnClosing;
    }

    private static App App => (App)Application.Current;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // "Close to tray": keep running in the notification area unless the app is really exiting.
        if (!App.IsExiting && App.Settings.Current.CloseToTray)
        {
            e.Cancel = true;
            App.HideToTray();
        }
    }

    private void OnStateChanged()
    {
        if (WindowState == WindowState.Minimized && App.Settings.Current.MinimizeToTray && !App.IsExiting)
        {
            // Stay "minimised" while hidden; App.ShowMainWindow restores it, keeping WPF and Win32 state in sync.
            Dispatcher.BeginInvoke(App.HideToTray);
            return;
        }

        // A maximised chrome-less window overhangs the screen by the resize border; pad it back in.
        bool max = WindowState == WindowState.Maximized;
        var b = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = max ? new Thickness(b.Left + 4, b.Top + 4, b.Right + 4, b.Bottom + 4) : new Thickness(0);
        MaxButton.Content = max ? "" : "";
        MaxButton.ToolTip = max ? "Restore" : "Maximise";
        System.Windows.Automation.AutomationProperties.SetName(MaxButton, (string)MaxButton.ToolTip);
    }

    // Navigate on Checked (not Click) so mouse, keyboard and accessibility tools all switch pages.
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { CommandParameter: string page } && DataContext is ViewModels.MainViewModel vm)
            vm.NavigateCommand.Execute(page);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ----- Windows 11 dark frame, rounded corners and matching border colour -----

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ApplyDwmStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1, round = DWMWCP_ROUND, border = 0x003B2C23; // #232C3B as COLORREF (0x00BBGGRR)
        // These calls are no-ops (return an error) on older Windows versions, which is fine.
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }
}
