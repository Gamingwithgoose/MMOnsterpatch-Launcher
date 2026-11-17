using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MMOnsterpatchLauncher.Wpf;

public partial class ThemedMessageWindow : Window
{
    public ThemedMessageWindow(string title, string message, ThemedMessageKind kind)
    {
        InitializeComponent();
        Title = title;
        DialogTitleText.Text = title;
        MessageText.Text = message;

        switch (kind)
        {
            case ThemedMessageKind.Error:
                IconText.Text = "!";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA6, 0xA6));
                IconCircle.Stroke = new SolidColorBrush(Color.FromRgb(0xE3, 0x3A, 0x3A));
                break;
            case ThemedMessageKind.Warning:
                IconText.Text = "!";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x8A));
                IconCircle.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x4D));
                break;
            default:
                IconText.Text = "i";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xDE, 0xC5, 0xFF));
                IconCircle.Stroke = new SolidColorBrush(Color.FromRgb(0xDE, 0xC5, 0xFF));
                break;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
}

public enum ThemedMessageKind
{
    Info,
    Warning,
    Error
}

public static class ThemedMessageBox
{
    public static void Show(Window? owner, string message, string title, ThemedMessageKind kind = ThemedMessageKind.Info)
    {
        var window = new ThemedMessageWindow(title, message, kind);
        if (owner is not null)
            window.Owner = owner;
        else if (Application.Current?.MainWindow is not null && Application.Current.MainWindow != window)
            window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }
}
