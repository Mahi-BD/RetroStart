using System.Windows;
using WinlyStart.Core;

namespace WinlyStart.UI;

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

        var r = s.Rail;
        RailDocuments.IsChecked = r.Documents;
        RailDownloads.IsChecked = r.Downloads;
        RailMusic.IsChecked = r.Music;
        RailPictures.IsChecked = r.Pictures;
        RailVideos.IsChecked = r.Videos;
        RailNetwork.IsChecked = r.Network;
        RailPersonal.IsChecked = r.PersonalFolder;
        RailExplorer.IsChecked = r.FileExplorer;
        RailSettings.IsChecked = r.Settings;
        RailCalendar.IsChecked = r.Calendar;
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

        var r = s.Rail;
        r.Documents = RailDocuments.IsChecked == true;
        r.Downloads = RailDownloads.IsChecked == true;
        r.Music = RailMusic.IsChecked == true;
        r.Pictures = RailPictures.IsChecked == true;
        r.Videos = RailVideos.IsChecked == true;
        r.Network = RailNetwork.IsChecked == true;
        r.PersonalFolder = RailPersonal.IsChecked == true;
        r.FileExplorer = RailExplorer.IsChecked == true;
        r.Settings = RailSettings.IsChecked == true;
        r.Calendar = RailCalendar.IsChecked == true;

        App.ApplySettings();
        Close();
    }
}
