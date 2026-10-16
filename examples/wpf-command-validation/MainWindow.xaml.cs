using System.Windows;

namespace WpfCommandValidation;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ThresholdViewModel();
    }
}
