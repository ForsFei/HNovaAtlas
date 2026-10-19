using System.Text;

DirectoryInfo sandbox = Directory.CreateTempSubdirectory("hnova-publish-");
try
{
    byte[] data = Encoding.UTF8.GetBytes("pump-A,72\n");
    string published = Path.Combine(sandbox.FullName, "readings.csv");
    using (var source = new MemoryStream(data))
        await PublishNewAsync(source, published, data.Length, CancellationToken.None);
    Check(await File.ReadAllTextAsync(published) == "pump-A,72\n", "complete file is visible");

    using (var source = new MemoryStream(data))
        await ExpectAsync<IOException>(() => PublishNewAsync(source, published,
            data.Length, CancellationToken.None));
    Check(await File.ReadAllTextAsync(published) == "pump-A,72\n",
        "an existing target is not overwritten");

    string invalid = Path.Combine(sandbox.FullName, "invalid.csv");
    using (var source = new MemoryStream(data))
        await ExpectAsync<InvalidDataException>(() => PublishNewAsync(source, invalid,
            data.Length + 1, CancellationToken.None));
    Check(!File.Exists(invalid), "a failed length check publishes nothing");

    string canceled = Path.Combine(sandbox.FullName, "canceled.csv");
    using var stop = new CancellationTokenSource();
    using (var source = new CancelOnSecondReadStream(data, stop))
        await ExpectAsync<OperationCanceledException>(() => PublishNewAsync(source,
            canceled, data.Length, stop.Token));
    Check(!File.Exists(canceled), "cancellation after one written chunk publishes nothing");
    Check(Directory.GetFiles(sandbox.FullName, "*.tmp").Length == 0,
        "failed attempts remove their own temporary files");
    Console.WriteLine("文件发布、冲突、校验失败与取消断言全部通过。");
}
finally
{
    sandbox.Delete(recursive: true); // Only the directory created for this example.
}

static async Task PublishNewAsync(Stream source, string destination, long expectedBytes,
    CancellationToken cancellationToken)
{
    if (expectedBytes < 0) throw new ArgumentOutOfRangeException(nameof(expectedBytes));
    string fullPath = Path.GetFullPath(destination);
    string directory = Path.GetDirectoryName(fullPath)!;
    string temporary = Path.Combine(directory,
        $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
    bool ownsTemporary = false;
    try
    {
        await using (var output = new FileStream(temporary, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            ownsTemporary = true;
            byte[] buffer = new byte[81920];
            long written = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (count > expectedBytes - written)
                    throw new InvalidDataException("Input exceeds the expected byte count.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                written += count;
            }
            if (written != expectedBytes)
                throw new InvalidDataException("Input ended before the expected byte count.");
            await output.FlushAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(temporary, fullPath); // No overwrite; the caller must own the target name.
        ownsTemporary = false;
    }
    catch (Exception original)
    {
        if (ownsTemporary)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
        }
        throw;
    }
}

static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class CancelOnSecondReadStream(byte[] bytes, CancellationTokenSource stop)
    : MemoryStream(bytes)
{
    private int reads;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (++reads == 2) stop.Cancel();
        return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4)], cancellationToken);
    }
}
