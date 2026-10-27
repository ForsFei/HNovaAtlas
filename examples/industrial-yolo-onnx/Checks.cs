using Microsoft.ML.OnnxRuntime;

namespace IndustrialYolo.Onnx;

internal static class Checks
{
    private static int count;
    public static void Managed()
    {
        count = 0;
        Bgr24Image image = YoloXInput.CreateFixture();
        YoloXInput input = YoloXInput.PrepareNoResize(image);
        int plane = 416 * 416;
        Check(image.Width == 416 && image.Height == 208, "owned fixture dimensions");
        Check(input.Shape.SequenceEqual(new long[] { 1, 3, 416, 416 }), "fixed NCHW shape");
        Check(input.Values.Length == 3 * plane, "dense array size");
        Check(input.SourceHeight == 208, "content height preserved");
        Check(input.Sha256 == NumericReference.InputSha256, "entire input matches independently generated NumPy hash");
        Check(input.Values[20 * 416 + 10] == 10, "first plane B, no /255");
        Check(input.Values[plane + 20 * 416 + 10] == 20, "second plane G");
        Check(input.Values[2 * plane + 20 * 416 + 10] == 50, "third plane R");
        Check(input.Values[208 * 416] == 114, "padding begins directly below source");
        Check(input.Values[3 * plane - 1] == 114, "last padding pixel");
        Check(input.Values[415] == 159, "horizontal fixture modulo");
        Check(input.Values[207 * 416] == 0, "last source row remains content");
        Check(input.Values.ToArray().All(v => float.IsFinite(v) && v is >= 0 and <= 255), "all values finite in source byte range");
        Reject(() => ((IList<long>)input.Shape)[0] = 99, "read-only shape");
        byte[] bytes = new byte[416 * 3 + 2];
        bytes[0] = 7; bytes[1] = 11; bytes[2] = 19; bytes[^1] = 255;
        Bgr24Image padded = Bgr24Image.Create(416, 1, bytes.Length, bytes);
        YoloXInput small = YoloXInput.PrepareNoResize(padded);
        Check(small.Values[0] == 7 && small.Values[plane] == 11 && small.Values[2 * plane] == 19, "BGR order retained");
        Check(small.Values[416] == 114 && small.Values[415] == 0, "row padding not interpreted as pixels");
        Array.Fill(bytes, (byte)99);
        Check(YoloXInput.PrepareNoResize(padded).Values.SequenceEqual(small.Values), "caller buffer mutation isolated");
        YoloXInput full = YoloXInput.PrepareNoResize(Bgr24Image.Create(416, 416, 1248, new byte[416 * 416 * 3]));
        Check(full.Values.ToArray().All(v => v == 0), "full-height source has no synthetic padding");
        Reject(() => YoloXInput.PrepareNoResize(Bgr24Image.Create(415, 1, 1245, new byte[1245])), "no implicit resizing");
        Reject(() => YoloXInput.PrepareNoResize(Bgr24Image.Create(416, 417, 1248, new byte[1248 * 417])), "too-tall source rejected");
        Reject(() => YoloXInput.PrepareNoResize(null!), "null input image");
        Reject(() => ModelArtifact.Verify([]), "empty model rejected without native load");
        Reject(() => ModelArtifact.Verify(new byte[ModelArtifact.Size]), "right size wrong hash rejected without native load");
        Check(NumericReference.Near(1, 1.00001f), "reference comparison tolerates specified error");
        Check(!NumericReference.Near(1, 1.1f), "reference comparison detects large error");
        Check(!NumericReference.Near(float.NaN, 0), "reference cannot contain NaN");
        Check(!NumericReference.Near(0, float.PositiveInfinity), "reference cannot contain infinity");
        Check(NumericReference.Compare(new float[] { 1, 2 }, new float[] { 1, 2 }) is { Count: 2, MaximumAbsoluteError: 0 }, "full numeric comparator");
        Reject(() => NumericReference.Compare(new float[] { 1 }, new float[] { 1, 2 }), "comparison length mismatch");
        Reject(() => NumericReference.Compare(Array.Empty<float>(), Array.Empty<float>()), "empty comparison not success");
        Reject(() => NumericReference.Compare(new float[] { 1 }, new float[] { 2 }), "incorrect reference fails");
        Console.WriteLine($"PASS {count} managed input/artifact/numerical-contract checks. No native session executed by this command.");
    }

