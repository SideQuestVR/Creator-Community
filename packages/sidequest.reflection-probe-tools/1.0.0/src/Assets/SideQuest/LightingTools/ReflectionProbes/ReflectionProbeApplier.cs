// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SideQuest.LightingTools.ReflectionProbes
{
    /// <summary>
    /// Turns a validated plan into actual probes.
    ///
    /// Ownership is the rule that matters here. Probes this tool created carry a name
    /// prefix and are recorded by GlobalObjectId in the result file, and only those are
    /// ever updated or deleted. A probe somebody placed by hand is left exactly as it is,
    /// because the tool cannot know why it is there and the plan file offers no way to put
    /// it back.
    /// </summary>
    public static class ReflectionProbeApplier
    {
        public const string NamePrefix = "SQ_ReflectionProbe_";
        public const string RootName = "SQ_ReflectionProbes";

        public static void Apply(
            SceneScan scan,
            List<ReflectionProbeSpec> specs,
            HashSet<string> previouslyOwned,
            ApplyResult result)
        {
            using (SqUndo.Scope scope = SqUndo.Group("Apply Reflection Probe Plan"))
            {
                Transform root = FindOrCreateRoot();

                for (int i = 0; i < specs.Count; i++)
                {
                    ReflectionProbeSpec spec = specs[i];

                    switch (spec.Action)
                    {
                        case ProbeAction.Create:
                            CreateProbe(spec, root, result);
                            break;

                        case ProbeAction.Update:
                            UpdateProbe(spec, previouslyOwned, result);
                            break;

                        case ProbeAction.Delete:
                            DeleteProbe(spec, previouslyOwned, result);
                            break;

                        default:
                            result.Kept++;
                            break;
                    }
                }

                result.UndoGroup = scope.GroupId;
                result.Applied = true;
            }
        }

        static Transform FindOrCreateRoot()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null) return existing.transform;

            GameObject root = SqUndo.CreateGameObject(RootName, null, "Create Reflection Probe Root");
            root.transform.position = Vector3.zero;
            return root.transform;
        }

        static void CreateProbe(ReflectionProbeSpec spec, Transform root, ApplyResult result)
        {
            GameObject go = SqUndo.CreateGameObject(NamePrefix + spec.Id, root, "Create Reflection Probe");
            go.transform.position = spec.Position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            ReflectionProbe probe = SqUndo.AddComponent<ReflectionProbe>(go, "Create Reflection Probe");
            Configure(probe, spec);

            result.Created++;
            result.Own(SqObjectId.Of(go));
        }

        static void UpdateProbe(ReflectionProbeSpec spec, HashSet<string> previouslyOwned, ApplyResult result)
        {
            ReflectionProbe probe = ResolveOwned(spec, previouslyOwned);
            if (probe == null) { result.Skipped++; return; }

            SqUndo.Modify(probe.transform, "Update Reflection Probe");
            probe.transform.position = spec.Position;

            SqUndo.Modify(probe, "Update Reflection Probe");
            Configure(probe, spec);

            result.Updated++;
            result.Own(SqObjectId.Of(probe.gameObject));
        }

        static void DeleteProbe(ReflectionProbeSpec spec, HashSet<string> previouslyOwned, ApplyResult result)
        {
            ReflectionProbe probe = ResolveOwned(spec, previouslyOwned);
            if (probe == null) { result.Skipped++; return; }

            SqUndo.DestroyGameObject(probe.gameObject, "Delete Reflection Probe");
            result.Deleted++;
        }

        /// <summary>
        /// Resolves a plan target, refusing anything this tool did not create.
        ///
        /// Both tests have to pass: the name prefix, and the recorded ownership list from
        /// the previous run. The prefix alone could be typed by anyone; the ownership list
        /// alone would miss probes created before a result file was lost.
        /// </summary>
        static ReflectionProbe ResolveOwned(ReflectionProbeSpec spec, HashSet<string> previouslyOwned)
        {
            if (string.IsNullOrEmpty(spec.TargetId)) return null;

            var id = new SqObjectId(spec.TargetId);
            ReflectionProbe probe = id.Resolve<ReflectionProbe>();
            if (probe == null) return null;

            bool ownedByName = probe.gameObject.name.StartsWith(NamePrefix);
            bool ownedByRecord = previouslyOwned != null && previouslyOwned.Contains(spec.TargetId);

            if (!ownedByName && !ownedByRecord)
            {
                SqLog.Detail("refusing to modify reflection probe not created by this tool: " + probe.gameObject.name);
                return null;
            }

            return probe;
        }

        static void Configure(ReflectionProbe probe, ReflectionProbeSpec spec)
        {
            // Baked, always. Realtime re-renders the scene six times per refresh, which no
            // standalone headset has the budget for - the placer raises RP030 rather than
            // letting a plan quietly ask for it.
            probe.mode = ReflectionProbeMode.Baked;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;

            probe.size = spec.BoxSize;
            probe.center = spec.BoxOffset;
            probe.boxProjection = spec.BoxProjection;
            probe.blendDistance = spec.BlendDistance;
            probe.importance = spec.Importance;
            probe.intensity = spec.Intensity;
            probe.resolution = spec.Resolution;
            probe.hdr = spec.Hdr;
            probe.nearClipPlane = spec.NearClip;
            probe.farClipPlane = spec.FarClip;
            probe.cullingMask = spec.CullingMask;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        }

        /// <summary>
        /// Matches recommended probes against what the tool already owns in the scene.
        ///
        /// Without this, every analysis would recommend a fresh set and every apply would
        /// delete and recreate, losing baked cubemaps that were still perfectly good.
        /// Matching on id keeps a stable scene across runs.
        /// </summary>
        public static void ReconcileWithScene(SceneScan scan, List<ReflectionProbeSpec> specs)
        {
            var owned = new Dictionary<string, ReflectionProbe>();

            for (int i = 0; i < scan.Existing.ReflectionProbes.Count; i++)
            {
                ReflectionProbe probe = scan.Existing.ReflectionProbes[i];
                if (probe == null) continue;

                string name = probe.gameObject.name;
                if (!name.StartsWith(NamePrefix)) continue;

                owned[name.Substring(NamePrefix.Length)] = probe;
            }

            var matched = new HashSet<string>();

            for (int i = 0; i < specs.Count; i++)
            {
                ReflectionProbeSpec spec = specs[i];

                ReflectionProbe existing;
                if (!owned.TryGetValue(spec.Id, out existing)) continue;

                spec.Action = ProbeAction.Update;
                spec.TargetId = SqObjectId.Of(existing.gameObject).Value;
                matched.Add(spec.Id);
            }

            // Anything the tool owns that the new layout no longer wants is proposed for
            // deletion explicitly, so the plan shows the removal rather than leaving an
            // orphan probe quietly influencing the scene.
            foreach (var pair in owned)
            {
                if (matched.Contains(pair.Key)) continue;

                specs.Add(new ReflectionProbeSpec
                {
                    Id = pair.Key,
                    Action = ProbeAction.Delete,
                    TargetId = SqObjectId.Of(pair.Value.gameObject).Value,
                    Position = pair.Value.transform.position,
                    Reason = "no longer needed by the recommended layout"
                });
            }
        }
    }
}
