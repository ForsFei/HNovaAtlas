using IndustrialStation;
using IndustrialVision;
namespace IndustrialStationEvidence;
internal static class ImageReconciliationChecks
{
    private static int count;
    private static void Check(bool ok,string why) { if(!ok) throw new InvalidOperationException(why);count++; }
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"hnova-image-inventory-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string ledger=Path.Combine(root,"ledger"),normal=Path.Combine(root,"normal"),abnormal=Path.Combine(root,"abnormal"),images=Path.Combine(root,"images");
        Guid session=Guid.NewGuid(),frame=Guid.NewGuid();WorkOrder order=new(Guid.NewGuid(),1,"offline","demo-ok-v1");
        StationTerminal terminal=new(session,1,order,CycleOutcome.Faulted,"StepFailed:Inspect",frame,null);
        AbnormalEvidence diagnostic=new(1,"DiagnosticOnly-NoPixels",terminal,AbnormalFrameState.AcceptedFrame,new(session,1,2,StepKind.Capture),frame,new FrameStamp(Guid.NewGuid(),1),null,null,"ProcessingFailed");
        using(var l=new StationLedger(ledger)){l.Begin(session,1,order);l.SaveTerminal(terminal);}
        using(var n=new EvidenceStore(normal)){}
        using(var a=new AbnormalEvidenceStore(abnormal)){a.Save(diagnostic);}
        ImageEvidenceReport Read()=>ImageEvidenceReconciler.Inspect(ledger,normal,abnormal,images);
        Check(Read().Images.Single().State==ImageEvidenceState.StorageUnavailable&&!Directory.Exists(images),"missing storage never created or mistaken for missing file");
        Directory.CreateDirectory(images);Check(Read().Images.Single().State==ImageEvidenceState.Missing,"known frame with absent archive");
        OwnedFrame owned=new(diagnostic.DiagnosticFrame!.Value,Mono8Image.FromPacked(2,2,new byte[]{1,2,3,4}));
        string archive=AbnormalImageArchive.Save(images,diagnostic,owned);byte[] original=File.ReadAllBytes(archive);
        var verified=Read();Check(verified.Images.Single().State==ImageEvidenceState.Verified,"original archive matches full diagnostic");
        Check(verified.RequiresAttention&&!verified.BaseReport.Rows.Single().ReviewRecorded,"verified image does not review or release cycle");
        Check(File.ReadAllBytes(archive).SequenceEqual(original),"read-only pixel inspection preserves bytes");
        File.WriteAllText(Path.Combine(images,"interrupted.pending"),"partial");Check(Read().ImageStorageIssues.Any(s=>s.StartsWith("Pending:")),"unfinished image material reported");
        File.WriteAllText(Path.Combine(images,"unexpected.txt"),"unlisted");Check(Read().ImageStorageIssues.Any(s=>s.StartsWith("UnexpectedEntry:")),"unknown entry reported");
        File.WriteAllText(archive,"{");Check(Read().Images.Single().State==ImageEvidenceState.Invalid,"broken JSON distinct from missing");File.WriteAllBytes(archive,original);
        var node=System.Text.Json.Nodes.JsonNode.Parse(original)!;node["Diagnostic"]!["ProcessingError"]="OtherFailure";File.WriteAllText(archive,node.ToJsonString());
        Check(Read().Images.Single().State==ImageEvidenceState.DiagnosticConflict,"valid archive with different diagnostic not verified");File.WriteAllBytes(archive,original);
        string orphan=Path.Combine(images,Guid.NewGuid().ToString("N")+"-0000000001.json");File.WriteAllBytes(orphan,original);
        Check(Read().Images.Any(r=>r.State==ImageEvidenceState.Orphan),"orphan never becomes original cycle result");
        Check(Read().Images.Single(r=>r.Session==session).State==ImageEvidenceState.Verified,"bad unrelated file does not hide valid original row");
        string intentOnly=Path.Combine(root,"intent-only");
        using(var l=new StationLedger(intentOnly)){l.Begin(session,1,order);}
        Check(ImageEvidenceReconciler.Inspect(intentOnly,normal,abnormal,images).Images.Single(r=>r.Session==session).State==ImageEvidenceState.BaseNotMatched,"image cannot manufacture missing original terminal");
        string fresh=Path.Combine(root,"fresh");Directory.CreateDirectory(fresh);
        Check(ImageEvidenceReconciler.Inspect(Path.Combine(root,"absent-ledger"),normal,abnormal,fresh).RequiresAttention,"base storage failure retains attention");
        Check(!Directory.Exists(Path.Combine(root,"absent-ledger")),"inspection never creates missing ledger");
        // Unreviewed abnormal with no captured frame: absence is intentional, not an image failure.
        string noFrameLedger=Path.Combine(root,"no-frame-ledger"),noFrameNotes=Path.Combine(root,"no-frame-notes");Guid other=Guid.NewGuid();
        var stopped=terminal with {Session=other,Outcome=CycleOutcome.Stopped,FrameId=null};
        using(var l=new StationLedger(noFrameLedger)){l.Begin(other,1,order);l.SaveTerminal(stopped);}
        using(var a=new AbnormalEvidenceStore(noFrameNotes)){a.Save(new(1,"DiagnosticOnly-NoPixels",stopped,AbnormalFrameState.NoQualifiedFrame,null,null,null,null,null,null));}
        Check(ImageEvidenceReconciler.Inspect(noFrameLedger,normal,noFrameNotes,fresh).Images.Single().State==ImageEvidenceState.NoFrameExpected,"no qualified frame classified separately");
        Console.WriteLine($"PASS {count} image inventory/classification/read-only checks; files retained at {root}");
    }
}
