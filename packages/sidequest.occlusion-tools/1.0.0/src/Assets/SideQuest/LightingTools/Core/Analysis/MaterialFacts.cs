// SideQuest Lighting Tools - MIT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// What a URP material tells us about how it wants to be lit and reflected.
    ///
    /// This is the input to reflection probe placement, and getting it wrong is expensive
    /// in both directions: a missed glossy surface means no probe where one was needed,
    /// and a false positive spends cubemap memory on a matte wall.
    ///
    /// The rule throughout: never silently guess. A material whose gloss cannot be read
    /// gets a neutral prior, a reduced confidence weight, and an entry in problems[] so a
    /// human can supply the answer once via SqSettings.shaderGlossOverrides.
    /// </summary>
    public sealed class MaterialFacts
    {
        public string ShaderName = "(none)";

        /// <summary>lit, simple-lit, complex-lit, unlit, shadergraph, unknown.</summary>
        public string Family = "unknown";

        public float Smoothness = 0.5f;
        public float Metallic;
        public float ClearCoat;

        /// <summary>opaque, cutout or transparent.</summary>
        public string SurfaceType = "opaque";

        public bool DoubleSided;

        /// <summary>False when _EnvironmentReflections is off - such a material ignores probes entirely.</summary>
        public bool UsesEnvironmentReflections = true;

        /// <summary>
        /// False for shaders that never sample a reflection probe at all, Simple Lit and
        /// Unlit being the ones that matter. Placing a probe for these is pure waste.
        /// </summary>
        public bool SamplesReflectionProbes = true;

        /// <summary>lit, gloss-map, graph-property, override or unknown.</summary>
        public string GlossSource = "unknown";

        /// <summary>1.0 when read directly; 0.25 for an unknown shader's neutral prior.</summary>
        public float Confidence = 1f;

        public bool IsOpaque { get { return string.Equals(SurfaceType, "opaque", StringComparison.Ordinal); } }
        public bool IsTransparent { get { return string.Equals(SurfaceType, "transparent", StringComparison.Ordinal); } }

        // URP Lit property ids, resolved once.
        static readonly int IdSmoothness = Shader.PropertyToID("_Smoothness");
        static readonly int IdGlossiness = Shader.PropertyToID("_Glossiness");
        static readonly int IdRoughness = Shader.PropertyToID("_Roughness");
        static readonly int IdMetallic = Shader.PropertyToID("_Metallic");
        static readonly int IdSurface = Shader.PropertyToID("_Surface");
        static readonly int IdBlend = Shader.PropertyToID("_Blend");
        static readonly int IdAlphaClip = Shader.PropertyToID("_AlphaClip");
        static readonly int IdCull = Shader.PropertyToID("_Cull");
        static readonly int IdEnvReflections = Shader.PropertyToID("_EnvironmentReflections");
        static readonly int IdMetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        static readonly int IdSpecGlossMap = Shader.PropertyToID("_SpecGlossMap");
        static readonly int IdClearCoatMask = Shader.PropertyToID("_ClearCoatMask");
        static readonly int IdClearCoatSmoothness = Shader.PropertyToID("_ClearCoatSmoothness");

        static readonly Dictionary<int, MaterialFacts> Cache = new Dictionary<int, MaterialFacts>();

        /// <summary>Clears the per-material cache. Call at the start of each scan.</summary>
        public static void ResetCache() { Cache.Clear(); }

        public static MaterialFacts Read(Material material)
        {
            if (material == null) return new MaterialFacts { ShaderName = "(missing)", Family = "unknown", Confidence = 0f };

            MaterialFacts cached;
            int key = material.GetInstanceID();
            if (Cache.TryGetValue(key, out cached)) return cached;

            MaterialFacts facts = ReadUncached(material);
            Cache[key] = facts;
            return facts;
        }

        static MaterialFacts ReadUncached(Material material)
        {
            var f = new MaterialFacts();
            Shader shader = material.shader;
            f.ShaderName = shader != null ? shader.name : "(missing shader)";
            f.Family = ClassifyFamily(shader);

            // A human-supplied override always wins. This is the escape hatch for custom
            // shaders, and it must not be second-guessed by the heuristics below.
            SqSettings.ShaderGlossOverride ov;
            if (SqSettings.instance.TryGetGlossOverride(f.ShaderName, out ov))
            {
                f.Smoothness = Mathf.Clamp01(ov.smoothness);
                f.Metallic = Mathf.Clamp01(ov.metallic);
                f.UsesEnvironmentReflections = ov.usesEnvironmentReflections;
                f.SamplesReflectionProbes = ov.usesEnvironmentReflections;
                f.GlossSource = "override";
                f.Confidence = 1f;
                ReadSurface(material, f);
                return f;
            }

            switch (f.Family)
            {
                case "unlit":
                    f.Smoothness = 0f;
                    f.Metallic = 0f;
                    f.SamplesReflectionProbes = false;
                    f.UsesEnvironmentReflections = false;
                    f.GlossSource = "lit";
                    f.Confidence = 1f;
                    break;

                case "simple-lit":
                    // Blinn-Phong. Has a smoothness slider, but never samples a reflection
                    // probe - a creator expecting reflections here will never get them.
                    f.Smoothness = ReadFloat(material, IdSmoothness, ReadFloat(material, IdGlossiness, 0.5f));
                    f.Metallic = 0f;
                    f.SamplesReflectionProbes = false;
                    f.UsesEnvironmentReflections = false;
                    f.GlossSource = "lit";
                    f.Confidence = 1f;
                    break;

                case "lit":
                case "complex-lit":
                    ReadLit(material, f);
                    break;

                default:
                    ReadUnknown(material, f);
                    break;
            }

            ReadSurface(material, f);
            return f;
        }

        static void ReadLit(Material material, MaterialFacts f)
        {
            f.Smoothness = Mathf.Clamp01(ReadFloat(material, IdSmoothness, 0.5f));
            f.Metallic = Mathf.Clamp01(ReadFloat(material, IdMetallic, 0f));
            f.GlossSource = "lit";
            f.Confidence = 1f;

            // A gloss map means the scalar is a multiplier over per-texel values we are
            // not going to sample. Treat the scalar as an upper bound and say so.
            bool hasGlossMap = HasTexture(material, IdMetallicGlossMap) || HasTexture(material, IdSpecGlossMap);
            if (hasGlossMap)
            {
                f.GlossSource = "gloss-map";
                f.Confidence = 0.7f;
            }

            if (material.HasProperty(IdEnvReflections))
            {
                f.UsesEnvironmentReflections = ReadFloat(material, IdEnvReflections, 1f) > 0.5f;
                f.SamplesReflectionProbes = f.UsesEnvironmentReflections;
            }

            if (string.Equals(f.Family, "complex-lit", StringComparison.Ordinal) && material.HasProperty(IdClearCoatMask))
            {
                // Clear coat adds a second, usually sharp, specular lobe. A matte base
                // under clear coat still wants a probe.
                float mask = Mathf.Clamp01(ReadFloat(material, IdClearCoatMask, 0f));
                float coatSmoothness = Mathf.Clamp01(ReadFloat(material, IdClearCoatSmoothness, 1f));
                f.ClearCoat = mask * coatSmoothness;
                f.Smoothness = Mathf.Max(f.Smoothness, f.ClearCoat);
            }
        }

        /// <summary>
        /// Unknown or custom Shader Graph. Tries direct properties, then a name scan, then
        /// gives up honestly with a neutral prior at low confidence.
        /// </summary>
        static void ReadUnknown(Material material, MaterialFacts f)
        {
            if (material.HasProperty(IdSmoothness))
            {
                f.Smoothness = Mathf.Clamp01(material.GetFloat(IdSmoothness));
                f.GlossSource = "graph-property";
                f.Confidence = 0.9f;
            }
            else if (material.HasProperty(IdGlossiness))
            {
                f.Smoothness = Mathf.Clamp01(material.GetFloat(IdGlossiness));
                f.GlossSource = "graph-property";
                f.Confidence = 0.9f;
            }
            else if (material.HasProperty(IdRoughness))
            {
                f.Smoothness = 1f - Mathf.Clamp01(material.GetFloat(IdRoughness));
                f.GlossSource = "graph-property";
                f.Confidence = 0.9f;
            }
            else if (TryScanProperties(material, f))
            {
                f.GlossSource = "graph-property";
                f.Confidence = 0.6f;
            }
            else
            {
                // Neutral prior. Deliberately not 0 (which would hide a mirror) and not 1
                // (which would spend a 256px cubemap on a plaster wall). The low weight is
                // what stops it dominating a zone's score either way.
                f.Smoothness = 0.5f;
                f.Metallic = 0f;
                f.GlossSource = "unknown";
                f.Confidence = 0.25f;
            }

            if (material.HasProperty(IdMetallic))
                f.Metallic = Mathf.Clamp01(material.GetFloat(IdMetallic));
        }

        /// <summary>
        /// Last resort before the neutral prior: walk the shader's declared properties
        /// looking for something gloss-shaped under a non-standard name.
        /// </summary>
        static bool TryScanProperties(Material material, MaterialFacts f)
        {
            Shader shader = material.shader;
            if (shader == null) return false;

            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Float &&
                    shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Range)
                    continue;

                string name = shader.GetPropertyName(i);
                string lower = name.ToLowerInvariant();

                if (lower.Contains("smooth") || lower.Contains("gloss"))
                {
                    f.Smoothness = Mathf.Clamp01(material.GetFloat(name));
                    return true;
                }

                if (lower.Contains("rough"))
                {
                    f.Smoothness = 1f - Mathf.Clamp01(material.GetFloat(name));
                    return true;
                }
            }

            return false;
        }

        static void ReadSurface(Material material, MaterialFacts f)
        {
            bool alphaClip = material.HasProperty(IdAlphaClip) && material.GetFloat(IdAlphaClip) > 0.5f;

            if (material.HasProperty(IdSurface))
            {
                // URP: 0 = Opaque, 1 = Transparent.
                bool transparent = material.GetFloat(IdSurface) > 0.5f;
                f.SurfaceType = transparent ? "transparent" : (alphaClip ? "cutout" : "opaque");
            }
            else
            {
                // No _Surface: fall back to the RenderType tag, which custom shaders set.
                string renderType = material.GetTag("RenderType", false, "Opaque");
                if (string.Equals(renderType, "Transparent", StringComparison.Ordinal)) f.SurfaceType = "transparent";
                else if (string.Equals(renderType, "TransparentCutout", StringComparison.Ordinal)) f.SurfaceType = "cutout";
                else f.SurfaceType = alphaClip ? "cutout" : "opaque";
            }

            // _Cull: 0 = Off (double sided), 1 = Front, 2 = Back.
            if (material.HasProperty(IdCull))
                f.DoubleSided = Mathf.Approximately(material.GetFloat(IdCull), 0f);
        }

        static string ClassifyFamily(Shader shader)
        {
            if (shader == null) return "unknown";

            string name = shader.name;

            if (name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
            {
                string leaf = name.Substring("Universal Render Pipeline/".Length);
                if (leaf.StartsWith("Simple Lit", StringComparison.Ordinal)) return "simple-lit";
                if (leaf.StartsWith("Complex Lit", StringComparison.Ordinal)) return "complex-lit";
                if (leaf.StartsWith("Unlit", StringComparison.Ordinal)) return "unlit";
                if (leaf.StartsWith("Lit", StringComparison.Ordinal)) return "lit";
                if (leaf.StartsWith("Baked Lit", StringComparison.Ordinal)) return "lit";
                return "unknown";
            }

            if (name.StartsWith("Unlit/", StringComparison.Ordinal)) return "unlit";

            // Shader Graph assets are identified by their asset extension rather than by
            // name, because their shader name is whatever the author typed.
            string path = AssetDatabase.GetAssetPath(shader);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                return "shadergraph";

            return "unknown";
        }

        static float ReadFloat(Material material, int id, float fallback)
        {
            return material.HasProperty(id) ? material.GetFloat(id) : fallback;
        }

        static bool HasTexture(Material material, int id)
        {
            return material.HasProperty(id) && material.GetTexture(id) != null;
        }

        /// <summary>
        /// How strongly this material wants a reflection probe.
        ///
        /// Smoothness is cubed because perceived reflection sharpness rises far faster
        /// than linearly: 0.9 and 0.6 look nothing alike, while 0.3 and 0.0 look much the
        /// same. Metallic matters because a metal has no diffuse term to hide a bad
        /// reflection behind. Confidence keeps a guessed material from dominating a zone.
        /// </summary>
        public float ReflectionWeight(float surfaceArea)
        {
            if (!SamplesReflectionProbes || !UsesEnvironmentReflections) return 0f;

            float gloss = Smoothness * Smoothness * Smoothness;
            float metalBoost = 0.35f + 0.65f * Metallic;
            float surfacePenalty = IsOpaque ? 1f : 0.5f;

            return gloss * metalBoost * surfaceArea * surfacePenalty * Confidence;
        }
    }
}
