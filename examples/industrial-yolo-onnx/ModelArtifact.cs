using System.Security.Cryptography;

namespace IndustrialYolo.Onnx;

public static class ModelArtifact
{
    public const string Url = "https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx";
    public const int Size = 3_659_407;
    public const string Sha256 = "C789161ED43C8269FCD4E67C67EEEB4E80C622DA2EB296A20BC6007BD18A0B7D";

    public static byte[] ReadVerified(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != Size) throw new ArgumentException("Model byte length does not match pinned YOLOX-Nano.");
        byte[] bytes = new byte[Size];
        file.ReadExactly(bytes);
        Verify(bytes);
        return bytes;
    }

    public static void Verify(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size || Convert.ToHexString(SHA256.HashData(bytes)) != Sha256)
            throw new ArgumentException("Model length/SHA256 mismatch; no runtime load attempted.");
    }

    public static async Task FetchAsync(string path)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path)) { _ = ReadVerified(path); return; }
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = Size + 1 };
        byte[] bytes = await client.GetByteArrayAsync(Url);
        Verify(bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
        bool created = false;
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                await output.WriteAsync(bytes);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
