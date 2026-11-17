using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MMOnsterpatchLauncher.Avalonia;

public partial class SimpleMessageWindow : Window
{
    public SimpleMessageWindow()
        : this("MMOnsterpatch Launcher", "")
    {
    }

    public SimpleMessageWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
