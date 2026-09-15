// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// Click-to-place probe painting in the Scene view.
    ///
    /// This is the one part of the prior-art tooling that was already correct: it converted
    /// to group-local space and recorded undo, which the automatic paths did not. It is
    /// carried over largely as it was, with the group lookup and the write routed through
    /// ProbeGroupWriter so painting and automatic placement cannot drift apart.
    ///
    /// Kept as an explicit mode rather than an always-on handler: a Scene view that quietly
    /// swallows clicks is worse than one that needs a button first.
    /// </summary>
    public static class ProbePaintMode
    {
        public static bool IsActive { get; private set; }

        static LightProbeGroup _group;

        public static void Start()
        {
            if (IsActive) return;

            _group = FindGroup();
            if (_group == null)
            {
                GameObject go = SqUndo.CreateGameObject(ProbeGroupWriter.DefaultGroupName, null, "Create Light Probe Group");
                _group = SqUndo.AddComponent<LightProbeGroup>(go, "Create Light Probe Group");
            }

            IsActive = true;
            SceneView.duringSceneGui += OnSceneGui;
            SceneView.RepaintAll();

            SqLog.Ok(LightProbePlan.ToolId, "paint-start", "group", _group.gameObject.name);
        }

        public static void Stop()
        {
            if (!IsActive) return;

            IsActive = false;
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.RepaintAll();

            int count = _group != null && _group.probePositions != null ? _group.probePositions.Length : 0;
            SqLog.Ok(LightProbePlan.ToolId, "paint-stop", "probes", count.ToString());
        }

        static LightProbeGroup FindGroup()
        {
            LightProbeGroup[] groups = Object.FindObjectsByType<LightProbeGroup>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < groups.Length; i++)
                if (groups[i].gameObject.name == ProbeGroupWriter.DefaultGroupName) return groups[i];

            return groups.Length > 0 ? groups[0] : null;
        }

        static void OnSceneGui(SceneView sceneView)
        {
            if (!IsActive) return;

            if (_group == null)
            {
                Stop();
                return;
            }

            // Take control of the default handle so a click paints instead of selecting
            // whatever is under the cursor.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            DrawExistingProbes();
            DrawHud(sceneView);

            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 1000f, ~0, QueryTriggerInteraction.Ignore)) return;

            if (e.control || e.command) RemoveNearest(hit.point);
            else AddProbe(hit.point);

            e.Use();
        }

        static void AddProbe(Vector3 surfacePoint)
        {
            var settings = SqSettings.instance;
            float height = settings.probeLayerHeights != null && settings.probeLayerHeights.Length > 0
                ? settings.probeLayerHeights[0]
                : 0.3f;

            List<Vector3> world = ProbeGroupWriter.ReadWorldPositions(_group);
            world.Add(surfacePoint + Vector3.up * height);

            ProbeGroupWriter.CommitWorldPositions(_group, world, "Paint Light Probe");
        }

        static void RemoveNearest(Vector3 point)
        {
            List<Vector3> world = ProbeGroupWriter.ReadWorldPositions(_group);
            if (world.Count == 0) return;

            int nearest = 0;
            float nearestSqr = (world[0] - point).sqrMagnitude;

            for (int i = 1; i < world.Count; i++)
            {
                float sqr = (world[i] - point).sqrMagnitude;
                if (sqr < nearestSqr) { nearestSqr = sqr; nearest = i; }
            }

            world.RemoveAt(nearest);
            ProbeGroupWriter.CommitWorldPositions(_group, world, "Remove Light Probe");
        }

        static void DrawExistingProbes()
        {
            Vector3[] local = _group.probePositions;
            if (local == null) return;

            Transform transform = _group.transform;
            Color previous = Handles.color;
            Handles.color = SqPreviewDraw.ColorCreate;

            // Capped for the same reason the preview is: immediate-mode handles make the
            // Scene view unusable long before the probe budget is reached.
            int limit = Mathf.Min(local.Length, SqPreviewDraw.MaxDrawnPoints);
            for (int i = 0; i < limit; i++)
            {
                Vector3 world = transform.TransformPoint(local[i]);
                Handles.DotHandleCap(0, world, Quaternion.identity, 0.04f, EventType.Repaint);
            }

            Handles.color = previous;
        }

        static void DrawHud(SceneView sceneView)
        {
            Handles.BeginGUI();

            var rect = new Rect(10, 10, 280, 54);
            GUI.Box(rect, GUIContent.none);

            int count = _group.probePositions != null ? _group.probePositions.Length : 0;
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, 18), "Painting light probes - " + count + " placed");
            GUI.Label(new Rect(rect.x + 8, rect.y + 26, rect.width - 16, 18), "Click to add, Ctrl+click to remove nearest");

            Handles.EndGUI();
        }
    }
}
