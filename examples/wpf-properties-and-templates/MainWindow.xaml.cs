using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WpfPropertiesAndTemplates;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ReadingsList.ItemsSource = new[]
        {
            new ReadingSnapshot("泵 A", 72),
            new ReadingSnapshot("泵 B", 68)
        };
        Actions.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(Actions_Click));
    }

    private void Actions_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is Button button)
            RouteText.Text = $"父容器收到来自“{button.Content}”的冒泡 Click。";
    }
}

public sealed record ReadingSnapshot(string Device, int Temperature);
