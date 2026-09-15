// SideQuest Lighting Tools - MIT
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Every scene mutation in the suite goes through here.
    ///
    /// The prior-art tools recorded undo in the hand-painting path and nowhere else, so
    /// the automatic placers silently overwrote work with no way back and no dirty flag -
    /// meaning the change could also be lost by closing without saving. Routing all
    /// mutation through one type makes that structurally impossible rather than something
    /// each new code path has to remember.
    /// </summary>
    public static class SqUndo
    {
        /// <summary>
        /// Wraps a batch of mutations so the whole apply collapses into a single Ctrl+Z.
        ///
        /// A plan that placed 400 probes and reconfigured 12 renderers must undo as one
        /// step; 412 undo entries is functionally the same as no undo at all.
        /// </summary>
        /// <remarks>
        /// A class, not a struct: a using-statement local is read-only, so a mutating
        /// Dispose on a struct would operate on a defensive copy and the disposed flag
        /// would never stick.
        /// </remarks>
        public sealed class Scope : IDisposable
        {
            readonly int _group;
            bool _disposed;

            internal Scope(string label)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(label);
                _group = Undo.GetCurrentGroup();
                _disposed = false;
            }

            public int GroupId { get { return _group; } }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Undo.CollapseUndoOperations(_group);
            }
        }

        public static Scope Group(string label)
        {
            return new Scope(string.IsNullOrEmpty(label) ? "SideQuest Lighting" : label);
        }

        /// <summary>Record a component or asset before changing its serialized fields.</summary>
        public static void Modify(Object target, string label)
        {
            if (target == null) return;
            Undo.RecordObject(target, label);
            EditorUtility.SetDirty(target);
            MarkSceneDirty(target);
        }

        /// <summary>
        /// Record a GameObject before changing static flags, name or hierarchy.
        /// RecordObject is not enough for these: static flags live on the GameObject
        /// itself and only RegisterCompleteObjectUndo captures them.
        /// </summary>
        public static void ModifyGameObject(GameObject go, string label)
        {
            if (go == null) return;
            Undo.RegisterCompleteObjectUndo(go, label);
            EditorUtility.SetDirty(go);
            MarkSceneDirty(go);
        }

        public static T AddComponent<T>(GameObject go, string label) where T : Component
        {
            if (go == null) return null;
            T component = Undo.AddComponent<T>(go);
            MarkSceneDirty(go);
            return component;
        }

        public static GameObject CreateGameObject(string name, Transform parent, string label)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, label);
            if (parent != null) Undo.SetTransformParent(go.transform, parent, label);
            MarkSceneDirty(go);
            return go;
        }

        public static void DestroyGameObject(GameObject go, string label)
        {
            if (go == null) return;
            Scene scene = go.scene;
            Undo.DestroyObjectImmediate(go);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
        }

        static void MarkSceneDirty(Object target)
        {
            var component = target as Component;
            GameObject go = component != null ? component.gameObject : target as GameObject;
            if (go == null) return;

            Scene scene = go.scene;
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
