// SideQuest Lighting Tools - MIT
using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Stable identity for a scene object across a report, a plan and a result.
    ///
    /// Hierarchy paths are the obvious choice and the wrong one: renaming an object or
    /// having two siblings with the same name silently retargets a plan onto something
    /// the agent never looked at. GlobalObjectId survives renames, reparenting and
    /// duplication.
    ///
    /// The cost is length - roughly 80 characters each, so 500 of them is 40 KB of noise
    /// in a report an LLM has to read. That is why reports index objects by integer and
    /// carry the id table separately, omitting it entirely at summary detail.
    /// </summary>
    public struct SqObjectId : IEquatable<SqObjectId>
    {
        public readonly string Value;

        public SqObjectId(string value) { Value = value; }

        public bool IsValid { get { return !string.IsNullOrEmpty(Value); } }

        public static SqObjectId Of(Object target)
        {
            if (target == null) return default(SqObjectId);
            return new SqObjectId(GlobalObjectId.GetGlobalObjectIdSlow(target).ToString());
        }

        /// <summary>
        /// Resolves back to a live object, or null. Returns null for anything that is not
        /// currently loaded - a plan may only touch objects in the open scene, and this is
        /// where that is enforced in practice.
        /// </summary>
        public Object Resolve()
        {
            if (!IsValid) return null;

            GlobalObjectId parsed;
            if (!GlobalObjectId.TryParse(Value, out parsed)) return null;

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
        }

        public T Resolve<T>() where T : Object
        {
            Object resolved = Resolve();
            if (resolved == null) return null;

            var direct = resolved as T;
            if (direct != null) return direct;

            // A GlobalObjectId for a component resolves to the component, but a plan may
            // reasonably name the GameObject and want a Renderer, or vice versa.
            var go = resolved as GameObject;
            if (go != null) return go.GetComponent<T>();

            var component = resolved as Component;
            if (component != null)
            {
                if (typeof(T) == typeof(GameObject)) return component.gameObject as T;
                return component.GetComponent<T>();
            }

            return null;
        }

        public bool Equals(SqObjectId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SqObjectId && Equals((SqObjectId)obj);
        }

        public override int GetHashCode()
        {
            return Value != null ? Value.GetHashCode() : 0;
        }

        public override string ToString() { return Value ?? string.Empty; }
    }
}
