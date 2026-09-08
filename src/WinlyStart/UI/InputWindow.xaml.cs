using System.Windows;

namespace WinlyStart.UI;

/// <summary>Small prompt used by "Add website…".</summary>
public partial class InputWindow : Window
{
    public string ResultValue { get; private set; } = string.Empty;
    public string ResultName { get; private set; } = string.Empty;

    public InputWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.CaretIndex = ValueBox.Text.Length; };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string url = ValueBox.Text.Trim();
        if (url.Length == 0 || url == "https://") { Fail("Enter a web address."); return; }
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Fail("That does not look like a web address.");
            return;
        }
        ResultValue = uri.ToString();
        ResultName = NameBox.Text.Trim() is { Length: > 0 } n ? n : uri.Host;
        DialogResult = true;
    }

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
