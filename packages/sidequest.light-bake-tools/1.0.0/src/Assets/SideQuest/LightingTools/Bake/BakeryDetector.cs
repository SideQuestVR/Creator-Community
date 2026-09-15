// SideQuest Lighting Tools - MIT
using System;
using SideQuest.LightingTools.Core;

namespace SideQuest.LightingTools.Bake
{
    /// <summary>
    /// Notices whether the Bakery GPU lightmapper is installed, and stops there.
    ///
    /// Detection is worth doing: if Bakery is driving lighting in a project, this tool
    /// writing Unity LightingSettings and starting a Unity bake will fight it, and the
    /// author deserves to be told rather than to discover two sets of lightmaps.
    ///
    /// Driving Bakery is not worth doing. Its API is not a stable public contract, it
    /// changes between versions, and it is commercial - so a tool published to a community
    /// directory cannot carry any of its code or depend on a particular shape of it.
    /// Reflection to ask "is it here?" is safe; reflection to operate it is a support
    /// burden with no ceiling.
    ///
    /// So when Bakery is present the useful half still applies. Lightmap UV validation,
    /// atlas budgeting, scale-in-lightmap tuning and probe placement all feed Bakery
    /// exactly as they feed Unity's lightmapper; only the settings asset and the bake
    /// itself are left alone.
    /// </summary>
    public static class BakeryDetector
    {
        public const string CodeBakeryPresent = "LB040_BAKERY_PRESENT";

        static bool _checked;
        static bool _present;

        public static bool IsPresent
        {
            get
            {
                if (_checked) return _present;

                _present = FindBakeryType() != null;
                _checked = true;
                return _present;
            }
        }

        static Type FindBakeryType()
        {
            // Bakery's editor entry point. Looked up across loaded assemblies rather than
            // by a fixed assembly-qualified name, because the assembly it lands in has
            // differed between Bakery releases and between asset-store and package installs.
            try
            {
                System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type type = assemblies[i].GetType("ftRenderLightmap", false);
                    if (type != null) return type;
                }
            }
            catch (Exception e)
            {
                SqLog.Detail("bakery probe failed: " + e.Message);
            }

            return null;
        }

        public static void Report(SqProblemList problems)
        {
            if (problems == null || !IsPresent) return;

            problems.Add(CodeBakeryPresent, SqSeverity.Info,
                "Bakery is installed in this project. This tool will not drive Bakery or change its settings, and running Unity's own bake alongside it will produce a second, conflicting set of lightmaps.")
                .WithAction("Use Analyze for UV validation, atlas budgeting and scale tuning - all of which Bakery consumes too - and bake with Bakery. Untick Apply Lighting Settings so this tool leaves Unity's bake configuration alone.");
        }
    }
}
