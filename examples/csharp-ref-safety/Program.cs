byte[] small = [3, 5, 7];
Check(SumBytes(small) == 15, "small input");
Check(small.SequenceEqual(new byte[] { 3, 5, 7 }), "caller buffer remains unchanged");

{
    Span<byte> local = stackalloc byte[] { 4, 6 };
    Check(SumBytes(local) == 10, "scoped parameter accepts a stack-backed view");
}

byte[] large = Enumerable.Repeat((byte)1, 200).ToArray();
Check(SumBytes(large) == 200, "large input uses the array branch");
Check(await SumAfterYieldAsync(small) == 15, "ref struct local stays after await");
Console.WriteLine("ref struct、scoped 与栈/堆缓冲分支验证通过。");

static int SumBytes(scoped ReadOnlySpan<byte> input)
{
    const int stackLimit = 128;
    Span<byte> scratch = input.Length <= stackLimit
        ? stackalloc byte[input.Length]
        : new byte[input.Length];

    // stackalloc 的未初始化区域不可先读；这里先完整填充有效区域。
    input.CopyTo(scratch);
    var cursor = new ByteCursor(scratch);
    int sum = 0;
    while (cursor.TryRead(out byte value)) sum += value;
    return sum;
}

static async Task<int> SumAfterYieldAsync(byte[] bytes)
{
    await Task.Yield();
    int sum;
    {
        ReadOnlySpan<byte> view = bytes;
        sum = SumBytes(view);
    }
    return sum;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

ref struct ByteCursor
{
    private ReadOnlySpan<byte> _remaining;

    public ByteCursor(ReadOnlySpan<byte> source) => _remaining = source;

    public bool TryRead(out byte value)
    {
        if (_remaining.IsEmpty)
        {
            value = default;
            return false;
        }
        value = _remaining[0];
        _remaining = _remaining[1..];
        return true;
    }
}
