ByteCount first = (ByteCount)1024L;
ByteCount second = (ByteCount)512L;
ByteCount total = first + second;
ByteCount sameTotal = (ByteCount)1536L;

Check(total.Value == 1536 && (long)total == 1536L, "addition preserves the unit");
Check(total == sameTotal && !(total != sameTotal) && total.Equals(sameTotal),
    "operators and typed equality agree");
Check(total.GetHashCode() == sameTotal.GetHashCode(), "equal values have equal hashes");
Check(new HashSet<ByteCount> { total, sameTotal }.Count == 1,
    "HashSet uses the same equality contract");
Check(default(ByteCount) == (ByteCount)0L, "default value is valid zero");

Expect<ArgumentOutOfRangeException>(() => { _ = (ByteCount)(-1L); });
Expect<OverflowException>(() => { _ = (ByteCount)long.MaxValue + (ByteCount)1L; });
Console.WriteLine("运算符、转换、不变量与相等性断言全部通过。");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Expect<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

public readonly struct ByteCount : IEquatable<ByteCount>
{
    public long Value { get; }

    public ByteCount(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public static ByteCount operator +(ByteCount left, ByteCount right)
        => new(checked(left.Value + right.Value));

    public static bool operator ==(ByteCount left, ByteCount right) => left.Equals(right);
    public static bool operator !=(ByteCount left, ByteCount right) => !left.Equals(right);

    public static explicit operator ByteCount(long value) => new(value);
    public static explicit operator long(ByteCount value) => value.Value;

    public bool Equals(ByteCount other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ByteCount other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
}
