// SideQuest Lighting Tools - MIT
using System;
using SideQuest.LightingTools.Core;
using UnityEngine;

namespace SideQuest.LightingTools.ReflectionProbes
{
    public enum ProbeAction { Create, Update, Delete, Keep }

    /// <summary>
    /// One reflection probe, as planned rather than as placed.
    ///
    /// Everything a probe needs is decided before anything is created, so preview and
    /// apply work from an identical description and the Scene view overlay cannot disagree
    /// with what the apply will do.
    ///
    /// Probes are addressed by a stable id derived from the zone and cluster rather than
    /// by scene position, so re-running analysis on an unchanged scene produces the same
    /// ids and the plan reads as update/keep instead of delete-everything-and-recreate.
    /// </summary>
    public sealed class ReflectionProbeSpec
    {
        /// <summary>Resolutions Unity actually supports for a reflection cubemap.</summary>
        public static readonly int[] AllowedResolutions = { 16, 32, 64, 128, 256, 512 };

        public string Id;
        public ProbeAction Action = ProbeAction.Create;

        /// <summary>GlobalObjectId of the probe being updated or deleted. Null for a create.</summary>
        public string TargetId;

        public Vector3 Position;
        public Vector3 BoxSize = Vector3.one * 4f;
        public Vector3 BoxOffset;

        public int Resolution = 64;
        public int Importance = 1;
        public float BlendDistance = 0.5f;
        public float Intensity = 1f;
        public float NearClip = 0.3f;
        public float FarClip = 1000f;
        public int CullingMask = ~0;
        public bool Hdr = true;
        public bool BoxProjection = true;

        public int ZoneId = -1;

        /// <summary>Why this probe exists, carried into the report so a reviewer can judge it.</summary>
        public string Reason;

        /// <summary>Estimated weight of glossy surfaces this probe serves.</summary>
        public float ServedWeight;

        public Bounds WorldBox
        {
            get { return new Bounds(Position + BoxOffset, BoxSize); }
        }

        /// <summary>
        /// Baked cubemap memory, in bytes.
        ///
        /// Six faces, plus roughly a third again for the mip chain that roughness
        /// convolution needs. HDR doubles it - and HDR is the default, because without it
        /// bright reflections clip and metal reads as grey plastic. This is the number that
        /// actually constrains a Quest scene, so it is estimated up front rather than
        /// discovered after a bake.
        /// </summary>
        public long EstimatedBytes
        {
            get
            {
                long bytesPerPixel = Hdr ? 8 : 4;
                return (long)(6L * Resolution * Resolution * bytesPerPixel * 1.33f);
            }
        }

        public static string ActionName(ProbeAction action)
        {
            switch (action)
            {
                case ProbeAction.Update: return "update";
                case ProbeAction.Delete: return "delete";
                case ProbeAction.Keep: return "keep";
                default: return "create";
            }
        }

