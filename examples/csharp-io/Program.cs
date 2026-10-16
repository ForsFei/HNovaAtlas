using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
    throwOnInvalidBytes: true);

using (MemoryStream bytes = new())
{
    using (var writer = new StreamWriter(bytes, utf8, bufferSize: 1024, leaveOpen: true))
        writer.WriteLine("pump-A,72.5");
    bytes.Position = 0;
    using var reader = new StreamReader(bytes, utf8,
        detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
    Require(reader.ReadLine() == "pump-A,72.5", "文本包装器与 leaveOpen");
}

try
{
    utf8.GetString([0xFF]);
    throw new InvalidOperationException("验证失败：严格 UTF-8 未拒绝坏字节");
}
catch (DecoderFallbackException)
{
    // 严格解码按预期失败。
}

string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
string absolute = Path.GetFullPath(Path.Combine("data", "readings.txt"), baseDirectory);
Require(Path.GetFileName(absolute) == "readings.txt", "路径基准与文件名");

string tempDirectory = Path.Combine(Path.GetTempPath(), "hnova-io-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);
try
{
    string sourcePath = Path.Combine(tempDirectory, "source.txt");
    string targetPath = Path.Combine(tempDirectory, "target.txt");
    byte[] content = utf8.GetBytes("温度,72.5\n");
    await File.WriteAllBytesAsync(sourcePath, content);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await CopyFileAsync(sourcePath, targetPath, cts.Token);
    Require((await File.ReadAllBytesAsync(targetPath)).SequenceEqual(content), "异步文件复制");
}
finally
{
    Directory.Delete(tempDirectory, recursive: true);
}

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
try
{
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, port);
    using TcpClient server = await accepted;

    byte[] payload = utf8.GetBytes("pump-A,72.5");
    byte[] frame = new byte[4 + payload.Length];
    BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), payload.Length);
    payload.CopyTo(frame.AsSpan(4));
    await server.GetStream().WriteAsync(frame.AsMemory(0, 2));
    await server.GetStream().WriteAsync(frame.AsMemory(2));
    Require(await ReadFrameAsync(client.GetStream(), CancellationToken.None) == "pump-A,72.5",
        "回环 TCP 长度帧");
}
finally
{
    listener.Stop();
}

Console.WriteLine("I/O 示例验证通过：流所有权、UTF-8、路径、异步文件与本地 TCP 帧");

static async Task CopyFileAsync(string sourcePath, string targetPath, CancellationToken token)
{
    await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read,
        FileShare.Read, bufferSize: 81920, useAsync: true);
    await using FileStream target = new(targetPath, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, bufferSize: 81920, useAsync: true);
    await source.CopyToAsync(target, token);
    await target.FlushAsync(token);
}

static async Task<string> ReadFrameAsync(Stream input, CancellationToken token)
{
    byte[] header = new byte[4];
    await input.ReadExactlyAsync(header, token);
    int length = BinaryPrimitives.ReadInt32BigEndian(header);
    if (length < 0 || length > 1024 * 1024)
        throw new InvalidDataException("帧长度超出 1 MiB 限制");
    byte[] payload = new byte[length];
    await input.ReadExactlyAsync(payload, token);
    return new UTF8Encoding(false, true).GetString(payload);
}

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"验证失败：{label}");
}
