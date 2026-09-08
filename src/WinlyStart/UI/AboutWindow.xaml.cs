using System.Reflection;
using System.Windows;
using WinlyStart.Core;

namespace WinlyStart.UI;

public partial class AboutWindow : Window
{
    private const string Repo = "https://github.com/Mahi-BD/WinlyStart";

    public AboutWindow()
    {
        InitializeComponent();
        Dialog.Apply(this);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {v?.ToString(3) ?? "0.1.0"} · .NET 8";
        DataPathText.Text = "Settings, tiles, custom items and calendar notes live in " + Store.Dir;
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => Launcher.Start("explorer.exe", "\"" + Store.Dir + "\"");
    private void GitHub_Click(object sender, RoutedEventArgs e) => Launcher.Start(Repo);
    private void Issue_Click(object sender, RoutedEventArgs e) => Launcher.Start(Repo + "/issues/new");
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
