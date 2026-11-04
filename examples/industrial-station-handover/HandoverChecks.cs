using System.Text.Json;
using System.Text.Json.Nodes;
namespace IndustrialStationHandover;
internal static class HandoverChecks
{
    private static int count;
    private static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); count++; }
    private static bool Fails(Action f) { try { f(); return false; } catch (Exception e) when (e is ArgumentException or IOException) { return true; } }
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "hnova-handover-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string package = HandoverPackage.CreateExample(Path.Combine(root, "good"), "offline-release-v1");
        Check(HandoverPackage.Verify(package).FilesMatchManifest, "generated example matches file inventory");
        var before = Directory.GetFiles(package).ToDictionary(p => Path.GetFileName(p)!, p => HandoverPackage.Sha(File.ReadAllBytes(p)));
        HandoverPackage.Verify(package);
        Check(Directory.GetFiles(package).All(p => before[Path.GetFileName(p)!] == HandoverPackage.Sha(File.ReadAllBytes(p))), "verify does not modify package bytes");
        Check(Fails(() => HandoverPackage.CreateExample(package, "another")), "existing package cannot be overwritten");
        Check(Fails(() => HandoverPackage.CreateExample(Path.Combine(root, "invalid"), "")), "empty release refused");
        Check(!Directory.Exists(Path.Combine(root, "invalid")), "invalid release creates no package directory");
        SiteParameters template = SiteParameters.Template("release");
        Check(template.TemplateOnly && template.Review().Any(i => i.StartsWith("Missing:")), "template leaves site values missing");
        Check(template.Motion.CardFamily == "E3032" && template.Motion.CardNumber is null && template.Motion.AxisNumber is null, "known family does not invent card/axis mapping");
        Check((template with { Motion = template.Motion with { MillimetresPerRevolution = double.NaN } }).Review().Any(i => i.Contains("MillimetresPerRevolution")), "nonfinite unit ratio flagged");
        Check((template with { Motion = template.Motion with { AxisNumber = -1 } }).Review().Any(i => i.Contains("AxisNumber")), "negative axis flagged");
        Check((template with { TemplateOnly = false }).Review().Contains("UnsupportedTemplateHeader"), "template cannot claim runtime configuration");
        Check((template with { Motion = template.Motion with { CardFamily = "pulse-card" } }).Review().Contains("MotionFamilyNeedsSeparateAdapter"), "pulse adapter cannot be inferred from E3032 template");
        string hash = new('A',64); MaintenanceRecord note = new(1,true,Guid.NewGuid(),MaintenanceKind.StartupReview,DateTimeOffset.UtcNow,"offline-test","r1",hash,hash,"fixture-only","not a real maintenance event");
        note.Validate(); Check(true,"well formed example maintenance declaration");
        Check(Fails(() => (note with { Operator = "" }).Validate()), "operator required");
        Check(Fails(() => (note with { Kind = MaintenanceKind.ParameterChange }).Validate()), "parameter change requires distinct before/after hashes");
        Check(Fails(() => (note with { BeforeConfigurationSha256 = "bad" }).Validate()), "malformed configuration hash refused");
        Check(Fails(() => (note with { Kind = (MaintenanceKind)99 }).Validate()), "unknown maintenance kind refused");
        Check(Fails(() => (note with { ObservedUtc = default }).Validate()), "maintenance time required");
        using (JsonDocument acceptance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package,"acceptance-report.json")) ))
            Check(acceptance.RootElement.GetProperty("MissingFieldEvidence").GetInt32() == 8, "handover still declares all field evidence missing");
        void Corrupt(string name, Action<string> action)
        {
            string dir = Path.Combine(root,name); Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(package)) File.Copy(f,Path.Combine(dir,Path.GetFileName(f)));
            action(dir); Check(!HandoverPackage.Verify(dir).FilesMatchManifest,name);
        }
        Corrupt("modified",d=>File.AppendAllText(Path.Combine(d,"handover.md"),"changed"));
        Corrupt("missing",d=>File.Move(Path.Combine(d,"handover.md"),Path.Combine(d,"renamed.md")));
        Corrupt("extra",d=>File.WriteAllText(Path.Combine(d,"extra.txt"),"unlisted"));
        Corrupt("pending",d=>File.WriteAllText(Path.Combine(d,"handover.md.pending"),"partial"));
        Corrupt("directory",d=>Directory.CreateDirectory(Path.Combine(d,"unexpected")));
        Corrupt("oversized",d=>{using FileStream f=new(Path.Combine(d,"handover.md"),FileMode.Open);f.SetLength(65537);});
        Corrupt("bad-schema",d=>Edit(d,n=>n["Schema"]=99));
        Corrupt("unknown-field",d=>Edit(d,n=>n["Unknown"]=1));
        Corrupt("path-traversal",d=>Edit(d,n=>n["Files"]![0]!["Name"]="../outside"));
        Corrupt("duplicate-name",d=>Edit(d,n=>n["Files"]![1]!["Name"]=n["Files"]![0]!["Name"]!.GetValue<string>()));
        Corrupt("duplicate-json",d=>{string p=Path.Combine(d,"handover-manifest.json");File.WriteAllText(p,File.ReadAllText(p).Replace("\"Schema\": 1","\"Schema\": 1, \"Schema\": 1"));});
        string missing = Path.Combine(root,"absent"); Check(!HandoverPackage.Verify(missing).FilesMatchManifest && !Directory.Exists(missing),"missing verify root remains missing");
        Console.WriteLine($"PASS {count} handover/template/maintenance/inventory checks; files retained at {root}");
    }
    private static void Edit(string d, Action<JsonNode> edit)
    { string p=Path.Combine(d,"handover-manifest.json");JsonNode n=JsonNode.Parse(File.ReadAllText(p))!;edit(n);File.WriteAllText(p,n.ToJsonString()); }
    public static void Demo()
    {
        string p=HandoverPackage.CreateExample(Path.Combine(Path.GetTempPath(),"hnova-handover-demo-"+Guid.NewGuid().ToString("N")),"offline-materials-v1");
        Console.WriteLine($"FilesMatchManifest={HandoverPackage.Verify(p).FilesMatchManifest}; template issues={SiteParameters.Template("offline-materials-v1").Review().Count}; field evidence missing=8");
        Console.WriteLine($"Teaching material only, no executable app/driver/device action. Retained at {p}");
    }
}
