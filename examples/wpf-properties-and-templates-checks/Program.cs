using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfPropertiesAndTemplates;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var badge = new StatusBadge();
        Check(badge.StatusText == "待机", "依赖属性默认值");
        badge.StatusText = "运行中";
        Check(badge.StatusText == "运行中", "依赖属性本地值");

        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.SteelBlue));
        button.Style = style;
        Check(ReferenceEquals(button.Background, Brushes.SteelBlue), "样式 setter 生效");
        Check(DependencyPropertyHelper.GetValueSource(button, Control.BackgroundProperty).BaseValueSource
              == BaseValueSource.Style, "样式是有效值来源");
        button.Background = Brushes.Gold;
        Check(ReferenceEquals(button.Background, Brushes.Gold), "本地值覆盖样式 setter");
        Check(DependencyPropertyHelper.GetValueSource(button, Control.BackgroundProperty).BaseValueSource
              == BaseValueSource.Local, "本地值是有效值来源");
        button.ClearValue(Control.BackgroundProperty);
        Check(ReferenceEquals(button.Background, Brushes.SteelBlue), "清除本地值后恢复样式值");

        var grid = new Grid();
        Grid.SetRow(button, 1);
        Check(Grid.GetRow(button) == 1, "Grid.Row 附加属性保存在子元素上");
        grid.Children.Add(button);
        int routedCount = 0;
        grid.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            Check(ReferenceEquals(sender, grid), "父容器是监听者");
            Check(ReferenceEquals(e.Source, button), "按钮是事件源");
            routedCount++;
        }));
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        Check(routedCount == 1, "Click 冒泡到父容器");

        Console.WriteLine("依赖属性、值优先级、附加属性与路由事件断言通过。");
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