    public static void Native(string path)
    {
        count = 0;
        byte[] artifact = ModelArtifact.ReadVerified(path);
        Check(artifact.Length == ModelArtifact.Size, "actual artifact hash/length accepted");
        artifact[100] ^= 1;
        Reject(() => ModelArtifact.Verify(artifact), "actual modified model rejected before load");
        YoloXInput input = YoloXInput.PrepareNoResize(YoloXInput.CreateFixture());
        Check(input.Sha256 == NumericReference.InputSha256, "actual input matches NumPy bytes");
        YoloXNanoSession session = new(path);
        YoloXRawOutput first;
        try
        {
            Check(session.RuntimeVersion == "1.27.0", "actual native runtime version1.27.0");
            Check(session.InputName == "images" && session.OutputName == "output", "actual metadata validated by constructor");
            first = session.Run(input);
            Check(first.Shape.SequenceEqual(new long[] { 1, 3549, 85 }), "actual output shape");
            Check(first.Values.Length == 301665, "actual raw float count");
            NumericReference.CheckSamples(first);
            Check(true, "20 frozen Python sample values agree within tolerance");
            Check(input.Sha256 == NumericReference.InputSha256, "actual Run leaves input unchanged");
            YoloXRawOutput second = session.Run(input);
            Check(NumericReference.Compare(first.Values, second.Values).Count == 301665, "same session reused for actual second run");
            float[] detached = first.CopyValues();
            detached[0] = 999;
            Check(first.Values[0] != 999, "managed output copy detached");
            Reject(() => session.Run(null!), "null run input rejected");
        }
        finally { session.Dispose(); }
        session.Dispose();
        Check(float.IsFinite(first.Values[0]) && NumericReference.Near(first.Values[0], 1.3127425909042358f), "managed output survives native/session disposal");
        Reject(() => session.Run(input), "Run after Dispose rejected");
        using SessionOptions options = new() { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        options.AppendExecutionProvider_CPU(1);
        using InferenceSession direct = new(ModelArtifact.ReadVerified(path), options);
        using RunOptions run = new();
        using OrtValue shortInput = OrtValue.CreateTensorValueFromMemory(new float[3], new long[] { 1, 3, 1, 1 });
        RejectOrt(() => { using var result = direct.Run(run, new Dictionary<string, OrtValue> { ["images"] = shortInput }, ["output"]); }, "actual ORT rejects wrong dimensions");
        using OrtValue validInput = OrtValue.CreateTensorValueFromMemory(input.Buffer, input.Shape.ToArray());
        RejectOrt(() => { using var result = direct.Run(run, new Dictionary<string, OrtValue> { ["wrong-name"] = validInput }, ["output"]); }, "actual ORT rejects wrong name");
        using OrtValue integerInput = OrtValue.CreateTensorValueFromMemory(new int[3 * 416 * 416], input.Shape.ToArray());
        RejectOrt(() => { using var result = direct.Run(run, new Dictionary<string, OrtValue> { ["images"] = integerInput }, ["output"]); }, "actual ORT rejects wrong dtype");
        Console.WriteLine($"PASS {count} actual CPU/ONNX checks. Synthetic pixels, raw outputs only; no decoding/NMS/detection-quality acceptance.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Check failed: {name}");
        count++;
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        { count++; return; }
        throw new InvalidOperationException($"Expected rejection: {name}");
    }
    private static void RejectOrt(Action action, string name)
    {
        try { action(); }
        catch (OnnxRuntimeException) { count++; return; }
        throw new InvalidOperationException($"Expected actual ORT rejection: {name}");
    }
}
