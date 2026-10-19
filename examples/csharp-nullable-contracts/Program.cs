using System.Diagnostics.CodeAnalysis;

string? candidate = " pump-A ";
if (TextRules.HasText(candidate))
    Check(candidate.Trim() == "pump-A", "成功检查后可安全访问");

Check(!TextRules.HasText(null), "null 应检查失败");
Check(!TextRules.HasText("   "), "空白应检查失败");

var session = new DeviceSession(" pump-A ");
Check(session.DeviceId == "pump-A", "构造时完成规范化");

try
{
    _ = new DeviceSession(null);
    throw new Exception("空值应在运行时被拒绝");
}
catch (ArgumentException ex) when (ex.ParamName == "raw") { }
Console.WriteLine("可空契约断言通过");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

public static class TextRules
{
    public static bool HasText([NotNullWhen(true)] string? text) =>
        !string.IsNullOrWhiteSpace(text);
}

public sealed class DeviceSession
{
    private string _deviceId;

    public DeviceSession(string? raw) => Initialize(raw);

    public string DeviceId => _deviceId;

    [MemberNotNull(nameof(_deviceId))]
    private void Initialize(string? raw)
    {
        if (!TextRules.HasText(raw))
            throw new ArgumentException("设备 ID 不能为空或空白。", nameof(raw));

        _deviceId = raw.Trim();
    }
}