        public static ProbeAction ParseAction(string name, PlanValidation validation, string field)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "create": return ProbeAction.Create;
                case "update": return ProbeAction.Update;
                case "delete": return ProbeAction.Delete;
                case "keep": return ProbeAction.Keep;
                default:
                    validation.Note(SqSeverity.Warn, field, "unrecognised action; treated as keep", name, "keep");
                    return ProbeAction.Keep;
            }
        }

        public void Write(SqJsonWriter w)
        {
            w.BeginObject();
            w.Prop("id", Id);
            w.Prop("action", ActionName(Action));
            if (TargetId != null) w.Prop("targetId", TargetId);

            w.Prop("position", Position);
            w.Prop("boxSize", BoxSize);
            if (BoxOffset != Vector3.zero) w.Prop("boxOffset", BoxOffset);

            w.Prop("resolution", Resolution);
            w.Prop("importance", Importance);
            w.Prop("blendDistance", BlendDistance);
            w.Prop("intensity", Intensity);
            w.Prop("nearClip", NearClip);
            w.Prop("farClip", FarClip);
            w.Prop("cullingMask", CullingMask);
            w.Prop("hdr", Hdr);
            w.Prop("boxProjection", BoxProjection);

            if (ZoneId >= 0) w.Prop("zone", ZoneId);
            w.Prop("estimatedKB", (int)(EstimatedBytes / 1024));
            if (ServedWeight > 0f) w.Prop("servedWeight", ServedWeight);
            if (Reason != null) w.Prop("reason", Reason);

            w.EndObject();
        }

        /// <summary>
        /// Reads one probe entry, clamping every scalar.
        ///
        /// Returns null only when the entry cannot be acted on at all - no position for a
        /// create, or a position nowhere near the scene. Everything else is pulled into
        /// range and recorded, because a single odd blend distance should not cost a whole
        /// analyse-and-replan round trip.
        /// </summary>
        public static ReflectionProbeSpec Read(SqJsonValue source, int index, SceneScan scan, PlanValidation validation)
        {
            string field = "probes[" + index + "]";

            var spec = new ReflectionProbeSpec
            {
                Id = source["id"].AsString("probe-" + index),
                TargetId = source["targetId"].AsString(null),
                ZoneId = source["zone"].AsInt(-1),
                Reason = source["reason"].AsString(null)
            };

            spec.Action = ParseAction(source["action"].AsString("create"), validation, field + ".action");

            if (spec.Action == ProbeAction.Delete || spec.Action == ProbeAction.Keep)
            {
                if (string.IsNullOrEmpty(spec.TargetId))
                {
                    validation.Note(SqSeverity.Warn, field + ".targetId",
                        "action needs a targetId naming an existing probe; entry skipped");
                    return null;
                }
                return spec;
            }

            Vector3 position;
            if (!source["position"].TryGetVector3(out position))
            {
                validation.Note(SqSeverity.Warn, field + ".position",
                    "missing or malformed position; entry skipped");
                return null;
            }

            if (scan.Scale.HasBounds && !PlanValidator.IsPositionPlausible(position, scan.Scale.WorldBounds))
            {
                validation.Note(SqSeverity.Warn, field + ".position",
                    "position is far outside the scene bounds; entry skipped",
                    position.ToString("0.#"), null);
                return null;
            }

            spec.Position = position;

            Vector3 boxSize;
            if (source["boxSize"].TryGetVector3(out boxSize))
            {
                spec.BoxSize = new Vector3(
                    validation.Clamp(field + ".boxSize.x", boxSize.x, 0.1f, 2000f),
                    validation.Clamp(field + ".boxSize.y", boxSize.y, 0.1f, 2000f),
                    validation.Clamp(field + ".boxSize.z", boxSize.z, 0.1f, 2000f));
            }

            Vector3 boxOffset;
            if (source["boxOffset"].TryGetVector3(out boxOffset))
            {
                spec.BoxOffset = new Vector3(
                    validation.Clamp(field + ".boxOffset.x", boxOffset.x, -1000f, 1000f),
                    validation.Clamp(field + ".boxOffset.y", boxOffset.y, -1000f, 1000f),
                    validation.Clamp(field + ".boxOffset.z", boxOffset.z, -1000f, 1000f));
            }

            spec.Resolution = validation.Snap(field + ".resolution", source["resolution"].AsInt(64), AllowedResolutions);
            spec.Importance = validation.ClampInt(field + ".importance", source["importance"].AsInt(1), 0, 10);
            spec.BlendDistance = validation.Clamp(field + ".blendDistance", source["blendDistance"].AsFloat(0.5f), 0f, 10f);
            spec.Intensity = validation.Clamp(field + ".intensity", source["intensity"].AsFloat(1f), 0f, 10f);
            spec.NearClip = validation.Clamp(field + ".nearClip", source["nearClip"].AsFloat(0.3f), 0.01f, 100f);
            spec.FarClip = validation.Clamp(field + ".farClip", source["farClip"].AsFloat(1000f), 1f, 10000f);

            if (spec.FarClip <= spec.NearClip)
            {
                validation.Note(SqSeverity.Warn, field + ".farClip",
                    "far clip was not beyond near clip; it was pushed out",
                    spec.FarClip.ToString("0.##"), (spec.NearClip * 100f).ToString("0.##"));
                spec.FarClip = spec.NearClip * 100f;
            }

            spec.CullingMask = source["cullingMask"].AsInt(~0);
            spec.Hdr = source["hdr"].AsBool(true);
            spec.BoxProjection = source["boxProjection"].AsBool(true);

            return spec;
        }
    }
}
