using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using WinlyStart.Core;

namespace WinlyStart.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {v?.ToString(3) ?? "0.1.0"}  ·  .NET 8  ·  MIT licence";
        DataPathText.Text = "Settings and tiles: " + Store.Dir;
    }

    private void Link_Click(object sender, RequestNavigateEventArgs e)
    {
        Launcher.Start(e.Uri.ToString());
        e.Handled = true;
    }
}
