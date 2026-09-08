using System.Windows;

namespace WinlyStart.UI;

public partial class CalendarWindow : Window
{
    public CalendarWindow()
    {
        InitializeComponent();
        Cal.SelectedDate = DateTime.Today;
        Cal.DisplayDate = DateTime.Today;
        UpdateHeader();
    }

    private void Cal_Changed(object sender, EventArgs e) => UpdateHeader();

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        Cal.DisplayDate = DateTime.Today;
        Cal.SelectedDate = DateTime.Today;
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var d = Cal.SelectedDate ?? DateTime.Today;
        TodayText.Text = d.ToString("dddd, d MMMM yyyy");
    }
}
