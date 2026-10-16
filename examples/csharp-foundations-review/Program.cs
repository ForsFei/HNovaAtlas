using System.Text;

int[] readings = [68, 72, 85];
int[] alias = readings;
alias[0] = 69;
Require(readings[0] == 69, "数组引用共享");
int[] copied = readings[1..];
copied[0] = 99;
Require(readings[1] == 72, "范围切片复制数组");
Span<int> view = readings.AsSpan(1);
view[0] = 73;
Require(readings[1] == 73, "Span 视图共享数据");

Require(string.Equals("pump-A", "PUMP-A", StringComparison.OrdinalIgnoreCase),
    "显式字符串比较规则");
var builder = new StringBuilder();
foreach (string part in new[] { "pump", "-", "A" }) builder.Append(part);
Require(builder.ToString() == "pump-A", "StringBuilder 拼接");

object? input = new Reading("pump-A", 82);
string status = input switch
{
    Reading { Value: >= 80 } => "危险",
    Reading { Value: >= 70 } => "预警",
    Reading => "正常",
    null => "缺失",
    _ => "不支持的输入"
};
Require(status == "危险", "switch 首个匹配臂");
Require(readings is [< 70, >= 70 and < 80, >= 80], "列表模式");

var monitor = new Monitor("pump-A", 70) { Label = "主泵" };
monitor.Add(72.5);
Require(monitor.Count == 1 && monitor[0] == 72.5, "计算属性与索引器");
monitor.ChangeLimit(75);
Require(!monitor.IsAlarm(72.5), "属性 setter 验证后的状态改变");

Console.WriteLine("基础补强示例验证通过：数组、字符串、模式匹配、属性与索引器");

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{label}");
}

public sealed record Reading(string DeviceId, double Value);

public sealed class Monitor
{
    private readonly List<double> _readings = [];
    private double _limit;

    public string DeviceId { get; }
    public string? Label { get; init; }
    public int Count => _readings.Count;
    public double this[int index] => _readings[index];

    public double Limit
    {
        get => _limit;
        private set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _limit = value;
        }
    }

    public Monitor(string deviceId, double limit)
    {
        DeviceId = !string.IsNullOrWhiteSpace(deviceId)
            ? deviceId : throw new ArgumentException("设备 ID 不能为空", nameof(deviceId));
        Limit = limit;
    }

    public void ChangeLimit(double value) => Limit = value;
    public void Add(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        _readings.Add(value);
    }
    public bool IsAlarm(double value) => value >= Limit;
}
