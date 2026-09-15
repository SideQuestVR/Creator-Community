// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// The console is the suite's second control channel.
    ///
    /// execute_editor_menu_item can invoke a parameterless static but cannot return
    /// anything, and it cannot tell you whether the menu item existed or threw. So every
    /// action emits exactly one machine-readable line, which get_console_logs can match:
    ///
    ///   [SQLT] reflection analyze ok report=SideQuestLighting/reports/Main.json zones=8
    ///   [SQLT] reflection apply error code=E_STALE_PLAN msg="plan targets an older report"
    ///
    /// One line per action, always. Extra diagnostics go to SqLog.Detail, which is
    /// off by default so the machine-readable line is never buried.
    /// </summary>
    public static class SqLog
    {
        public static bool VerboseDetail;

        public static void Ok(string tool, string action, params string[] keyValuePairs)
        {
            Debug.Log(Format(tool, action, "ok", keyValuePairs));
        }

        public static void Warn(string tool, string action, params string[] keyValuePairs)
        {
            Debug.LogWarning(Format(tool, action, "warn", keyValuePairs));
        }

        /// <summary>Failure with a stable machine-readable code (E_STALE_PLAN, E_BUDGET, ...).</summary>
        public static void Fail(string tool, string action, string code, string message)
        {
            Debug.LogError(Format(tool, action, "error", new[] { "code", code, "msg", message }));
        }

        public static void Error(string tool, string message)
        {
            Debug.LogError(SqLightingCore.ConsolePrefix + " " + tool + " error msg=" + Quote(message));
        }

        /// <summary>Human-facing extra detail. Never parsed; silent unless VerboseDetail.</summary>
        public static void Detail(string message)
        {
            if (VerboseDetail) Debug.Log(SqLightingCore.ConsolePrefix + " detail " + message);
        }

        static string Format(string tool, string action, string status, IList<string> keyValuePairs)
        {
            var sb = new StringBuilder(128);
            sb.Append(SqLightingCore.ConsolePrefix).Append(' ');
            sb.Append(tool).Append(' ');
            sb.Append(action).Append(' ');
            sb.Append(status);

            if (keyValuePairs != null)
            {
                for (int i = 0; i + 1 < keyValuePairs.Count; i += 2)
                {
                    sb.Append(' ').Append(keyValuePairs[i]).Append('=');
                    sb.Append(Quote(keyValuePairs[i + 1]));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Values are quoted only when they need it, so the common case stays terse and
        /// greppable. Newlines are flattened - a multi-line value would break the
        /// one-line-per-action contract the log reader depends on.
        /// </summary>
        static string Quote(string value)
        {
            if (value == null) return "\"\"";

            value = value.Replace('\n', ' ').Replace('\r', ' ');

            bool needsQuotes = value.Length == 0;
            for (int i = 0; i < value.Length && !needsQuotes; i++)
            {
                char c = value[i];
                if (c == ' ' || c == '"' || c == '=') needsQuotes = true;
            }

            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "'") + "\"";
        }
    }
}
