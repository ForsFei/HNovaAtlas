using System.Globalization;

Check(!TryReadTemperature("bad", out _), "无效输入是预期失败");
Check(!TryReadTemperature("NaN", out _), "非有限数值不能进入业务计算");
Check(TryReadTemperature("72.5", out double temperature) && temperature == 72.5,
    "有效输入应成功解析");

var diskFailure = new IOException("注入的存储失败");
try
{
    ReadDevice("pump-A", () => throw diskFailure);
    throw new Exception("应抛出带上下文的异常");
}
catch (DeviceReadException ex)
{
    Check(ReferenceEquals(ex.InnerException, diskFailure), "原始原因应保留");
}

var otherFailure = new IOException("另一个注入失败");
try
{
    ReadDevice("sensor-X", () => throw otherFailure);
    throw new Exception("筛选不匹配时应继续传播原异常");
}
catch (IOException ex)
{
    Check(ReferenceEquals(ex, otherFailure), "不匹配的筛选不应包装异常");
}

var relayFailure = new IOException("原始异常");
try
{
    Relay(() => throw relayFailure);
    throw new Exception("应原样重抛");
}
catch (IOException ex)
{
    Check(ReferenceEquals(ex, relayFailure), "throw; 保留同一异常对象");
}
Console.WriteLine("错误边界断言通过");

static bool TryReadTemperature(string? text, out double value) =>
    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
    && double.IsFinite(value);

static void ReadDevice(string deviceId, Action read)
{
    try { read(); }
    catch (IOException ex) when (deviceId.StartsWith("pump-", StringComparison.Ordinal))
    {
        throw new DeviceReadException($"设备 {deviceId} 读取失败。", ex);
    }
}

static void Relay(Action action)
{
    try { action(); }
    catch (IOException) { throw; } // 同一 catch 内保留原始堆栈信息
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

public sealed class DeviceReadException : Exception
{
    public DeviceReadException() { }
    public DeviceReadException(string message) : base(message) { }
    public DeviceReadException(string message, Exception innerException)
        : base(message, innerException) { }
}
