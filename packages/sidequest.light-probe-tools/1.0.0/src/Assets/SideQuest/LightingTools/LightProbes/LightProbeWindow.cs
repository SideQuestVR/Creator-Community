// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.LightProbes
{
    /// <summary>
    /// The window a person uses. Everything here also works without it, through the menu
    /// items and the JSON files - the window is a front end to the same plan format, not a
    /// second way of doing the work.
    ///
    /// Settings are read from and written to SqSettings, which persists to ProjectSettings.
    /// The prior-art windows kept their values in private fields, so every number reset the
    /// moment the window closed and nothing was ever reproducible between sessions.
    /// </summary>
    public sealed class LightProbeWindow : EditorWindow
    {
        ProbeStrategy _strategy = ProbeStrategy.Adaptive;
        GameObject _volumeObject;
        Vector2 _scroll;
        string _lastMessage;

        [MenuItem(LightProbeTool.MenuRoot + "Light Probes Window", false, 1)]
        public static void Open()
        {
            GetWindow<LightProbeWindow>(false, "Light Probes", true).minSize = new Vector2(340, 460);
        }

        void OnEnable()
        {
            var settings = SqSettings.instance;
            _strategy = ProbeStrategy.Adaptive;
            titleContent = new GUIContent("Light Probes");
            if (settings.probeLayerHeights == null || settings.probeLayerHeights.Length == 0)
            {
                settings.probeLayerHeights = new[] { 0.3f, 1.6f, 2.6f };
                settings.Persist();
            }
        }

        void OnGUI()
        {
            var settings = SqSettings.instance;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Strategy", EditorStyles.boldLabel);
            _strategy = (ProbeStrategy)EditorGUILayout.EnumPopup(
                new GUIContent("Placement", "Adaptive reads the scene; NavMesh follows walkable space; Volume fills a collider; Agent uses supplied or painted positions."),
                _strategy);

            DrawStrategyHelp();

            if (_strategy == ProbeStrategy.MeshVolume)
            {
                _volumeObject = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Volume object", "A GameObject with a collider bounding the space to fill."),
                    _volumeObject, typeof(GameObject), true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Density", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            settings.probeSpacing = EditorGUILayout.Slider(
                new GUIContent("Spacing (m)", "Distance between probes. Smaller means more probes - the same meaning in every strategy."),
                settings.probeSpacing, 0.25f, 10f);

            settings.probeMinSpacing = EditorGUILayout.Slider(
                new GUIContent("Minimum spacing (m)", "Density heuristics never subdivide below this."),
                settings.probeMinSpacing, 0.1f, 5f);

            settings.probeMergeDistance = EditorGUILayout.Slider(
                new GUIContent("Merge distance (m)", "Probes closer than this are thinned, keeping one real sample per cell."),
                settings.probeMergeDistance, 0.05f, 5f);

            settings.probeEdgeBand = EditorGUILayout.Slider(
                new GUIContent("Edge band (m)", "Extra probes are added within this distance of a NavMesh or geometry edge."),
                settings.probeEdgeBand, 0f, 3f);

            settings.maxLightProbes = EditorGUILayout.IntSlider(
                new GUIContent("Max probes", "Hard budget. A plan can never raise this for itself."),
                settings.maxLightProbes, 50, 20000);

            DrawLayerHeights(settings);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Static flags", EditorStyles.boldLabel);
            settings.verboseDetail = EditorGUILayout.Toggle(
                new GUIContent("Verbose console", "Log extra non-machine-readable detail."), settings.verboseDetail);

            if (EditorGUI.EndChangeCheck()) settings.Persist();

            EditorGUILayout.Space();
            DrawEstimate(settings);

            EditorGUILayout.Space();
            DrawActions();

            EditorGUILayout.Space();
            DrawPaintSection();

            if (!string.IsNullOrEmpty(_lastMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_lastMessage, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawStrategyHelp()
        {
            string help;
            switch (_strategy)
            {
                case ProbeStrategy.NavMesh:
                    help = "Samples a grid over the baked NavMesh plus its boundary edges. Needs a NavMesh; puts probes exactly where players can stand.";
                    break;
                case ProbeStrategy.MeshVolume:
                    help = "Fills a collider volume with an even 3D grid. Use when you know which space matters.";
                    break;
                case ProbeStrategy.Agent:
                    help = "Uses positions painted below, or supplied by an agent in the plan file. For judgement the heuristics cannot encode.";
                    break;
                default:
                    help = "Derives density from lights, geometry edges and how sharply lighting varies. Works without a NavMesh.";
                    break;
            }
            EditorGUILayout.HelpBox(help, MessageType.None);
        }

        static void DrawLayerHeights(SqSettings settings)
        {
            EditorGUILayout.LabelField(
                new GUIContent("Layer heights (m)", "Probes are placed at each height above the floor. A single layer lights VR badly and tetrahedralises into slivers."));

            EditorGUI.indentLevel++;
            var heights = new List<float>(settings.probeLayerHeights);

            for (int i = 0; i < heights.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                heights[i] = EditorGUILayout.FloatField("Layer " + (i + 1), heights[i]);
                if (GUILayout.Button("-", GUILayout.Width(24)) && heights.Count > 1)
                {
                    heights.RemoveAt(i);
                    settings.probeLayerHeights = heights.ToArray();
                    settings.Persist();
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.indentLevel--;
                    return;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add layer"))
            {
                float last = heights.Count > 0 ? heights[heights.Count - 1] : 0.3f;
                heights.Add(last + 1f);
            }

            settings.probeLayerHeights = heights.ToArray();
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// A count before committing, because the alternative is finding out by waiting.
        ///
        /// The prior-art tool stepped its loops by a field that defaulted to zero, so the
        /// first click on a fresh window hung the Editor outright. Estimating first also
        /// makes it obvious when spacing is off by an order of magnitude.
        /// </summary>
        void DrawEstimate(SqSettings settings)
        {
            if (settings.probeSpacing < 0.01f)
            {
                EditorGUILayout.HelpBox("Spacing is too small to sample. Raise it above 0.01m.", MessageType.Error);
                return;
            }

            var scan = SceneScannerCache.Peek();
            if (scan == null || !scan.Scale.HasBounds)
            {
                EditorGUILayout.HelpBox("Run Analyze Scene for a probe count estimate.", MessageType.None);
                return;
            }

            float area = Mathf.Max(scan.Scale.FloorAreaEstimate, 1f);
            int layers = Mathf.Max(1, settings.probeLayerHeights.Length);
            int estimate = Mathf.RoundToInt(area / (settings.probeSpacing * settings.probeSpacing)) * layers;

            EditorGUILayout.HelpBox(string.Format(
                "Rough estimate: {0:N0} probes over {1:N0}m² in {2} layer(s). Budget is {3:N0}.",
                estimate, area, layers, settings.maxLightProbes),
                estimate > settings.maxLightProbes ? MessageType.Warning : MessageType.None);
        }

        void DrawActions()
        {
            EditorGUILayout.LabelField("Workflow", EditorStyles.boldLabel);

            if (GUILayout.Button("1. Analyze Scene"))
            {
                LightProbeTool.AnalyzeScene();
                _lastMessage = "Report written. See the Console for its path.";
            }

            if (GUILayout.Button("2. Accept Recommendations"))
            {
                LightProbeTool.AcceptRecommendations();
                _lastMessage = "Plan written from the recommendation. Edit it by hand, or let an agent adjust it.";
            }

            if (GUILayout.Button("3. Preview Plan"))
            {
                LightProbeTool.PreviewPlan();
                _lastMessage = "Preview drawn in the Scene view. Nothing has been modified.";
            }

            GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
            if (GUILayout.Button("4. Apply Plan"))
            {
                LightProbeTool.ApplyPlan();
                _lastMessage = "Applied. One Ctrl+Z reverts the whole change.";
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Revert Last Apply")) LightProbeTool.RevertLastApply();
            if (GUILayout.Button("Clear Preview")) LightProbeTool.ClearPreview();
            EditorGUILayout.EndHorizontal();
        }

        void DrawPaintSection()
        {
            EditorGUILayout.LabelField("Manual placement", EditorStyles.boldLabel);

            bool painting = ProbePaintMode.IsActive;
            GUI.backgroundColor = painting ? new Color(1f, 0.85f, 0.4f) : Color.white;

            if (GUILayout.Button(painting ? "Stop painting" : "Paint probes in Scene view"))
            {
                if (painting) ProbePaintMode.Stop();
                else ProbePaintMode.Start();
            }

            GUI.backgroundColor = Color.white;

            if (painting)
            {
                EditorGUILayout.HelpBox(
                    "Click a surface to add a probe. Ctrl+click a probe to remove the nearest one. Painting writes straight to the group, with undo.",
                    MessageType.Info);
            }
        }
    }

    /// <summary>
    /// Caches the most recent scan so the window can show an estimate without rescanning
    /// on every repaint. OnGUI runs many times a second; a full scene scan there would make
    /// the window unusable on any real scene.
    /// </summary>
    public static class SceneScannerCache
    {
        static SceneScan _last;

        public static void Store(SceneScan scan) { _last = scan; }
        public static SceneScan Peek() { return _last; }
    }
}
