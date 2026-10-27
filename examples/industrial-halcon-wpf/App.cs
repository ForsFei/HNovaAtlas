using System.Windows;

namespace IndustrialHalconWpf;

public static class App
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            try { SessionChecks.Run().GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args is ["--native-test"])
        {
            try
            {
                var result = NativeFrameRunner.Analyze(IndustrialVision.PlainPgm.LoadFixture());
                if (result.Candidates.Count != 1 || result.Candidates[0] != new IndustrialHalcon.BlobFeature(4, 1.5, 1.5))
                    throw new InvalidOperationException("Native frame bridge fixture mismatch.");
                Console.WriteLine("Native frame bridge fixture passed; not visible-window or production validation."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine($"Native frame bridge not passed: {ex.GetType().Name}; no fallback or environment changes."); return 3; }
        }
        if (args.Length != 0) return 2;
        return new Application().Run(new MainWindow());
    }
}
