// SideQuest Lighting Tools - MIT
using System.Globalization;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Number formatting for anything that leaves the Editor.
    ///
    /// SqJsonWriter already writes numeric fields invariantly, but several values reach a
    /// file as pre-formatted STRINGS - validation issues recording what was given versus
    /// what was used, and the free-form stats block. Those bypass the writer entirely, and
    /// on a machine with a comma decimal separator they emit "3,99": valid text, and an
    /// invalid number to everything that reads it back.
    ///
    /// Caught in a real report on a German-locale machine, which is exactly the kind of
    /// thing that never shows up on the author's own setup.
    /// </summary>
    public static class SqFormat
    {
        public static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Num(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Vector as a JSON-shaped array. Vector3.ToString() is culture-sensitive and
        /// already uses commas as separators, so in a comma-decimal locale it produces
        /// "(1,5, 2,5, 3,5)" - ambiguous to a reader and unparseable to a tool.
        /// </summary>
        public static string Vec(Vector3 value)
        {
            return "[" + Num(value.x) + "," + Num(value.y) + "," + Num(value.z) + "]";
        }
    }
}
