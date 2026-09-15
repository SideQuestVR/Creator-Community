// SideQuest Lighting Tools - MIT
using System;
using UnityEditor;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// Everything the four tools need to know about one renderer, gathered in a single
    /// pass so no tool pays to walk the scene again.
    ///
    /// Held by index within a scan. GlobalObjectIds are resolved lazily and only for the
    /// handful of objects a report actually names, because eighty characters each adds up
    /// to more bytes than the rest of the report combined.
    /// </summary>
    public sealed class RendererFacts
    {
        public int Index;
        public Renderer Renderer;
        public GameObject GameObject;
        public string Name;

        public bool HasBounds;
        public Bounds WorldBounds;

        public Mesh SharedMesh;
        public int TriangleCount;

        /// <summary>
        /// Bounds surface area, not true mesh area. Ten times cheaper, stable under
        /// scaling, and the downstream uses - reflection weighting and lightmap texel
        /// budgeting - only need the right order of magnitude.
        /// </summary>
        public float SurfaceArea;

        public bool HasUv2;
        public bool IsSkinned;

        /// <summary>True if this renderer or a parent has a Rigidbody or Animator, so it may move.</summary>
        public bool MayMove;

        public StaticEditorFlags StaticFlags;
        public bool ContributeGI;
        public bool OccluderStatic;
        public bool OccludeeStatic;
        public bool BatchingStatic;
        public bool IsStatic;

        public MaterialFacts[] Materials = new MaterialFacts[0];

        /// <summary>The material contributing the most reflection weight, or the first one.</summary>
        public MaterialFacts Dominant;

        public bool AllOpaque = true;
        public bool AnyDoubleSided;
        public bool AnyMissingMaterial;
        public float MaxSmoothness;
        public float ReflectionWeight;

        /// <summary>
        /// Second-largest bounds axis. This, not overall size, is what decides whether
        /// something can hide anything: a wall is thin but blocks a room, a pipe is long
        /// but blocks nothing.
        /// </summary>
        public float OccluderFaceSize;

        /// <summary>
        /// Smallest bounds axis. An occluder needs some thickness: Umbra voxelises the
        /// scene, and a surface thinner than a voxel is either dropped or fattened to fill
        /// one, the second of which invents occlusion that is not there.
        /// </summary>
        public float OccluderThickness;

        public int ZoneId = -1;

        SqObjectId _id;
        bool _idResolved;

        /// <summary>Resolved on first use - see the class remarks on cost.</summary>
        public SqObjectId Id
        {
            get
            {
                if (!_idResolved)
                {
                    _id = SqObjectId.Of(GameObject);
                    _idResolved = true;
                }
                return _id;
            }
        }

        public static RendererFacts Read(Renderer renderer, int index)
        {
            var f = new RendererFacts();
            f.Index = index;
            f.Renderer = renderer;
            f.GameObject = renderer.gameObject;
            f.Name = renderer.gameObject.name;

            ReadBounds(renderer, f);
            ReadMesh(renderer, f);
            ReadStaticFlags(f);
            ReadMaterials(renderer, f);
            ReadMovement(renderer, f);

            return f;
        }

        static void ReadBounds(Renderer renderer, RendererFacts f)
        {
            Bounds b = renderer.bounds;
            Vector3 size = b.size;

            // A renderer that has never been culled can report a degenerate bounds.
            // Treating that as real would poison every percentile derived from it.
            f.HasBounds = size.x > 0f || size.y > 0f || size.z > 0f;
            if (!f.HasBounds) return;

            f.WorldBounds = b;
            f.SurfaceArea = 2f * (size.x * size.y + size.y * size.z + size.z * size.x);

            // Sort the three axes. The middle one says whether the object has a substantial
            // face, and the smallest says how thick it is - both matter for occlusion, and
            // for opposite reasons.
            float a = size.x, c = size.y, d = size.z;
            float lo = Mathf.Min(a, Mathf.Min(c, d));
            float hi = Mathf.Max(a, Mathf.Max(c, d));

            f.OccluderThickness = lo;
            f.OccluderFaceSize = a + c + d - lo - hi;
        }

        static void ReadMesh(Renderer renderer, RendererFacts f)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                f.IsSkinned = true;
                f.SharedMesh = skinned.sharedMesh;
            }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) f.SharedMesh = filter.sharedMesh;
            }

            if (f.SharedMesh == null) return;

            // vertexCount is free; triangle indices are not, so derive from index count.
            try
            {
                f.TriangleCount = (int)(SafeIndexCount(f.SharedMesh) / 3);
            }
            catch (Exception)
            {
                f.TriangleCount = 0;
            }

            // uv2 allocates, so ask for the channel's existence rather than the array.
            f.HasUv2 = f.SharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1);
        }

        static uint SafeIndexCount(Mesh mesh)
        {
            uint total = 0;
            int subMeshes = mesh.subMeshCount;
            for (int i = 0; i < subMeshes; i++) total += mesh.GetIndexCount(i);
            return total;
        }

        static void ReadStaticFlags(RendererFacts f)
        {
            f.StaticFlags = GameObjectUtility.GetStaticEditorFlags(f.GameObject);
            f.ContributeGI = (f.StaticFlags & StaticEditorFlags.ContributeGI) != 0;
            f.OccluderStatic = (f.StaticFlags & StaticEditorFlags.OccluderStatic) != 0;
            f.OccludeeStatic = (f.StaticFlags & StaticEditorFlags.OccludeeStatic) != 0;
            f.BatchingStatic = (f.StaticFlags & StaticEditorFlags.BatchingStatic) != 0;
            f.IsStatic = f.GameObject.isStatic || f.StaticFlags != 0;
        }

        static void ReadMaterials(Renderer renderer, RendererFacts f)
        {
            Material[] shared = renderer.sharedMaterials;
            if (shared == null || shared.Length == 0) return;

            f.Materials = new MaterialFacts[shared.Length];
            float bestWeight = -1f;

            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null) f.AnyMissingMaterial = true;

                MaterialFacts m = MaterialFacts.Read(shared[i]);
                f.Materials[i] = m;

                if (!m.IsOpaque) f.AllOpaque = false;
                if (m.DoubleSided) f.AnyDoubleSided = true;
                if (m.Smoothness > f.MaxSmoothness) f.MaxSmoothness = m.Smoothness;

                // Each submaterial is weighted by an equal share of the renderer's area.
                float share = f.SurfaceArea / shared.Length;
                float weight = m.ReflectionWeight(share);
                f.ReflectionWeight += weight;

                if (weight > bestWeight) { bestWeight = weight; f.Dominant = m; }
            }

            if (f.Dominant == null) f.Dominant = f.Materials[0];
        }

        static void ReadMovement(Renderer renderer, RendererFacts f)
        {
            // Occlusion flags on something that moves are worse than useless: the bake
            // records where it was, and the object then culls against stale geometry.
            if (renderer.GetComponentInParent<Rigidbody>() != null) { f.MayMove = true; return; }
            if (renderer.GetComponentInParent<Animator>() != null) { f.MayMove = true; return; }
            if (f.IsSkinned) f.MayMove = true;
        }

        /// <summary>
        /// Whether this renderer can usefully hide other geometry.
        ///
        /// Deliberately not a size test. The prior-art tool disqualified anything whose
        /// bounds magnitude was small, which rejects every thin wall - the one thing that
        /// occludes best - while accepting long thin props that occlude nothing.
        /// </summary>
        public bool IsViableOccluder(float minOccluderFaceSize, float minOccluderThickness = 0f)
        {
            if (!HasBounds || MayMove || IsSkinned || !IsStatic) return false;
            if (OccluderFaceSize < minOccluderFaceSize) return false;

            // A flat plane has a huge face and no substance. It cannot be voxelised into a
            // solid, so treating it as an occluder produces occlusion that does not match
            // the geometry - most visibly as objects vanishing at close range.
            if (OccluderThickness < minOccluderThickness) return false;
            if (!AllOpaque || AnyMissingMaterial) return false;
            if (AnyDoubleSided) return false;

            // Alpha-clipped foliage is opaque by RenderType but full of holes.
            for (int i = 0; i < Materials.Length; i++)
            {
                if (Materials[i] == null) return false;
                if (string.Equals(Materials[i].SurfaceType, "cutout", StringComparison.Ordinal)) return false;
            }

            return true;
        }
    }
}
