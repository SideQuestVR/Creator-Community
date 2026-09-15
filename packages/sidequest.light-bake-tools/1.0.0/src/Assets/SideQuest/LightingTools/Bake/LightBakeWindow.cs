// SideQuest Lighting Tools - MIT
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Bake
{
    /// <summary>
    /// The light bake window.
    ///
    /// Leads with the atlas budget, because that is the number a bake is actually
    /// constrained by and the one Unity's own Lighting window never shows. Resolution is
    /// entered in texels per world unit, which tells you nothing about how many atlas
    /// pages come out - so the pages are shown here instead, live, as the slider moves.
    /// </summary>
    public sealed class LightBakeWindow : EditorWindow
    {
        Vector2 _scroll;
        string _lastMessage;

        [MenuItem(LightBakeTool.MenuRoot + "Light Bake Window", false, 1)]
        public static void Open()
        {
            GetWindow<LightBakeWindow>(false, "Light Bake", true).minSize = new Vector2(360, 460);
        }

        void OnGUI()
        {
            var settings = SqSettings.instance;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawBakeryNotice();

            EditorGUILayout.LabelField("Atlas budget", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            settings.maxLightmapAtlases = EditorGUILayout.IntSlider(
                new GUIContent("Max atlases", "How many lightmap pages the world may ship. On Quest this is memory taken from textures, meshes and everything else."),
                settings.maxLightmapAtlases, 1, 16);

            // The string-label overload: the GUIContent one wants GUIContent[] options.
            settings.lightmapAtlasSize = EditorGUILayout.IntPopup(
                "Atlas size",
                settings.lightmapAtlasSize,
                new[] { "256", "512", "1024", "2048", "4096" },
                new[] { 256, 512, 1024, 2048, 4096 });

            if (EditorGUI.EndChangeCheck()) settings.Persist();

            EditorGUILayout.HelpBox(
                "Two 1024 pages, or one 2048, is a realistic ceiling for a standalone headset world. Analyze solves the resolution that fits rather than leaving you to find it by baking.",
                MessageType.None);

            EditorGUILayout.Space();
            DrawBakeState();

            EditorGUILayout.Space();
            DrawActions();

            if (!string.IsNullOrEmpty(_lastMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_lastMessage, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        static void DrawBakeryNotice()
        {
            if (!BakeryDetector.IsPresent) return;

            EditorGUILayout.HelpBox(
                "Bakery is installed. This tool will not drive it or change its settings. Analysis still applies - lightmap UVs, atlas budget and scale tuning all feed Bakery too - but starting a Unity bake here would produce a second, competing set of lightmaps.",
                MessageType.Warning);
            EditorGUILayout.Space();
        }

        static void DrawBakeState()
        {
            EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

            bool running = Lightmapping.isRunning;

            if (running)
            {
                Rect rect = EditorGUILayout.GetControlRect(false, 18f);
                EditorGUI.ProgressBar(rect, Lightmapping.buildProgress,
                    string.Format("Baking {0:P0}", Lightmapping.buildProgress));
                return;
            }

            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            int atlases = lightmaps != null ? lightmaps.Length : 0;

            EditorGUILayout.HelpBox(
                atlases == 0
                    ? "Idle. No baked lightmaps in this scene."
                    : string.Format("Idle. {0} baked lightmap page(s).", atlases),
                MessageType.None);
        }

        void DrawActions()
        {
            EditorGUILayout.LabelField("Workflow", EditorStyles.boldLabel);

            if (GUILayout.Button("1. Analyze Scene"))
            {
                LightBakeTool.AnalyzeScene();
                _lastMessage = "Report written. Check the atlas estimate and the lightmap UV warnings first.";
            }

            if (GUILayout.Button("2. Generate Missing Lightmap UVs"))
            {
                LightBakeTool.GenerateMissingUvs();
                _lastMessage = "Model importers updated where lightmap UVs were missing.";
            }

            if (GUILayout.Button("3. Accept Recommendations"))
            {
                LightBakeTool.AcceptRecommendations();
                _lastMessage = "Plan written, with the resolution solved to fit the atlas budget.";
            }

            GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
            if (GUILayout.Button("4. Apply Plan"))
            {
                LightBakeTool.ApplyPlan();
                _lastMessage = "Lighting settings asset written and assigned. No bake started.";
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(Lightmapping.isRunning))
            {
                if (GUILayout.Button("5. Start Bake"))
                {
                    LightBakeTool.StartBake();
                    _lastMessage = "Bake started in the background. The scene must be saved first.";
                }
            }

            using (new EditorGUI.DisabledScope(!Lightmapping.isRunning))
            {
                if (GUILayout.Button("Cancel"))
                {
                    LightBakeTool.CancelBake();
                    _lastMessage = "Bake cancelled.";
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Revert Last Apply")) LightBakeTool.RevertLastApply();

            if (GUILayout.Button("Clear Baked Lighting"))
            {
                LightBakeTool.ClearBakedLighting();
                _lastMessage = "Baked lighting cleared.";
            }
            EditorGUILayout.EndHorizontal();
        }

        void OnInspectorUpdate()
        {
            // The bake runs outside the GUI loop, so progress only moves if the window asks.
            if (Lightmapping.isRunning) Repaint();
        }
    }
}
