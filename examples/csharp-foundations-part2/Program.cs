using Atlas.Foundations;

Assert(!Enum.IsDefined((JobState)99), "数值转换可产生未声明状态");
Assert(Enum.TryParse<JobState>("Running", true, out var parsed) && Enum.IsDefined(parsed), "名称解析与合法性检查");

Access granted = Access.Read | Access.Write;
Assert((granted & Access.Write) == Access.Write, "位组合包含写入权限");
const Access known = Access.Read | Access.Write | Access.Execute;
Assert((((Access)9 & ~known) != Access.None), "未知权限位应被发现");

var result = Range([4, 9, 2]);
var (smallest, largest) = result;
Assert(smallest == 2 && largest == 9, "解构按位置取值");
Assert((1, 2) == (1, 2), "元组逐位置比较");
var array = new[] { 1 };
var original = (items: array, count: 1);
var copy = original;
copy.items[0] = 7;
Assert(original.items[0] == 7, "元组复制不深拷贝引用元素");

Assert(" atlas ".NormalizeKey() == "ATLAS", "扩展方法调用");
Assert(TextExtensions.NormalizeKey(" atlas ") == "ATLAS", "静态方法调用");
bool rejectedNull = false;
try { _ = TextExtensions.NormalizeKey(null!); }
catch (ArgumentNullException) { rejectedNull = true; }
Assert(rejectedNull, "显式拒绝 null 接收者");

Console.WriteLine("C# 基础补强示例全部通过：enum、元组、扩展方法。");

static (int min, int max) Range(ReadOnlySpan<int> values)
{
    if (values.IsEmpty) throw new ArgumentException("至少需要一个值");
    int min = values[0], max = values[0];
    foreach (int value in values)
    {
        min = Math.Min(min, value);
        max = Math.Max(max, value);
    }
    return (min, max);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

namespace Atlas.Foundations
{
    enum JobState { Unknown = 0, Queued = 1, Running = 2, Done = 3 }

    [Flags]
    enum Access { None = 0, Read = 1, Write = 2, Execute = 4 }

    static class TextExtensions
    {
        public static string NormalizeKey(this string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Trim().ToUpperInvariant();
        }
    }
}
