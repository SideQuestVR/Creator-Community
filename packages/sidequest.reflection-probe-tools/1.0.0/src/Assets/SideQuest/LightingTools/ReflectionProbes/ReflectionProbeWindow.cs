// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using SideQuest.LightingTools.Core;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.ReflectionProbes
{
    /// <summary>
    /// The reflection probe window.
    ///
    /// The gloss override table is the part worth the screen space. Reflection placement is
    /// only as good as its reading of which materials are shiny, and a custom Shader Graph
    /// gives it nothing to read. Rather than guessing silently, unknown shaders are listed
    /// here for a one-time answer that then applies everywhere.
    /// </summary>
    public sealed class ReflectionProbeWindow : EditorWindow
    {
        Vector2 _scroll;
        string _lastMessage;
        bool _showOverrides = true;

        [MenuItem(ReflectionProbeTool.MenuRoot + "Reflection Probes Window", false, 1)]
        public static void Open()
        {
            GetWindow<ReflectionProbeWindow>(false, "Reflection Probes", true).minSize = new Vector2(360, 480);
        }

        void OnGUI()
        {
            var settings = SqSettings.instance;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawPipelineWarnings();

            EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            settings.reflectionProbeEyeHeight = EditorGUILayout.Slider(
                new GUIContent("Capture height (m)", "Height above the room floor at which a probe captures. Eye height is what the player actually sees reflected."),
                settings.reflectionProbeEyeHeight, 0.2f, 4f);

            settings.reflectionBoxPadding = EditorGUILayout.Slider(
                new GUIContent("Box padding (m)", "How far the probe's influence box extends beyond the space it serves."),
                settings.reflectionBoxPadding, 0f, 2f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Budget", EditorStyles.boldLabel);

            settings.maxReflectionProbes = EditorGUILayout.IntSlider(
                new GUIContent("Max probes", "Hard ceiling. A plan can never raise this for itself."),
                settings.maxReflectionProbes, 1, 128);

            settings.reflectionProbeMemoryBudgetMB = EditorGUILayout.Slider(
                new GUIContent("Cubemap memory (MB)", "Total baked reflection memory. Resolutions are reduced to fit rather than probes being dropped."),
                settings.reflectionProbeMemoryBudgetMB, 1f, 256f);

            if (EditorGUI.EndChangeCheck()) settings.Persist();

            EditorGUILayout.HelpBox(
                "A 128px HDR probe costs about 1MB. Quest scenes rarely have more than about 24MB to spend on reflections.",
                MessageType.None);

            EditorGUILayout.Space();
            DrawOverrides(settings);

            EditorGUILayout.Space();
            DrawActions();

            if (!string.IsNullOrEmpty(_lastMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_lastMessage, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Surfaces the URP settings that decide whether probes work at all - with the
        /// caveat that in a shipped Banter world the client's URP asset is the one that
        /// counts, so fixing them here may change nothing in the built world.
        /// </summary>
        static void DrawPipelineWarnings()
        {
            UrpFacts facts = UrpFacts.Read();

            if (!facts.HasUrp)
            {
                EditorGUILayout.HelpBox(
                    "This project is not using URP. SideQuest Lighting Tools targets URP only.",
                    MessageType.Error);
                return;
            }

            bool boxProjectionOff = facts.ReflectionProbeBoxProjection.HasValue && !facts.ReflectionProbeBoxProjection.Value;
            bool blendingOff = facts.ReflectionProbeBlending.HasValue && !facts.ReflectionProbeBlending.Value;

            if (!boxProjectionOff && !blendingOff) return;

            var missing = new List<string>();
            if (boxProjectionOff) missing.Add("Box Projection");
            if (blendingOff) missing.Add("Probe Blending");

            EditorGUILayout.HelpBox(string.Format(
                "{0} disabled on this project's URP asset ({1}). Reflections will look wrong here. In a Banter world the client's URP asset decides this at runtime, so enabling it locally may not change the shipped result.",
                string.Join(" and ", missing.ToArray()), facts.AssetName),
                MessageType.Warning);
        }

        void DrawOverrides(SqSettings settings)
        {
            _showOverrides = EditorGUILayout.Foldout(_showOverrides,
                string.Format("Shader gloss overrides ({0})", settings.shaderGlossOverrides.Count), true);

            if (!_showOverrides) return;

            EditorGUILayout.HelpBox(
                "For shaders whose smoothness cannot be read automatically, usually custom Shader Graphs. Without an entry they get a neutral value at reduced weight and are listed in the report.",
                MessageType.None);

            EditorGUI.indentLevel++;
            int removeAt = -1;

            for (int i = 0; i < settings.shaderGlossOverrides.Count; i++)
            {
                SqSettings.ShaderGlossOverride entry = settings.shaderGlossOverrides[i];

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                entry.shaderName = EditorGUILayout.TextField("Shader", entry.shaderName);
                if (GUILayout.Button("-", GUILayout.Width(24))) removeAt = i;
                EditorGUILayout.EndHorizontal();

                entry.smoothness = EditorGUILayout.Slider("Smoothness", entry.smoothness, 0f, 1f);
                entry.metallic = EditorGUILayout.Slider("Metallic", entry.metallic, 0f, 1f);
                entry.usesEnvironmentReflections = EditorGUILayout.Toggle(
                    new GUIContent("Samples probes", "Untick for shaders that never read a reflection probe, such as Simple Lit or Unlit."),
                    entry.usesEnvironmentReflections);

                EditorGUILayout.EndVertical();
                settings.shaderGlossOverrides[i] = entry;
            }

            if (removeAt >= 0)
            {
                settings.shaderGlossOverrides.RemoveAt(removeAt);
                settings.Persist();
            }

            if (GUILayout.Button("Add override"))
            {
                settings.shaderGlossOverrides.Add(new SqSettings.ShaderGlossOverride
                {
                    shaderName = "",
                    smoothness = 0.5f,
                    metallic = 0f,
                    usesEnvironmentReflections = true
                });
                settings.Persist();
            }

            EditorGUI.indentLevel--;
        }

        void DrawActions()
        {
            EditorGUILayout.LabelField("Workflow", EditorStyles.boldLabel);

            if (GUILayout.Button("1. Analyze Scene"))
            {
                ReflectionProbeTool.AnalyzeScene();
                _lastMessage = "Report written. Check problems[] for shaders that could not be read.";
            }

            if (GUILayout.Button("2. Accept Recommendations"))
            {
                ReflectionProbeTool.AcceptRecommendations();
                _lastMessage = "Plan written. Edit it by hand, or let an agent adjust resolutions and positions.";
            }

            if (GUILayout.Button("3. Preview Plan"))
            {
                ReflectionProbeTool.PreviewPlan();
                _lastMessage = "Boxes drawn in the Scene view, colour-coded by action. Nothing has been modified.";
            }

            GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
            if (GUILayout.Button("4. Apply Plan"))
            {
                ReflectionProbeTool.ApplyPlan();
                _lastMessage = "Applied. Probes placed by hand were left untouched.";
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("5. Bake Probes"))
            {
                ReflectionProbeTool.BakeProbes();
                _lastMessage = "Baked. Cubemaps are under Assets/ReflectionProbes.";
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Revert Last Apply")) ReflectionProbeTool.RevertLastApply();
            if (GUILayout.Button("Clear Preview")) ReflectionProbeTool.ClearPreview();
            EditorGUILayout.EndHorizontal();
        }
    }
}
