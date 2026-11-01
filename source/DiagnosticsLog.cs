using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SolastaCoop
{
    internal static class BuildInfo
    {
        internal const string Version = "0.1.10";
    }

    internal sealed class DiagnosticWriter
    {
        private readonly object sync = new object();
        private readonly long maximumBytes;
        private readonly int backups;
        private readonly Dictionary<string, Queue<string>> sessionHistory = new Dictionary<string, Queue<string>>();
        private readonly Queue<string> failures = new Queue<string>();
        internal readonly string DirectoryPath;
        internal readonly string Session = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        internal string LastError { get; private set; }
        internal long FailedWrites { get; private set; }
        internal static readonly string[] Categories = { "core", "network", "range", "unity" };

        internal DiagnosticWriter(string directory, long maximumBytes = 1048576, int backups = 3)
        {
            if (maximumBytes < 1024 || backups < 1 || backups > 10) throw new ArgumentOutOfRangeException();
            DirectoryPath = directory;
            this.maximumBytes = maximumBytes;
            this.backups = backups;
            LastError = "";
        }

        internal string FilePath(string category, int backup = 0)
        {
            if (Array.IndexOf(Categories, category) < 0) category = "core";
            string name = category == "core" ? "coop-view" : category;
            return Path.Combine(DirectoryPath, name + "-" + BuildInfo.Version +
                (backup == 0 ? "" : "." + backup.ToString(CultureInfo.InvariantCulture)) + ".log");
        }

        internal static string Sanitize(string text)
        {
            string value = text ?? "";
            value = Regex.Replace(value, @"(?i)\bBearer\s+[^\s,;]+", "Bearer [redacted]");
            value = Regex.Replace(value,
                @"(?i)\b(appid\w*|token|(?:auth|access|refresh)[_-]?token|password|secret|userid|authorization)\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;]+)",
                "$1=[redacted]");
            value = Regex.Replace(value, @"(?i)[a-z]:\\Users\\[^\\\r\n]+", "%USERPROFILE%");
            value = Regex.Replace(value, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "?");
            return value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        internal void Write(string category, string level, string message)
        {
            lock (sync)
            {
                if (Array.IndexOf(Categories, category) < 0) category = "core";
                string safe = Sanitize(message);
                int limit = (int)Math.Min(8192, maximumBytes / 4 - 128);
                if (safe.Length > limit) safe = safe.Substring(0, limit) + " [truncated]";
                string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " [" + Session +
                    "] [" + level + "] " + safe + Environment.NewLine;
                Queue<string> history;
                if (!sessionHistory.TryGetValue(category, out history))
                    sessionHistory[category] = history = new Queue<string>();
                if (history.Count == 256) history.Dequeue();
                history.Enqueue(line.TrimEnd('\r', '\n'));
                if (level == "ERROR")
                {
                    if (failures.Count == 32) failures.Dequeue();
                    failures.Enqueue(line.TrimEnd('\r', '\n'));
                }
                try
                {
                    Directory.CreateDirectory(DirectoryPath);
                    string path = FilePath(category);
                    if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(line) > maximumBytes)
                    {
                        string oldest = FilePath(category, backups);
                        if (File.Exists(oldest)) File.Delete(oldest);
                        for (int i = backups - 1; i >= 1; i--)
                            if (File.Exists(FilePath(category, i))) File.Move(FilePath(category, i), FilePath(category, i + 1));
                        File.Move(path, FilePath(category, 1));
                    }
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    FailedWrites++;
                    LastError = e.GetType().Name + ": " + Sanitize(e.Message);
                }
            }
        }

        internal string ReadTail(string category, int lines = 150)
        {
            lock (sync)
            {
                if (lines < 1) return "";
                Queue<string> history;
                if (sessionHistory.TryGetValue(category, out history))
                {
                    string[] entries = history.ToArray();
                    return String.Join(Environment.NewLine, entries, Math.Max(0, entries.Length - lines), Math.Min(lines, entries.Length));
                }
                var recent = new Queue<string>();
                // Include the newest rotated file so a report made just after rotation retains context.
                foreach (string path in new[] { FilePath(category, 1), FilePath(category) })
                {
                    if (!File.Exists(path)) continue;
                    using (var reader = new StreamReader(path, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (recent.Count == lines) recent.Dequeue();
                            recent.Enqueue(line);
                        }
                    }
                }
                return String.Join(Environment.NewLine, recent.ToArray());
            }
        }

        internal string ReadFailures()
        {
            lock (sync) return String.Join(Environment.NewLine, failures.ToArray());
        }
    }

    internal static class Log
    {
        internal static DiagnosticWriter Writer = new DiagnosticWriter(LogLocation.DirectoryPath);
        internal static string DirectoryPath { get { return Writer.DirectoryPath; } }
        internal static volatile bool Verbose = false;
        private static readonly object unitySync = new object();
        private static readonly HashSet<string> unitySeen = new HashSet<string>();
        private static DateTime unityWindow = DateTime.UtcNow;
        private static int unityCount, unityDropped;
        internal static long SuppressedUnityMessages;

        internal static void Write(string message) { Event("core", "INFO", message); }
        internal static void Event(string category, string level, string message)
        {
            if (level == "DEBUG" && !Verbose) return;
            Writer.Write(category, level, message);
        }
        internal static void Error(string category, string context, Exception error)
        {
            Event(category, "ERROR", context + ": " + error);
        }
        internal static void UnityMessage(string level, string condition, string stack)
        {
            lock (unitySync)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - unityWindow).TotalSeconds >= 10)
                {
                    if (unityDropped > 0) Event("unity", "WARN", "Suppressed repeated/excess console messages: " + unityDropped);
                    unityWindow = now; unityCount = 0; unityDropped = 0; unitySeen.Clear();
                }
                string key = level + ":" + (condition ?? "");
                if (key.Length > 1024) key = key.Substring(0, 1024);
                if (unityCount >= 20 || !unitySeen.Add(key))
                {
                    unityDropped++; SuppressedUnityMessages++; return;
                }
                unityCount++;
                Event("unity", level, condition + (String.IsNullOrEmpty(stack) ? "" : " | stack=" + stack));
            }
        }
    }
}
