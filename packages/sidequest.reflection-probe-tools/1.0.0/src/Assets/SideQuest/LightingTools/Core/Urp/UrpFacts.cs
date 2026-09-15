// SideQuest Lighting Tools - MIT
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// A snapshot of the URP settings that change what this suite should recommend.
    ///
    /// Read this with the caveat it carries: in a Banter world shipped as an AssetBundle,
    /// the CLIENT's URP asset decides reflection probe blending, box projection, the light
    /// probe system and the additional-light limits - not the one in this project. Every
    /// recommendation that depends on one of these is advisory, and says so via
    /// dependsOnClientUrpAsset. The Authority field carries the same warning into the report.
    ///
    /// Several of these properties have moved between public API and serialized-only
    /// across URP versions, so each is read by reflection with a SerializedObject
    /// fallback. An unreadable value is reported as unknown, never as a default that
    /// happens to compile.
    /// </summary>
    public sealed class UrpFacts
    {
        public const string Authority = "editor-only, may differ from Banter client";

        public bool HasUrp;
        public string AssetName = "(none)";

        /// <summary>"legacy", "apv" or "unknown".</summary>
        public string LightProbeSystem = "unknown";

        public bool? ReflectionProbeBlending;
        public bool? ReflectionProbeBoxProjection;
        public bool? SupportsHdr;
        public bool? MainLightShadows;
        public int? MsaaSampleCount;
        public int? MaxAdditionalLights;

        /// <summary>"disabled", "per-vertex", "per-pixel" or "unknown".</summary>
        public string AdditionalLightsMode = "unknown";

        /// <summary>"auto", "per-vertex", "mixed", "per-pixel" or "unknown".</summary>
        public string ShEvalMode = "unknown";

        public string ColorSpace = "unknown";

        public bool UsesAdaptiveProbeVolumes
        {
            get { return string.Equals(LightProbeSystem, "apv", StringComparison.Ordinal); }
        }

        public static UrpFacts Read()
        {
            var facts = new UrpFacts();
            facts.ColorSpace = PlayerSettings.colorSpace == UnityEngine.ColorSpace.Linear ? "linear" : "gamma";

            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                // Not URP. The suite targets URP only, so callers raise URP001 and stop
                // rather than silently producing Built-in-shaped advice.
                return facts;
            }

            facts.HasUrp = true;
            facts.AssetName = asset.name;

            facts.SupportsHdr = TryGetBool(asset, "supportsHDR", "m_SupportsHDR");
            facts.MainLightShadows = TryGetBool(asset, "supportsMainLightShadows", "m_MainLightShadowsSupported");
            facts.ReflectionProbeBlending = TryGetBool(asset, "reflectionProbeBlending", "m_ReflectionProbeBlending");
            facts.ReflectionProbeBoxProjection = TryGetBool(asset, "reflectionProbeBoxProjection", "m_ReflectionProbeBoxProjection");

            facts.MsaaSampleCount = TryGetInt(asset, "msaaSampleCount", "m_MSAA");
            facts.MaxAdditionalLights = TryGetInt(asset, "maxAdditionalLightsCount", "m_AdditionalLightsPerObjectLimit");

            int? additional = TryGetInt(asset, "additionalLightsRenderingMode", "m_AdditionalLightsRenderingMode");
            facts.AdditionalLightsMode = MapLightRenderingMode(additional);

            int? sh = TryGetInt(asset, "shEvalMode", "m_ShEvalMode");
            facts.ShEvalMode = MapShEvalMode(sh);

            int? probeSystem = TryGetInt(asset, "lightProbeSystem", "m_LightProbeSystem");
            facts.LightProbeSystem = MapLightProbeSystem(probeSystem);

            return facts;
        }

        // ---- enum mapping ----
        //
        // Values are mapped by ordinal rather than by casting to the URP enum type,
        // because some of these enums are not public in every URP version. An unexpected
        // ordinal reports "unknown" rather than guessing.

        static string MapLightRenderingMode(int? v)
        {
            if (!v.HasValue) return "unknown";
            switch (v.Value)
            {
                case 0: return "disabled";
                case 1: return "per-vertex";
                case 2: return "per-pixel";
                default: return "unknown";
            }
        }

        static string MapShEvalMode(int? v)
        {
            if (!v.HasValue) return "unknown";
            switch (v.Value)
            {
                case 0: return "auto";
                case 1: return "per-vertex";
                case 2: return "mixed";
                case 3: return "per-pixel";
                default: return "unknown";
            }
        }

        static string MapLightProbeSystem(int? v)
        {
            if (!v.HasValue) return "unknown";
            switch (v.Value)
            {
                case 0: return "legacy";
                case 1: return "apv";
                default: return "unknown";
            }
        }

        // ---- resilient reads ----

        static bool? TryGetBool(UnityEngine.Object asset, string propertyName, string serializedName)
        {
            object viaProperty = TryGetViaProperty(asset, propertyName);
            if (viaProperty is bool) return (bool)viaProperty;

            SerializedProperty sp = FindSerialized(asset, serializedName, propertyName);
            if (sp != null && sp.propertyType == SerializedPropertyType.Boolean) return sp.boolValue;

            return null;
        }

        static int? TryGetInt(UnityEngine.Object asset, string propertyName, string serializedName)
        {
            object viaProperty = TryGetViaProperty(asset, propertyName);
            if (viaProperty != null)
            {
                if (viaProperty is int) return (int)viaProperty;
                if (viaProperty is Enum) return Convert.ToInt32(viaProperty);
            }

            SerializedProperty sp = FindSerialized(asset, serializedName, propertyName);
            if (sp != null)
            {
                if (sp.propertyType == SerializedPropertyType.Integer) return sp.intValue;
                if (sp.propertyType == SerializedPropertyType.Enum) return sp.enumValueIndex;
            }

            return null;
        }

        static object TryGetViaProperty(object target, string propertyName)
        {
            try
            {
                PropertyInfo pi = target.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.CanRead) return pi.GetValue(target, null);
            }
            catch (Exception e)
            {
                SqLog.Detail("urp property read failed for " + propertyName + ": " + e.Message);
            }
            return null;
        }

        static SerializedProperty FindSerialized(UnityEngine.Object asset, params string[] names)
        {
            try
            {
                var so = new SerializedObject(asset);
                for (int i = 0; i < names.Length; i++)
                {
                    SerializedProperty sp = so.FindProperty(names[i]);
                    if (sp != null) return sp;
                }
            }
            catch (Exception e)
            {
                SqLog.Detail("urp serialized read failed: " + e.Message);
            }
            return null;
        }

        public void Write(SqJsonWriter w)
        {
            w.BeginObject("pipeline");
            w.Prop("hasUrp", HasUrp);
            w.Prop("urpAssetName", AssetName);
            w.Prop("lightProbeSystem", LightProbeSystem);
            w.Prop("additionalLightsMode", AdditionalLightsMode);
            w.Prop("shEvalMode", ShEvalMode);
            w.Prop("colorSpace", ColorSpace);

            if (ReflectionProbeBlending.HasValue) w.Prop("reflectionProbeBlending", ReflectionProbeBlending.Value);
            if (ReflectionProbeBoxProjection.HasValue) w.Prop("reflectionProbeBoxProjection", ReflectionProbeBoxProjection.Value);
            if (SupportsHdr.HasValue) w.Prop("supportsHDR", SupportsHdr.Value);
            if (MainLightShadows.HasValue) w.Prop("mainLightShadows", MainLightShadows.Value);
            if (MsaaSampleCount.HasValue) w.Prop("msaa", MsaaSampleCount.Value);
            if (MaxAdditionalLights.HasValue) w.Prop("maxAdditionalLights", MaxAdditionalLights.Value);

            w.Prop("authority", Authority);
            w.EndObject();
        }
    }
}
