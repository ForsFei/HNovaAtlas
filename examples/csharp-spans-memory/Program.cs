using System.Buffers;

CheckViewAndSnapshot();

var pool = new CountingPool();
using (var source = new MemoryStream([1, 2, 3, 4]))
{
    byte[] packet = await ReadPacketAsync(source, 4, pool);
    Check(packet.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "帧内容应正确");
    Check(packet.Length == 4 && pool.ReturnCount == 1, "只复制有效区并归还一次");
}

using (var shortSource = new MemoryStream([9, 8]))
{
    try
    {
        await ReadPacketAsync(shortSource, 4, pool);
        throw new Exception("短读应抛异常");
    }
    catch (EndOfStreamException) { }
}
Check(pool.ReturnCount == 2, "失败路径也应归还");
Console.WriteLine("视图和租期断言通过");

static void CheckViewAndSnapshot()
{
    int[] values = [10, 20, 30];
    Span<int> view = values.AsSpan(1, 2);
    view[0] = 21;
    Check(values[1] == 21, "Span 是原数组视图");
    int[] snapshot = view.ToArray();
    view[0] = 22;
    Check(snapshot[0] == 21, "ToArray 才是独立快照");

    ReadOnlySpan<char> line = "pump-A|72".AsSpan();
    int separator = line.IndexOf('|');
    Check(separator > 0 && line[..separator].SequenceEqual("pump-A"), "设备名解析");
    Check(int.TryParse(line[(separator + 1)..], out int value) && value == 72,
        "读数解析");
}

static async Task<byte[]> ReadPacketAsync(Stream source, int length, ArrayPool<byte> pool)
{
    byte[] rented = pool.Rent(length);
    try
    {
        Memory<byte> target = rented.AsMemory(0, length);
        await source.ReadExactlyAsync(target);
        return target.ToArray(); // 结果必须独立于即将归还的 rented
    }
    finally
    {
        pool.Return(rented, clearArray: true);
    }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class CountingPool : ArrayPool<byte>
{
    public int ReturnCount { get; private set; }

    public override byte[] Rent(int minimumLength) => new byte[minimumLength + 3];

    public override void Return(byte[] array, bool clearArray = false)
    {
        ReturnCount++;
        if (clearArray) Array.Clear(array);
    }
}
