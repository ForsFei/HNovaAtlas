using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IndustrialYolo.Onnx;

public sealed class YoloXRawOutput
{
    private readonly float[] values;
    public IReadOnlyList<long> Shape { get; } = Array.AsReadOnly(new long[] { 1, 3549, 85 });
    public ReadOnlySpan<float> Values => values;
    internal YoloXRawOutput(float[] ownedValues) { values = ownedValues; }
    public float[] CopyValues() => (float[])values.Clone();
}

// One caller at a time; no concurrent Run/Dispose or cancellation guarantees.
public sealed class YoloXNanoSession : IDisposable
{
    public const int OutputCount = 3549 * 85;
    private readonly InferenceSession session;
    private bool disposed;
    public string RuntimeVersion { get; }
    public string InputName => "images";
    public string OutputName => "output";

    public YoloXNanoSession(string modelPath)
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Use an x64 process.");
        byte[] bytes = ModelArtifact.ReadVerified(modelPath);
        using SessionOptions options = new()
        {
            IntraOpNumThreads = 1, InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL
        };
        options.AppendExecutionProvider_CPU(1);
        InferenceSession opened = new(bytes, options);
        try
        {
            ValidateMetadata(opened.InputMetadata, "images", [1, 3, 416, 416]);
            ValidateMetadata(opened.OutputMetadata, "output", [1, 3549, 85]);
            RuntimeVersion = OrtEnv.Instance().GetVersionString();
            session = opened;
        }
        catch { opened.Dispose(); throw; }
    }

    private static void ValidateMetadata(IReadOnlyDictionary<string, NodeMetadata> metadata, string name, int[] dimensions)
    {
        if (metadata.Count != 1 || !metadata.TryGetValue(name, out NodeMetadata? node) ||
            !node.IsTensor || node.ElementType != typeof(float) || !node.Dimensions.SequenceEqual(dimensions))
            throw new ArgumentException($"Model metadata mismatch at {name}; do not guess layout or output semantics.");
    }

    public YoloXRawOutput Run(YoloXInput input)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        using OrtValue inputValue = OrtValue.CreateTensorValueFromMemory(input.Buffer, input.Shape.ToArray());
        using RunOptions runOptions = new();
        using IDisposableReadOnlyCollection<OrtValue> outputs = session.Run(runOptions,
            new Dictionary<string, OrtValue> { [InputName] = inputValue }, [OutputName]);
        if (outputs.Count != 1) throw new InvalidOperationException("Unexpected output count.");
        OrtValue value = outputs[0];
        OrtTensorTypeAndShapeInfo info = value.GetTensorTypeAndShape();
        if (info.ElementDataType != TensorElementType.Float || !info.Shape.SequenceEqual(new long[] { 1, 3549, 85 }))
            throw new InvalidOperationException("Actual output dtype/shape disagrees with fixed contract.");
        ReadOnlySpan<float> raw = value.GetTensorDataAsSpan<float>();
        if (raw.Length != OutputCount) throw new InvalidOperationException("Unexpected raw output length.");
        foreach (float number in raw)
            if (!float.IsFinite(number)) throw new InvalidOperationException("Nonfinite model output; no fallback/empty detection.");
        return new(raw.ToArray()); // detach before the native output collection is disposed
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        session.Dispose();
    }
}
