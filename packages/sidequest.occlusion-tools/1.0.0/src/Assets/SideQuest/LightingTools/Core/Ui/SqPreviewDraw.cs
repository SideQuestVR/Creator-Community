// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Draws a validated plan in the Scene view without touching the scene.
    ///
    /// Preview is what makes the agent loop safe to watch: the same validator runs, the
    /// same numbers are written to a preview file, and the result is visible in a
    /// screenshot - all before anything is modified. A plan that looks wrong here costs a
    /// re-plan; the same plan applied costs an undo and a re-bake.
    ///
    /// State lives in a ScriptableSingleton so it survives a domain reload, which happens
    /// whenever scripts recompile between the preview and the screenshot.
    /// </summary>
    [FilePath("Library/SideQuestLightingPreview.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SqPreviewDraw : ScriptableSingleton<SqPreviewDraw>
    {
        [System.Serializable]
        public struct Point
        {
            public Vector3 Position;
            public Color Color;
            public float Size;
        }

        [System.Serializable]
        public struct Box
        {
            public Vector3 Center;
            public Vector3 Size;
            public Color Color;
            public string Label;
        }

        [System.Serializable]
        public struct Tint
        {
            public int RendererInstanceId;
            public Color Color;
        }

        [SerializeField] List<Point> _points = new List<Point>();
        [SerializeField] List<Box> _boxes = new List<Box>();
        [SerializeField] List<Tint> _tints = new List<Tint>();
        [SerializeField] string _owner;

        /// <summary>
        /// Cap on drawn points. Handles are immediate-mode, so a 20,000-probe preview
        /// would make the Scene view unusable - and the shape of a probe field is legible
        /// from a sample of it anyway. The exact count comes from the preview file.
        /// </summary>
        public const int MaxDrawnPoints = 4000;

        static bool _hooked;

        public string Owner { get { return _owner; } }
        public int PointCount { get { return _points.Count; } }
        public int BoxCount { get { return _boxes.Count; } }
        public bool HasContent { get { return _points.Count > 0 || _boxes.Count > 0 || _tints.Count > 0; } }

        public void Begin(string owner)
        {
            _owner = owner;
            _points.Clear();
            _boxes.Clear();
            _tints.Clear();
            EnsureHook();
        }

        public void AddPoint(Vector3 position, Color color, float size = 0.08f)
        {
            if (_points.Count >= MaxDrawnPoints) return;
            _points.Add(new Point { Position = position, Color = color, Size = size });
        }

        public void AddBox(Vector3 center, Vector3 size, Color color, string label = null)
        {
            _boxes.Add(new Box { Center = center, Size = size, Color = color, Label = label });
        }

        public void AddTint(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            _tints.Add(new Tint { RendererInstanceId = renderer.GetInstanceID(), Color = color });
        }

        public void Commit()
        {
            Save(true);
            SceneView.RepaintAll();
        }

        public void Clear()
        {
            _owner = null;
            _points.Clear();
            _boxes.Clear();
            _tints.Clear();
            Save(true);
            SceneView.RepaintAll();
        }

        static void EnsureHook()
        {
            if (_hooked) return;
            SceneView.duringSceneGui += OnSceneGui;
            _hooked = true;
        }

        [InitializeOnLoadMethod]
        static void Initialise() { EnsureHook(); }

        static void OnSceneGui(SceneView sceneView)
        {
            SqPreviewDraw state = instance;
            if (!state.HasContent) return;

            Color previous = Handles.color;

            for (int i = 0; i < state._points.Count; i++)
            {
                Point p = state._points[i];
                Handles.color = p.Color;
                // Dot handles stay a constant size on screen, so a probe field reads the
                // same whether the view is inside a room or pulled back over the scene.
                Handles.DotHandleCap(0, p.Position, Quaternion.identity, p.Size * 0.5f, EventType.Repaint);
            }

            for (int i = 0; i < state._boxes.Count; i++)
            {
                Box b = state._boxes[i];
                Handles.color = b.Color;
                Handles.DrawWireCube(b.Center, b.Size);

                if (!string.IsNullOrEmpty(b.Label))
                {
                    Handles.color = Color.white;
                    Handles.Label(b.Center + Vector3.up * (b.Size.y * 0.5f + 0.15f), b.Label);
                }
            }

            for (int i = 0; i < state._tints.Count; i++)
            {
                Tint t = state._tints[i];
                var renderer = EditorUtility.InstanceIDToObject(t.RendererInstanceId) as Renderer;
                if (renderer == null) continue;

                Handles.color = t.Color;
                Bounds bounds = renderer.bounds;
                Handles.DrawWireCube(bounds.center, bounds.size);
            }

            Handles.color = previous;
        }

        // ---- shared colour vocabulary, so all four tools read the same way ----

        public static readonly Color ColorCreate = new Color(0.3f, 0.9f, 0.4f, 1f);
        public static readonly Color ColorUpdate = new Color(0.4f, 0.7f, 1f, 1f);
        public static readonly Color ColorDelete = new Color(1f, 0.4f, 0.35f, 1f);
        public static readonly Color ColorKeep = new Color(0.6f, 0.6f, 0.6f, 1f);
        public static readonly Color ColorOccluder = new Color(1f, 0.75f, 0.2f, 1f);
        public static readonly Color ColorOccludee = new Color(0.4f, 0.8f, 1f, 1f);

        /// <summary>Probe colour by density tier, so clustering is visible at a glance.</summary>
        public static Color DensityColor(float normalisedDensity)
        {
            return Color.Lerp(new Color(0.25f, 0.5f, 1f), new Color(1f, 0.85f, 0.25f), Mathf.Clamp01(normalisedDensity));
        }
    }
}
