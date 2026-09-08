using System.Windows;
using RetroStart.Core;

namespace RetroStart.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var s = App.Settings;
        WinKey.IsChecked = s.ReplaceWinKey;
        StartButton.IsChecked = s.ReplaceStartButton;
        Autostart.IsChecked = s.StartWithWindows;
        ThemeBox.SelectedIndex = (int)s.Theme;
        ColumnsBox.SelectedIndex = s.TileColumns == 8 ? 1 : 0;
        HeightSlider.Value = s.MenuHeight;
        AtStartButton.IsChecked = s.OpenAtStartButton;
        Recent.IsChecked = s.ShowRecentlyAdded;
        MostUsed.IsChecked = s.ShowMostUsed;
        Trim.IsChecked = s.TrimMemoryWhenHidden;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;
        s.ReplaceWinKey = WinKey.IsChecked == true;
        s.ReplaceStartButton = StartButton.IsChecked == true;
        s.StartWithWindows = Autostart.IsChecked == true;
        s.Theme = (ThemeMode)Math.Max(0, ThemeBox.SelectedIndex);
        s.TileColumns = ColumnsBox.SelectedIndex == 1 ? 8 : 6;
        s.MenuHeight = (int)HeightSlider.Value;
        s.OpenAtStartButton = AtStartButton.IsChecked == true;
        s.ShowRecentlyAdded = Recent.IsChecked == true;
        s.ShowMostUsed = MostUsed.IsChecked == true;
        s.TrimMemoryWhenHidden = Trim.IsChecked == true;
        App.ApplySettings();
        Close();
    }
}
