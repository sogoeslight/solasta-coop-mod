using System;
using System.IO;
using System.Linq;
using System.Threading;
using SolastaCoop;

public static class VerifyDiagnostics
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "solasta-diagnostic-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Log.Writer = new DiagnosticWriter(Path.Combine(root, "console"));
        try
        {
            string redirected = Path.Combine(root, "redirected-downloads");
            Check(LogLocation.ChooseDirectory(() => redirected, root) == Path.Combine(redirected, "SolastaCoopMod"),
                "Redirected Windows Downloads location is respected");
            Check(LogLocation.ChooseDirectory(() => { throw new Exception(); }, root) == Path.Combine(root, "Downloads", "SolastaCoopMod"),
                "Known-folder failure falls back to profile Downloads");
            var writer = new DiagnosticWriter(Path.Combine(root, "basic"));
            writer.Write("network", "ERROR", "token=private123; AppIdRealtime=private456; password=\"two words\"\nforged-line\rBearer private789");
            string text = File.ReadAllText(writer.FilePath("network"));
            Check(!text.Contains("private123") && !text.Contains("private456") && !text.Contains("two words") &&
                !text.Contains("private789") && text.Contains("[redacted]"), "Obvious authentication secrets are redacted");
            string header = DiagnosticWriter.Sanitize("Authorization: Bearer header-secret; access_token=query-secret");
            Check(!header.Contains("header-secret") && !header.Contains("query-secret"),
                "Authorization Bearer and underscored token fields are redacted together");
            Check(File.ReadAllLines(writer.FilePath("network")).Length == 1 && text.Contains("\\nforged-line"),
                "Multiline console messages cannot forge log entries");
            File.Delete(writer.FilePath("network"));
            Check(writer.ReadTail("network").Contains("[redacted]") && writer.ReadFailures().Contains("[ERROR]"),
                "Deleting a log does not discard in-memory history or error evidence");
            for (int i = 0; i < 400; i++) writer.Write("network", "INFO", "later-event-" + i);
            Check(!writer.ReadTail("network", 300).Contains("later-event-0 ") &&
                writer.ReadTail("network", 300).Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length == 256 &&
                writer.ReadFailures().Contains("forged-line"),
                "Session history is bounded and original errors survive informational event churn");
            Check(text.Contains(writer.Session) && Path.GetFileName(writer.FilePath("network")).Contains(BuildInfo.Version),
                "Log entries carry a session ID and filenames carry the mod version");
            writer.Write("../../escape", "INFO", "category-test");
            Check(File.Exists(writer.FilePath("core")) && Directory.GetFiles(root).Length == 0,
                "Unknown category cannot escape the logging directory");
            var rotating = new DiagnosticWriter(Path.Combine(root, "rotation"), 1024, 3);
            for (int i = 0; i < 50; i++) rotating.Write("range", "INFO", "rotation-" + i + " " + new string('x', 500));
            FileInfo[] files = new DirectoryInfo(rotating.DirectoryPath).GetFiles();
            Check(files.Length == 4 && files.All(f => f.Length <= 1024),
                "Rollover keeps current plus three bounded backup files");
            string tail = rotating.ReadTail("range", 3);
            Check(tail.Contains("rotation-49") && tail.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length <= 3,
                "Report tails retain the newest context after rollover");
            rotating.Write("range", "WARN", new string('\u20ac', 20000));
            Check(new FileInfo(rotating.FilePath("range")).Length <= 1024,
                "Oversized Unicode messages respect the byte limit");
            var concurrent = new DiagnosticWriter(Path.Combine(root, "concurrent"));
            var workers = new Thread[4];
            for (int i = 0; i < workers.Length; i++)
            {
                int worker = i;
                workers[i] = new Thread(() => { for (int j = 0; j < 20; j++) concurrent.Write("unity", "ERROR", "worker=" + worker + "; item=" + j); });
                workers[i].Start();
            }
            foreach (Thread worker in workers) worker.Join();
            string[] lines = File.ReadAllLines(concurrent.FilePath("unity"));
            Check(lines.Length == 80 && lines.All(l => l.Contains("[ERROR]") && l.Contains("worker=")),
                "Concurrent Unity callbacks do not lose or interleave log records");
            string blocked = Path.Combine(root, "blocked");
            File.WriteAllText(blocked, "occupied");
            var failing = new DiagnosticWriter(blocked);
            failing.Write("core", "INFO", "test");
            Check(failing.FailedWrites == 1 && failing.LastError.Length > 0,
                "Storage failure is reported without throwing into the game");
            Check(failing.ReadTail("core").Contains("test"), "Failed disk writes retain report evidence in memory");
            Log.UnityMessage("WARN", "Photon repeat", "");
            Log.UnityMessage("WARN", "Photon repeat", "");
            Check(Log.SuppressedUnityMessages == 1, "Repeated console messages are suppressed within the rate window");
            for (int i = 0; i < 30; i++) Log.UnityMessage("ERROR", "Unique error " + i, "");
            Check(Log.SuppressedUnityMessages >= 12, "A console error storm has a global write budget");
            Console.WriteLine("Diagnostic writer tests: PASS");
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
