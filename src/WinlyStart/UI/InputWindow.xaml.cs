using System.IO;
using System.Windows;

namespace WinlyStart.UI;

/// <summary>Adds a website, or edits any user-added item (website, program or file).</summary>
public partial class InputWindow : Window
{
    public string ResultValue { get; private set; } = string.Empty;
    public string ResultName { get; private set; } = string.Empty;

    private readonly bool _fileMode;

    /// <param name="name">Existing name when editing; null adds a new website.</param>
    /// <param name="target">Existing URL or path when editing.</param>
    public InputWindow(string? name = null, string? target = null)
    {
        InitializeComponent();
        Dialog.Apply(this);

        bool editing = target is { Length: > 0 };
        _fileMode = editing && !LooksLikeUrl(target!);

        if (editing)
        {
            Title = "Edit item";
            Prompt.Text = "Edit this item";
            OkButton.Content = "Save";
            ValueBox.Text = target!;
            NameBox.Text = name ?? string.Empty;
            if (_fileMode)
            {
                HeaderGlyph.Text = "";                    // document glyph
                SubPrompt.Text = "Change the program or file this tile opens.";
                ValueLabel.Text = "Program or file";
                BrowseButton.Visibility = Visibility.Visible;
            }
            else SubPrompt.Text = "Change the address or the name shown on the tile.";
        }

        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.CaretIndex = ValueBox.Text.Length; };
    }

    private static bool LooksLikeUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a program or file",
            Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd;*.msc|All files|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        ValueBox.Text = dlg.FileName;
        if (NameBox.Text.Trim().Length == 0) NameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string value = ValueBox.Text.Trim();
        if (value.Length == 0) { Fail("Enter an address."); return; }

        if (_fileMode)
        {
            if (!File.Exists(value) && !Directory.Exists(value)) { Fail("That file could not be found."); return; }
            ResultValue = value;
            ResultName = NameBox.Text.Trim() is { Length: > 0 } fn ? fn : Path.GetFileNameWithoutExtension(value);
            DialogResult = true;
            return;
        }

        if (value == "https://") { Fail("Enter a web address."); return; }
        if (!value.Contains("://")) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
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
