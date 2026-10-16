var snapshot = new DeviceSnapshot("pump-A", 72);
ITextExport export = snapshot;
IAuditText audit = snapshot;
IFormatVersion versioned = snapshot;

Check(export.Render() == "pump-A,72", "export interface exposes the reading");
Check(audit.Render() == "device=pump-A;value=[masked]",
    "audit interface uses a distinct implementation");
Check(versioned.FormatVersion() == "v1", "interface reference calls the default body");
Check(typeof(DeviceSnapshot).GetMethod("Render") is null
    && typeof(DeviceSnapshot).GetMethod("FormatVersion") is null,
    "neither member appears on the class public surface");

IFormatVersion customized = new CustomSnapshot();
Check(customized.FormatVersion() == "v2", "a class member can replace the default");
Console.WriteLine("显式接口实现、默认成员与调用视图断言全部通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public interface ITextExport
{
    string Render();
}

public interface IAuditText
{
    string Render();
}

public interface IFormatVersion
{
    string FormatVersion() => "v1";
}

public sealed class DeviceSnapshot(string id, int value)
    : ITextExport, IAuditText, IFormatVersion
{
    string ITextExport.Render() => $"{id},{value}";
    string IAuditText.Render() => $"device={id};value=[masked]";
}

public sealed class CustomSnapshot : IFormatVersion
{
    public string FormatVersion() => "v2";
}
