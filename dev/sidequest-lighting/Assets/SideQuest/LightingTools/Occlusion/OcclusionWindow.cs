// SideQuest Lighting Tools - MIT
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Occlusion
{
    /// <summary>
    /// The occlusion window.
    ///
    /// Shows the three bake parameters alongside the live bake state, because the mistake
    /// this tool exists to prevent - a hole size small enough to turn a two-minute bake
    /// into an overnight one - is only visible if the numbers and the running bake are on
    /// screen together.
    /// </summary>
    public sealed class OcclusionWindow : EditorWindow
    {
        Vector2 _scroll;
        string _lastMessage;

        [MenuItem(OcclusionTool.MenuRoot + "Occlusion Window", false, 1)]
        public static void Open()
        {
            GetWindow<OcclusionWindow>(false, "Occlusion", true).minSize = new Vector2(340, 420);
        }

        void OnGUI()
        {
            var settings = SqSettings.instance;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Classification", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            settings.minOccluderFaceSize = EditorGUILayout.Slider(
                new GUIContent("Min occluder face (m)",
                    "An object occludes only if its second-largest axis reaches this. Tests overall size instead and every thin wall - the best occluder there is - gets rejected."),
                settings.minOccluderFaceSize, 0.1f, 10f);

            settings.occlusionDataBudgetMB = EditorGUILayout.Slider(
                new GUIContent("Data budget (MB)", "Umbra data size that triggers a warning. On Quest this is memory taken from everything else."),
                settings.occlusionDataBudgetMB, 0.5f, 64f);

            if (EditorGUI.EndChangeCheck()) settings.Persist();

            EditorGUILayout.Space();
            DrawBakeParameters();

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

        static void DrawBakeParameters()
        {
            EditorGUILayout.LabelField("Bake parameters (project-wide)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            float occluder = EditorGUILayout.Slider(
                new GUIContent("Smallest occluder", "Unity's default of 5 is sized for city blocks; nothing in a room-scale world is 5m, so the bake finds no occluders."),
                StaticOcclusionCulling.smallestOccluder, 0.1f, 20f);

            float hole = EditorGUILayout.Slider(
                new GUIContent("Smallest hole", "The narrowest gap sight lines pass through. Too large and geometry pops in through doorways; too small and the bake takes hours."),
                StaticOcclusionCulling.smallestHole, 0.01f, 5f);

            float backface = EditorGUILayout.Slider(
                new GUIContent("Backface threshold", "100 makes no backface assumptions, which is right for closed interiors. Lower values shrink the data but let the camera see through walls from the wrong side."),
                StaticOcclusionCulling.backfaceThreshold, 5f, 100f);

            if (EditorGUI.EndChangeCheck())
            {
                StaticOcclusionCulling.smallestOccluder = occluder;
                StaticOcclusionCulling.smallestHole = hole;
                StaticOcclusionCulling.backfaceThreshold = backface;
            }

            if (hole < 0.1f)
            {
                EditorGUILayout.HelpBox(
                    "A hole this small multiplies the bake cost sharply - halving it makes the bake roughly eight times longer. Analyze Scene derives this from the narrowest doorway it measured.",
                    MessageType.Warning);
            }
        }

        static void DrawBakeState()
        {
            EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

            bool running = StaticOcclusionCulling.isRunning;
            int dataSize = (int)StaticOcclusionCulling.umbraDataSize;

            EditorGUILayout.HelpBox(
                running
                    ? "A bake is running in the background. The Editor stays usable; Bake Status reports progress."
                    : string.Format("Idle. Current occlusion data: {0} MB.", SqFormat.Num(dataSize / 1048576f)),
                running ? MessageType.Info : MessageType.None);
        }

        void DrawActions()
        {
            EditorGUILayout.LabelField("Workflow", EditorStyles.boldLabel);

            if (GUILayout.Button("1. Analyze Scene"))
            {
                OcclusionTool.AnalyzeScene();
                _lastMessage = "Report written. Check the occluder count - zero means a bake would cull nothing.";
            }

            if (GUILayout.Button("2. Accept Recommendations"))
            {
                OcclusionTool.AcceptRecommendations();
                _lastMessage = "Plan written, with parameters derived from the scene's own doorways.";
            }

            if (GUILayout.Button("3. Preview Plan"))
            {
                OcclusionTool.PreviewPlan();
                _lastMessage = "Scene view tinted: orange occluders, blue occludees, red excluded.";
            }

            GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
            if (GUILayout.Button("4. Apply Plan"))
            {
                OcclusionTool.ApplyPlan();
                _lastMessage = "Static flags applied and bake parameters set.";
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(StaticOcclusionCulling.isRunning))
            {
                if (GUILayout.Button("5. Start Bake"))
                {
                    OcclusionTool.StartBake();
                    _lastMessage = "Bake started in the background.";
                }
            }

            using (new EditorGUI.DisabledScope(!StaticOcclusionCulling.isRunning))
            {
                if (GUILayout.Button("Cancel"))
                {
                    OcclusionTool.CancelBake();
                    _lastMessage = "Bake cancelled.";
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Revert Last Apply")) OcclusionTool.RevertLastApply();
            if (GUILayout.Button("Clear Preview")) OcclusionTool.ClearPreview();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Clear Occlusion Data"))
            {
                OcclusionTool.ClearData();
                _lastMessage = "Occlusion data cleared.";
            }
        }

        void OnInspectorUpdate()
        {
            // The bake runs outside the GUI loop, so the window has to ask rather than wait
            // to be told, or the running state would only update when the mouse moves.
            if (StaticOcclusionCulling.isRunning) Repaint();
        }
    }
}
