var counter = new MutableCounter(5);
Check(TryIncrement(in counter) == 5, "in 参数调用落在防御性副本上");
Check(counter.Value == 5, "调用方原值不变");
counter.Increment();
Check(counter.Value == 6, "可写局部变量可正常修改");

var mutable = new MutableReading("pump-A", 72);
mutable.Value = 73; // record struct 的位置属性默认可写
Check(mutable.Value == 73, "普通 record struct 可变");

var first = new FrozenReading("pump-A", 72);
var second = first with { Value = 73 };
Check(first.Value == 72 && second.Value == 73, "with 产生新的值");

int[] source = [1];
var shallow = new ShallowSnapshot(source);
source[0] = 9;
Check(shallow.First == 9, "readonly struct 没有冻结引用字段指向的数组");
Console.WriteLine("结构体只读与复制断言通过");

static int TryIncrement(in MutableCounter value)
{
    value.Increment(); // 编译器在副本上调用非 readonly 成员
    return value.Value;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

public struct MutableCounter(int value)
{
    public int Value = value;
    public void Increment() => Value++;
}

public record struct MutableReading(string DeviceId, int Value);
public readonly record struct FrozenReading(string DeviceId, int Value);

public readonly struct ShallowSnapshot
{
    private readonly int[] _source;
    public ShallowSnapshot(int[] source) => _source = source;
    public int First => _source[0];
}
