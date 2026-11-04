using System.Diagnostics;
using System.Runtime.InteropServices;
using IndustrialYolo.Images;
using IndustrialYolo.Onnx;
using IndustrialYolo.Review;
using OpenCvSharp;

namespace IndustrialYolo.Deployment;

public sealed record ProbeStage(string Name, string Status, string Detail);
public sealed record NativeOrigin(string Name, string RelativePath);
public sealed record SmokeEvidence(string InputSha256, int OutputValues, int FrozenReferenceSamples,
    int DecodedRows, int Kept, string OrtVersion, string OpenCvVersion, string SettingsVersion);
public sealed record DeploymentReport(string ReportVersion, DateTimeOffset StartedUtc, string Status,
    string ProfilePath, string? ProfileSha256, string? ReleaseId, string Framework, string Os, string ProcessArchitecture,
    string AppBase, string WorkingDirectory, bool NativeRequested, IReadOnlyList<ProbeStage> Stages, SmokeEvidence? Smoke, IReadOnlyList<NativeOrigin> Origins);

public static class DeploymentProbe
{
    public static DeploymentReport Run(string profilePath, bool native)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        profilePath = Path.GetFullPath(profilePath); string root = Path.GetDirectoryName(profilePath)!;
        List<ProbeStage> stages = []; List<NativeOrigin> origins = [];
        ReleaseProfile? profile = null; DeploymentSettings? settings = null; SmokeEvidence? smoke = null;
        string? profileHash = null; bool failed = false;
        void Check(string name, Action check, bool enabled = true)
        {
            if (failed || !enabled) { stages.Add(new(name, "NotRun", failed ? "Earlier stage failed." : "Native probing not requested.")); return; }
            try { check(); stages.Add(new(name, "Passed", "Completed.")); }
            catch (Exception error) { failed = true; stages.Add(new(name, "Failed", $"{error.GetType().Name}: {error.Message}")); }
        }
        Check("Environment", () => ReleaseInventory.Require(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
            Environment.Version.Major == 10, "This release probe supports Windows x64 / .NET10 only."));
        Check("Profile", () => { profile = ReleaseInventory.ReadProfile(profilePath, out string hash); profileHash = hash; });
        Check("Inventory", () => ReleaseInventory.Verify(root, profile!));
        Check("Settings", () => settings = ReleaseInventory.ReadSettings(root, profile!.SettingsPath,
            profile.Files.Single(f => f.Path == profile.SettingsPath).Sha256));
        Check("Model", () => _ = ModelArtifact.ReadVerified(ReviewData.Resolve(root, profile!.ModelPath)));
        Check("ExecutionRoot", () => ReleaseInventory.Require(string.Equals(Path.TrimEndingDirectorySeparator(root),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)), StringComparison.OrdinalIgnoreCase),
            "Launch the probe executable FROM the sealed release directory; inspecting another directory is not a native probe."), native);
        Check("CpuPipeline", () => smoke = Smoke(root, profile!, settings!), native);
        Check("NativeOrigins", () =>
        {
            using Process process = Process.GetCurrentProcess();
            foreach (string name in new[] { "onnxruntime.dll", "OpenCvSharpExtern.dll" })
            {
                ProcessModule[] modules = process.Modules.Cast<ProcessModule>().Where(m => string.Equals(m.ModuleName, name, StringComparison.OrdinalIgnoreCase)).ToArray();
                ReleaseInventory.Require(modules.Length == 1, $"Expected one loaded {name}.");
                string full = Path.GetFullPath(modules[0].FileName);
                Artifact file = profile!.Files.Single(f => Path.GetFileName(f.Path).Equals(name, StringComparison.OrdinalIgnoreCase));
                ReleaseInventory.Require(string.Equals(full, Path.GetFullPath(Path.Combine(root, file.Path)), StringComparison.OrdinalIgnoreCase), $"Native library loaded outside inventoried path: {name}");
                origins.Add(new(name, file.Path));
            }
        }, native);
        return new("atlas-yolo-deployment-report-v1", started, failed ? "Failed" : native ? "ProbePassed" : "InventoryPassed",
            profilePath, profileHash, profile?.ReleaseId, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), AppContext.BaseDirectory, Environment.CurrentDirectory, native, Array.AsReadOnly(stages.ToArray()), smoke, Array.AsReadOnly(origins.ToArray()));
    }
    private static SmokeEvidence Smoke(string root, ReleaseProfile profile, DeploymentSettings settings)
    {
        using Mat mat = OpenCvImageInput.ToMat(YoloXInput.CreateFixture());
        if (!Cv2.ImEncode(".png", mat, out byte[] encoded)) throw new InvalidOperationException("Smoke PNG encoding failed.");
        PreparedImage prepared = OpenCvImageInput.Prepare(OpenCvImageInput.Decode(encoded, "owned-smoke"));
        ReleaseInventory.Require(prepared.Tensor.Sha256 == NumericReference.InputSha256, "Entire BGR input fingerprint differs from frozen fixture.");
        using YoloXNanoSession session = new(ReviewData.Resolve(root, profile.ModelPath));
        ReleaseInventory.Require(session.RuntimeVersion == "1.27.0" && Cv2.GetVersionString() == "4.13.0", "Runtime versions differ from validated contract.");
        YoloXRawOutput raw = session.Run(prepared.Tensor); NumericReference.CheckSamples(raw);
        ImageReport result = ImagePostprocessor.Process(raw, prepared, new(settings.ScoreThreshold, settings.NmsThreshold));
        return new(prepared.Tensor.Sha256, raw.Values.Length, 20, result.Rows.Count, result.Detections.Count,
            session.RuntimeVersion, Cv2.GetVersionString()!, settings.SettingsVersion);
    }
    public static void WriteReport(DeploymentReport report, string destination)
    {
        destination = Path.GetFullPath(destination); string root = Path.GetDirectoryName(report.ProfilePath)!;
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        ReleaseInventory.Require(!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "Write diagnostic reports OUTSIDE the sealed release directory.");
        ReleaseInventory.WriteNew(report, destination);
    }
}
