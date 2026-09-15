// SideQuest Lighting Tools - MIT
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Resolves the on-disk locations of reports, plans, results and previews, and
    /// provides the path-safety checks the plan validator depends on.
    ///
    /// Everything lives under &lt;ProjectRoot&gt;/SideQuestLighting/, deliberately OUTSIDE
    /// Assets/: no .meta churn, no AssetDatabase import, no refresh_unity_assets round
    /// trip, and no chance of a multi-megabyte analysis report being swept into a shipped
    /// AssetBundle. It is still inside the project root, so both the MCP server and the
    /// agent's own file tools reach it without any extra configuration.
    /// </summary>
    public static class ReportPaths
    {
        public const string ReportsDir = "reports";
        public const string PlansDir = "plans";
        public const string ResultsDir = "results";
        public const string PreviewsDir = "previews";
        public const string StatusFileName = "status.json";

        /// <summary>The Unity project root - the folder containing Assets/.</summary>
        public static string ProjectRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.dataPath;
            }
        }

        public static string DataRoot
        {
            get { return Path.Combine(ProjectRoot, SqLightingCore.DataFolderName); }
        }

        public static string StatusPath
        {
            get { return Path.Combine(DataRoot, StatusFileName); }
        }

        public static string Report(string sceneName, string system)
        {
            return InDir(ReportsDir, sceneName, system, "report");
        }

        public static string Plan(string sceneName, string system)
        {
            return InDir(PlansDir, sceneName, system, "plan");
        }

        public static string Result(string sceneName, string system)
        {
            return InDir(ResultsDir, sceneName, system, "result");
        }

        public static string Preview(string sceneName, string system)
        {
            return InDir(PreviewsDir, sceneName, system, "preview");
        }

        static string InDir(string dir, string sceneName, string system, string kind)
        {
            string file = Sanitize(sceneName) + "." + Sanitize(system) + "." + kind + ".json";
            return Path.Combine(Path.Combine(DataRoot, dir), file);
        }

        /// <summary>Path relative to the project root, forward-slashed - what goes in logs and JSON.</summary>
        public static string ToProjectRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;

            string root = ProjectRoot.Replace('\\', '/').TrimEnd('/');
            string p = absolutePath.Replace('\\', '/');

            if (p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return p.Substring(root.Length + 1);
            return p;
        }

        /// <summary>Creates the data folder tree if absent. Safe to call repeatedly.</summary>
        public static void EnsureDataFolders()
        {
            EnsureFolder(DataRoot);
            EnsureFolder(Path.Combine(DataRoot, ReportsDir));
            EnsureFolder(Path.Combine(DataRoot, PlansDir));
            EnsureFolder(Path.Combine(DataRoot, ResultsDir));
            EnsureFolder(Path.Combine(DataRoot, PreviewsDir));
        }

        public static void EnsureFolder(string absolutePath)
        {
            if (!Directory.Exists(absolutePath)) Directory.CreateDirectory(absolutePath);
        }

        /// <summary>
        /// Strips characters that are illegal in a filename, plus '.' and path separators,
        /// so a scene named "../../etc" cannot escape the reports folder.
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "untitled";

            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '_' || c == '-';
                sb.Append(ok ? c : '_');
            }

            string result = sb.ToString().Trim('_');
            return result.Length == 0 ? "untitled" : result;
        }

        // ---- path safety, used by PlanValidator ----

        /// <summary>
        /// True if a plan-supplied asset path is safe to write: relative, forward-slashed,
        /// rooted at Assets/, free of traversal segments, and with a plausible extension.
        ///
        /// No plan-supplied path is ever handed to File.Delete or AssetDatabase.DeleteAsset
        /// regardless of what this returns - this gates creation only.
        /// </summary>
        public static bool IsSafeAssetPath(string path, out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(path)) { reason = "path is empty"; return false; }
            if (path.Length > 500) { reason = "path exceeds 500 characters"; return false; }

            if (path.IndexOf('\\') >= 0) { reason = "path must use forward slashes"; return false; }
            if (path.IndexOf(':') >= 0) { reason = "path must not contain a drive or scheme separator"; return false; }
            if (path.StartsWith("/")) { reason = "path must be relative"; return false; }

            foreach (char c in Path.GetInvalidPathChars())
            {
                if (path.IndexOf(c) >= 0) { reason = "path contains an invalid character"; return false; }
            }

            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                reason = "path must start with Assets/";
                return false;
            }

            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i];
                if (seg.Length == 0) { reason = "path contains an empty segment"; return false; }
                if (seg == "." || seg == "..") { reason = "path contains a traversal segment"; return false; }
                if (seg.EndsWith(" ") || seg.EndsWith(".")) { reason = "path segment ends with a space or dot"; return false; }
            }

            return true;
        }
    }
}
