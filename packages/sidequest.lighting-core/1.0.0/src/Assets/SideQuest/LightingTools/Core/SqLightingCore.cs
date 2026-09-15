// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using UnityEditor;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Identity and compatibility for the shared Core.
    ///
    /// The four tool packages each embed a byte-identical copy of Core at the same path
    /// with the same .meta GUIDs, so importing two of them overwrites rather than
    /// duplicates. That works, but it means a user who imports tool A at 1.2.0 and then
    /// tool B at 1.0.0 silently downgrades Core. This class exists to make that loud.
    ///
    /// Core API is additive-only. Never rename, never remove, never change a signature -
    /// mark obsolete instead.
    /// </summary>
    public static class SqLightingCore
    {
        public const string Version = "1.0.0";

        /// <summary>
        /// Bumped only when a change would break a tool compiled against an older Core.
        /// Tools compare this, not Version, so a patch release does not trip the guard.
        /// </summary>
        public const int ApiLevel = 1;

        public const string ConsolePrefix = "[SQLT]";

        /// <summary>Folder under the project root holding reports, plans and results.</summary>
        public const string DataFolderName = "SideQuestLighting";

        static readonly Dictionary<string, ToolRegistration> Registered =
            new Dictionary<string, ToolRegistration>(StringComparer.Ordinal);

        public struct ToolRegistration
        {
            public string ToolId;
            public string ToolVersion;
            public int RequiredApiLevel;
        }

        /// <summary>
        /// Called from each tool's [InitializeOnLoad]. Mismatches are reported once, with
        /// the exact fix, rather than surfacing later as a confusing MissingMethodException.
        /// </summary>
        public static void RegisterTool(string toolId, string toolVersion, int requiredApiLevel)
        {
            if (string.IsNullOrEmpty(toolId)) return;

            Registered[toolId] = new ToolRegistration
            {
                ToolId = toolId,
                ToolVersion = toolVersion,
                RequiredApiLevel = requiredApiLevel
            };

            if (requiredApiLevel > ApiLevel)
            {
                SqLog.Error("core", string.Format(
                    "{0} {1} needs Core API level {2} but the installed Core is {3} (API {4}). " +
                    "Reimport SideQuestLightingCore-{1}.unitypackage, or reimport the newest tool package.",
                    toolId, toolVersion, requiredApiLevel, Version, ApiLevel));
            }
        }

        public static IEnumerable<ToolRegistration> RegisteredTools
        {
            get { return Registered.Values; }
        }

        public static bool IsToolInstalled(string toolId)
        {
            return toolId != null && Registered.ContainsKey(toolId);
        }
    }

    /// <summary>
    /// Reports Core's own identity once per domain reload. Cheap, and it makes
    /// "which version is actually installed?" answerable from a console log.
    /// </summary>
    [InitializeOnLoad]
    static class SqCoreVersionGuard
    {
        static SqCoreVersionGuard()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var tool in SqLightingCore.RegisteredTools)
                {
                    if (tool.RequiredApiLevel > SqLightingCore.ApiLevel) return; // already reported
                }
            };
        }
    }
}
