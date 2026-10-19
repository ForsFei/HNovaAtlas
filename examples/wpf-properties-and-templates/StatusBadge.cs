using System.Windows;
using System.Windows.Controls;

namespace WpfPropertiesAndTemplates;

public sealed class StatusBadge : Control
{
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText),
            typeof(string),
            typeof(StatusBadge),
            new PropertyMetadata("待机"));

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }
}
