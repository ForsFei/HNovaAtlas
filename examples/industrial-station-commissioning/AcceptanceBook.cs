using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace IndustrialStationCommissioning;

public enum TrialStage { Simulation, Field }
public enum TrialOutcome { NotRun, Passed, Failed }
public sealed record AcceptanceBaseline(string ApplicationRevision, string RuntimeAndSdkInventory,
    string DeviceAndConfigurationRevision, string ProtocolRevision, string RecipeRevision, string SampleAndCriteriaRevision);
public sealed record AcceptanceCase(string Id, string Subject, string Procedure, string ExpectedEvidence);
public sealed record TrialObservation(string CaseId, TrialStage Stage, TrialOutcome Outcome,
    string BaselineSha256, string Operator, DateTimeOffset ObservedUtc, string EvidenceReference);
public sealed record CaseRecord(AcceptanceCase Case, TrialObservation? Simulation, TrialObservation? Field);
public sealed record AcceptanceReport(int Schema, string Scope, AcceptanceBaseline Baseline, string BaselineSha256,
    IReadOnlyList<CaseRecord> Cases, int MissingFieldEvidence, int FailedFieldEvidence);

// A bounded record book, NEVER a machine permission or automatic acceptance decision.
public sealed class AcceptanceBook
{
    public static IReadOnlyList<AcceptanceCase> Cases { get; } = Array.AsReadOnly(new[]
    {
        new AcceptanceCase("environment", "运行环境与SDK", "核对应用提交、架构、运行时、原生DLL、驱动、固件及配置清单", "目标机清单与来源/版本/文件哈希；固定验收基线"),
        new AcceptanceCase("communication", "通信与未知写入", "读设备身份/状态；在受控台架验证写入回执丢失后的只读核对", "地址/类型/端序/超时/设备启动代，原命令关联及不重复写入日志"),
        new AcceptanceCase("motion-scale", "E3032轴映射与单位", "先核对拓扑、轴号、参数，再按现场许可验证方向、单位、反馈和到位", "型号/固件/轴映射/机械比例与测量记录；具体误差阈值见基线"),
        new AcceptanceCase("motion-stop", "运动异常与停止", "核验停止、报警、限位及观察丢失下的状态和恢复流程", "指令/反馈/报警与实际停止观察；普通停止和独立保护分开验收"),
        new AcceptanceCase("camera-correlation", "相机触发与帧关联", "核验源身份、重开代、触发到帧映射、丢帧与过期帧", "型号/接口/像素格式/Stride，触发证据、帧身份和时钟定义"),
        new AcceptanceCase("vision-quality", "实际样本与质量", "使用固定真实样本和标注，分别统计误判/漏判和处理失败", "样本清单/标注/配方或模型版本、误检漏检明细与验收阈值"),
        new AcceptanceCase("persistence", "结果保存与重启", "核验留图失败、断链/进程退出、旧周期核对与受控恢复", "原图/统计/账本、未完成材料和无自动重发/放行记录"),
        new AcceptanceCase("endurance", "节拍与长期资源", "按目标节拍和持续时长观察队列、延迟、内存、句柄与关闭", "时长/件数/分位延迟/峰值/增长趋势和失败样本；指标见基线")
    });
    private readonly object gate = new();
    private readonly AcceptanceBaseline baseline;
    private readonly Dictionary<(string, TrialStage), TrialObservation> observations = new();
    public string BaselineSha256 { get; }
    public AcceptanceBook(AcceptanceBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        foreach (string text in new[] { baseline.ApplicationRevision, baseline.RuntimeAndSdkInventory, baseline.DeviceAndConfigurationRevision,
            baseline.ProtocolRevision, baseline.RecipeRevision, baseline.SampleAndCriteriaRevision }) ValidateText(text);
        this.baseline = baseline;
        BaselineSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(baseline)));
    }
    public bool Record(TrialObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!Cases.Any(c => c.Id == observation.CaseId) || !Enum.IsDefined(observation.Stage) || !Enum.IsDefined(observation.Outcome) ||
            observation.Outcome == TrialOutcome.NotRun || observation.BaselineSha256 != BaselineSha256 || observation.ObservedUtc == default ||
            observation.ObservedUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Matching case/stage/baseline, explicit UTC and actual declared outcome required");
        ValidateText(observation.Operator); ValidateText(observation.EvidenceReference);
        lock (gate)
        {
            var key = (observation.CaseId, observation.Stage);
            if (observations.TryGetValue(key, out TrialObservation? existing))
            {
                if (existing != observation) throw new InvalidOperationException("Sealed observation conflict; use a new run book for retest");
                return false;
            }
            observations.Add(key, observation); return true;
        }
    }
    public AcceptanceReport Snapshot
    {
        get
        {
            lock (gate)
            {
                CaseRecord[] rows = Cases.Select(c => new CaseRecord(c, observations.GetValueOrDefault((c.Id, TrialStage.Simulation)),
                    observations.GetValueOrDefault((c.Id, TrialStage.Field)))).ToArray();
                return new(1, "RecordOnly-NoMachinePermission", baseline, BaselineSha256, Array.AsReadOnly(rows),
                    rows.Count(r => r.Field is null), rows.Count(r => r.Field?.Outcome == TrialOutcome.Failed));
            }
        }
    }
    private static void ValidateText(string? text)
    { if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 || text != text.Trim() || text.Any(char.IsControl)) throw new ArgumentException("Bounded nonempty text required"); }
    public string WriteNewReport(string directory)
    {
        AcceptanceReport report = Snapshot; Directory.CreateDirectory(directory);
        string pending = Path.Combine(directory, "acceptance-report.json.pending"), final = Path.Combine(directory, "acceptance-report.json");
        // Preserve a prior report and unfinished publication, never overwrite either.
        if (File.Exists(final)) throw new IOException("Report already exists; choose a new run directory");
        JsonSerializerOptions options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, options);
        if (bytes.Length > 65536) throw new InvalidOperationException("Report cap exceeded");
        using (FileStream f = new(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes); f.Flush(true); }
        File.Move(pending, final, overwrite: false); return final;
    }
}
