using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinlyStart.Core;

namespace WinlyStart.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Dialog.Apply(this);
        var s = App.Settings;
        WinKey.IsChecked = s.ReplaceWinKey;
        StartButton.IsChecked = s.ReplaceStartButton;
        Autostart.IsChecked = s.StartWithWindows;
        ThemeBox.SelectedIndex = (int)s.Theme;
        ColumnsBox.SelectedIndex = s.TileColumns == 8 ? 1 : 0;
        HeightSlider.Value = s.MenuHeight;
        AtCorner.IsChecked = s.OpenAtCorner;
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

        _profilePath = s.ProfileImagePath;
        ShowProfile();
    }

    // ───────────────────────── account picture ─────────────────────────

    private string _profilePath = string.Empty;

    private void ShowProfile()
    {
        string? path = _profilePath.Length > 0 && File.Exists(_profilePath) ? _profilePath : UserInfo.WindowsPicturePath;
        ProfileCaption.Text = _profilePath.Length > 0
            ? "Custom picture · shown on the user button in the left rail."
            : "Using your Windows account picture.";
        ProfileBrush.ImageSource = null;
        if (path == null) return;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.DecodePixelWidth = 128;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            ProfileBrush.ImageSource = bi;
        }
        catch { }
    }

    private void ChoosePicture_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an account picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        _profilePath = dlg.FileName;
        ShowProfile();
    }

    private void ResetPicture_Click(object sender, RoutedEventArgs e)
    {
        _profilePath = string.Empty;
        ShowProfile();
    }

    // IsCancel only closes windows opened with ShowDialog(); this window is shown with Show().
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;
        s.ReplaceWinKey = WinKey.IsChecked == true;
        s.ReplaceStartButton = StartButton.IsChecked == true;
        s.StartWithWindows = Autostart.IsChecked == true;
        s.Theme = (ThemeMode)Math.Max(0, ThemeBox.SelectedIndex);
        s.TileColumns = ColumnsBox.SelectedIndex == 1 ? 8 : 6;
        s.MenuHeight = (int)HeightSlider.Value;
        s.OpenAtCorner = AtCorner.IsChecked == true;
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
        s.ProfileImagePath = _profilePath;

        App.ApplySettings();
        Close();
    }
}
