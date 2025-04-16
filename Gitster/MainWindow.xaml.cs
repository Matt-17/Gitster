using System.Windows;

using LibGit2Sharp;

namespace Gitster;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private string _path;

    public MainWindow()
    {
        InitializeComponent();

        DatePicker.SelectedDate = DateTime.Now;
        HoursSlider.Value = DateTime.Now.Hour;
        MinutesSlider.Value = DateTime.Now.Minute;

        DaysSlider.Value = DateTime.Now.Day;
        MonthsSlider.Value = DateTime.Now.Month;

        Path = Properties.Settings.Default.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private string Path
    {
        get => _path;
        set
        {
            _path = value;
            UpdateSettingsPath();
            UpdateFolderTextBox();
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        using var repo = new Repository(Path);

        if (DatePicker.SelectedDate == null)
        {
            MessageBox.Show("Please select a date");
            return;
        }

        var commit = repo.Head.Tip;
        var author = commit.Author;
        var committer = commit.Committer;

        var currentTimezoneOffset = DateTimeOffset.Now.Offset;

        var year = DatePicker.SelectedDate.Value.Year;
        var month = DatePicker.SelectedDate.Value.Month;
        var day = DatePicker.SelectedDate.Value.Day;
        var hour = (int)HoursSlider.Value;
        var minute = (int)MinutesSlider.Value;
        var newAuthor = new Signature(author.Name, author.Email, new DateTimeOffset(year, month, day, hour, minute, author.When.Second, currentTimezoneOffset));
        var newCommiter = new Signature(committer.Name, committer.Email, new DateTimeOffset(year, month, day, hour, minute, committer.When.Second, currentTimezoneOffset));
        var commitOptions = new CommitOptions
        {
            AmendPreviousCommit = true,
        };

        repo.Commit(commit.Message, newAuthor, newCommiter, commitOptions);

        UpdateElements();
    }

    private void TimeSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TimeTextBlock.Text = $"{(int)HoursSlider.Value:00}:{(int)MinutesSlider.Value:00}";
    }

    private void ButtonRead_Click(object sender, RoutedEventArgs e)
    {
        using var repo = new Repository(Path);

        var commit = repo.Head.Tip.Parents.First();
        var author = commit.Author;
        DatePicker.SelectedDate = new DateTime(author.When.Year, author.When.Month, author.When.Day);
        HoursSlider.Value = author.When.Hour;
        MinutesSlider.Value = author.When.Minute;

    }

    private void DatePicker_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DatePicker.SelectedDate == null)
        {
            return;
        }

        var currentDate = DatePicker.SelectedDate ?? DateTime.Now;
        DaysSlider.Maximum = DateTime.DaysInMonth(currentDate.Year, currentDate.Month);
        DaysSlider.Value = currentDate.Day;
        MonthsSlider.Value = currentDate.Month;
    }

    private void DateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DatePicker?.SelectedDate is null)
        {
            return;
        }

        try
        {
            var months = (int)MonthsSlider.Value;

            var daysSliderMaximum = DateTime.DaysInMonth(DatePicker.SelectedDate.Value.Year, months);
            DaysSlider.Value = Math.Min(DaysSlider.Value, daysSliderMaximum);

            var days = (int)DaysSlider.Value;

            var currentDate = DatePicker.SelectedDate ?? DateTime.Now;
            var newDate = new DateTime(currentDate.Year, months, days);

            DatePicker.SelectedDate = newDate;
        }
        catch (Exception)
        {
            // ignored
            // this is a workaround for the exception that is thrown when the date is invalid
            // we can ignore this exception because we are already handling it in the DatePicker_SelectedDateChanged method
        }
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        UpdateElements();
    }

    private void FolderTb_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Path = FolderTb.Text;
        UpdateElements();
    }

    private void ButtonTime_Click(object sender, RoutedEventArgs e)
    {
        using var repo = new Repository(Path);
        var commit = repo.Head.Tip;
        var author = commit.Author;

        DatePicker.SelectedDate = new DateTime(author.When.Year, author.When.Month, author.When.Day);
        HoursSlider.Value = author.When.Hour;
        MinutesSlider.Value = author.When.Minute;
    }

    private void UpdateFolderTextBox()
    {
        FolderTb.Text = Path;
    }

    private void UpdateSettingsPath()
    {
        var settings = Properties.Settings.Default;
        settings.Path = Path;
        settings.Save();
    }

    private void UpdateElements()
    {
        try
        {
            using var repo = new Repository(Path);

            CommitName.Text = repo.Head.Tip.MessageShort;
            CommitDate.Text = repo.Head.Tip.Author.When.ToString(@"dd.MM.yyyy \u\m HH:mm");

            var commit = repo.Head.Tip.Parents.First();
            PreviousCommitName.Text = commit.MessageShort;

            PreviousDate.Text = commit.Author.When.ToString(@"dd.MM.yyyy \u\m HH:mm");

            GoButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            // empty all the fields
            CommitName.Text = string.Empty;
            CommitDate.Text = string.Empty;

            PreviousCommitName.Text = string.Empty;
            DatePicker.SelectedDate = null;

            MonthsSlider.Value = 1;
            DaysSlider.Value = 1;
            HoursSlider.Value = 0;
            MinutesSlider.Value = 0;

            GoButton.IsEnabled = false;
        }
    }
}