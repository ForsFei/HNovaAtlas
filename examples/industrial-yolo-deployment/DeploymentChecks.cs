using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IndustrialYolo.Onnx;
using IndustrialYolo.Review;

namespace IndustrialYolo.Deployment;

internal static class DeploymentChecks
{
    private static int count;
    public static void Run()
    {
        count = 0;
        string root = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "verification", Guid.NewGuid().ToString("N"))).FullName;
        string file = Path.Combine(root, "artifact.txt"); File.WriteAllText(file, "owned-release-file", new UTF8Encoding(false));
        IReadOnlyList<Artifact> scan = ReleaseInventory.Scan(root);
        Check(scan.Count == 1 && scan[0].Path == "artifact.txt", "relative artifact scan");
        Check(scan[0].Sha256 == ReviewData.Sha(Encoding.UTF8.GetBytes("owned-release-file")), "stream hash matches owned bytes");
        Check(scan[0].Bytes == new FileInfo(file).Length, "byte length captured");
        File.WriteAllText(Path.Combine(root, ReleaseInventory.ProfileName), "excluded-profile");
        Check(ReleaseInventory.Scan(root).Count == 1, "profile itself excluded from inventory");
        foreach (string invalidPath in new[] { "../x", "/absolute", "C:/x", "sub\\x", ".. /x", "a/./b" })
            Check(!ReleaseInventory.ValidRelative(invalidPath), "unsafe relative profile path");
        Check(ReleaseInventory.ValidRelative("runtimes/win-x64/native/onnxruntime.dll"), "nested asset path");
        DeploymentSettings settings = new("atlas-yolo-deployment-settings-v1", "v1", ReviewData.Pipeline, ReviewData.LabelSet, .1f, .45);
        string settingsFile = Path.Combine(root, ReleaseInventory.SettingsPath);
        File.WriteAllBytes(settingsFile, JsonSerializer.SerializeToUtf8Bytes(settings, ReleaseInventory.Json));
        Check(ReleaseInventory.ReadSettings(root, ReleaseInventory.SettingsPath) == settings, "fixed settings read");
        File.WriteAllBytes(settingsFile, JsonSerializer.SerializeToUtf8Bytes(settings with { LabelSet = "scratch-dent" }, ReleaseInventory.Json));
        Reject(() => ReleaseInventory.ReadSettings(root, ReleaseInventory.SettingsPath), "wrong label set");
        File.WriteAllBytes(settingsFile, JsonSerializer.SerializeToUtf8Bytes(settings with { NmsThreshold = 2 }, ReleaseInventory.Json));
        Reject(() => ReleaseInventory.ReadSettings(root, ReleaseInventory.SettingsPath), "invalid threshold");
        string[] names = ["IndustrialYolo.Deployment.exe", "IndustrialYolo.Deployment.dll", "IndustrialYolo.Deployment.deps.json",
            "IndustrialYolo.Deployment.runtimeconfig.json", ReleaseInventory.SettingsPath, ReleaseInventory.ModelPath, "onnxruntime.dll", "OpenCvSharpExtern.dll"];
        Artifact[] metadata = names.Select(n => new Artifact(n, n == ReleaseInventory.ModelPath ? ModelArtifact.Size : 1,
            n == ReleaseInventory.ModelPath ? ModelArtifact.Sha256 : new string('0', 64))).ToArray();
        ReleaseProfile profile = new("atlas-yolo-release-v1", "metadata-only", "win-x64", "framework-dependent-folder",
            ReleaseInventory.ModelPath, ModelArtifact.Sha256, ReleaseInventory.SettingsPath, metadata);
        ReleaseInventory.Validate(profile); Check(true, "metadata validation, not actual native artifacts");
        Reject(() => ReleaseInventory.Validate(profile with { Files = metadata[..^1] }), "missing native entry");
        Reject(() => ReleaseInventory.Validate(profile with { Files = metadata.Concat([metadata[0]]).ToArray() }), "duplicate path");
        Reject(() => ReleaseInventory.Validate(profile with { Rid = "win-x86" }), "wrong RID");
        Reject(() => ReleaseInventory.Validate(profile with { ModelSha256 = new('0', 64) }), "wrong model hash");
        Reject(() => ReleaseInventory.Verify(root, profile), "metadata cannot certify different real files");
        DeploymentReport invalid = DeploymentProbe.Run(Path.Combine(root, ReleaseInventory.ProfileName), native: false);
        Check(invalid.Status == "Failed" && invalid.Stages.Any(s => s.Name == "Profile" && s.Status == "Failed"), "bad profile diagnostic stage");
        Check(invalid.Stages.Where(s => s.Name is "Inventory" or "CpuPipeline").All(s => s.Status == "NotRun"), "no later probes after failure");
        string report = Path.Combine(Path.GetDirectoryName(root)!, $"report-{Guid.NewGuid():N}.json");
        DeploymentProbe.WriteReport(invalid, report);
        Check(File.Exists(report), "failure diagnostic can be published outside release");
        Reject(() => DeploymentProbe.WriteReport(invalid, report), "no overwrite report");
        Reject(() => DeploymentProbe.WriteReport(invalid, Path.Combine(root, "diagnostic.json")), "no dirtying sealed folder");
        using Process process = Process.GetCurrentProcess();
        Check(!process.Modules.Cast<ProcessModule>().Any(m => m.ModuleName is "onnxruntime.dll" or "OpenCvSharpExtern.dll"), "self-test never loads native engines");
        Console.WriteLine($"PASS {count} inventory/settings/diagnostic checks. Owned dummy artifacts; no codec, model or publish acceptance.");
    }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException($"Deployment check failed: {name}"); count++; }
    private static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or IOException or JsonException or InvalidOperationException) { count++; return; }
        throw new InvalidOperationException($"Expected deployment rejection: {name}");
    }
}
