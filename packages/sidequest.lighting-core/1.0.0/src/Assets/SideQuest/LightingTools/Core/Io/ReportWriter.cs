// SideQuest Lighting Tools - MIT
using System;
using System.IO;
using System.Text;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// File IO for the JSON handshake. Writes are atomic and reads are bounded.
    ///
    /// Atomic matters because the agent polls these files while the Editor writes them:
    /// a torn read of a half-written report looks exactly like a malformed report, and
    /// debugging that distinction wastes far more time than a temp file costs.
    /// </summary>
    public static class ReportWriter
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Writes text to a sibling temp file, then replaces the target. Returns the byte
        /// length written.
        /// </summary>
        public static int WriteAtomic(string absolutePath, string content)
        {
            string dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir)) ReportPaths.EnsureFolder(dir);

            string temp = absolutePath + ".tmp";
            byte[] bytes = Utf8NoBom.GetBytes(content ?? string.Empty);

            File.WriteAllBytes(temp, bytes);

            // File.Replace needs an existing destination; Move refuses one. Handle both.
            if (File.Exists(absolutePath))
            {
                try
                {
                    File.Replace(temp, absolutePath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(absolutePath);
                    File.Move(temp, absolutePath);
                }
                catch (IOException)
                {
                    File.Delete(absolutePath);
                    File.Move(temp, absolutePath);
                }
            }
            else
            {
                File.Move(temp, absolutePath);
            }

            return bytes.Length;
        }

        /// <summary>
        /// Reads a file, refusing anything over <paramref name="maxBytes"/> without loading
        /// it. A 40 MB plan is rejected by its size on disk, not after it has been read
        /// into memory and parsed.
        /// </summary>
        public static bool TryReadBounded(string absolutePath, int maxBytes, out string content, out string error)
        {
            content = null;
            error = null;

            try
            {
                if (!File.Exists(absolutePath))
                {
                    error = "file not found: " + ReportPaths.ToProjectRelative(absolutePath);
                    return false;
                }

                var info = new FileInfo(absolutePath);
                if (info.Length > maxBytes)
                {
                    error = string.Format("file is {0} bytes, limit is {1}", info.Length, maxBytes);
                    return false;
                }

                content = File.ReadAllText(absolutePath, Utf8NoBom);
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>UTC timestamp in the one format every file in the suite uses.</summary>
        public static string UtcNow()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
