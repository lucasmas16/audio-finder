using System;
using System.IO;
using System.Threading;

namespace AudioFinder
{
    // Local, bounded operational trace. Never record device IDs, names, paths or exception messages.
    internal static class ConnectionTrace
    {
        internal const string Build = "native-feedback-20261009-1";
        internal static bool Enabled;
        private static readonly object Gate = new object();
        internal static void Write(string stage)
        {
            if (!Enabled) return;
            try
            {
                lock (Gate)
                {
                    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFinder");
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, "connection-diagnostic.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 262144) File.WriteAllText(path, "");
                    File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " thread=" + Thread.CurrentThread.ManagedThreadId + " " + stage + Environment.NewLine);
                }
            }
            catch { /* Diagnostics cannot interfere with output selection. */ }
        }
    }
}
