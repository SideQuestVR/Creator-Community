// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// A voxelisation of the scene into solid and free space.
    ///
    /// This is the substrate for everything spatial in the suite: which cells a probe may
    /// occupy, how rooms connect, how wide a doorway is, and whether a reflection probe
    /// has ended up inside a wall. Cell size comes from SceneScale, so it adapts to the
    /// content rather than assuming a room-sized world.
    ///
    /// Occupancy is read from colliders where they exist, because colliders describe the
    /// space a player can actually occupy. A scene with no colliders falls back to
    /// renderer bounds, which is coarser - an L-shaped room reads as a filled rectangle -
    /// so the fallback is reported rather than applied silently.
    /// </summary>
    public sealed class OccupancyGrid
    {
        /// <summary>
        /// Ceiling on total voxels. Beyond this the grid coarsens itself rather than
        /// allocating: a 1km scene at 1m cells would otherwise ask for a billion cells and
        /// take the Editor down with it.
        /// </summary>
        public const int MaxCells = 4_000_000;

        public Bounds WorldBounds { get; private set; }
        public float CellSize { get; private set; }
        public int SizeX { get; private set; }
        public int SizeY { get; private set; }
        public int SizeZ { get; private set; }

        /// <summary>True when colliders were unavailable and renderer bounds were used instead.</summary>
        public bool UsedRendererFallback { get; private set; }

        public int SolidCount { get; private set; }
        public int FreeCount { get { return CellCount - SolidCount; } }
        public int CellCount { get { return SizeX * SizeY * SizeZ; } }

        bool[] _solid;

        public static OccupancyGrid Build(Bounds bounds, float cellSize, IList<RendererFacts> renderers)
        {
            var grid = new OccupancyGrid();

            // Pad by one cell so the outermost surfaces have free space on both sides and
            // a boundary test does not read off the end of the array.
            bounds.Expand(cellSize * 2f);
            grid.WorldBounds = bounds;
            grid.CellSize = ChooseCellSize(bounds, cellSize);

            grid.SizeX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / grid.CellSize));
            grid.SizeY = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / grid.CellSize));
            grid.SizeZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / grid.CellSize));
            grid._solid = new bool[grid.CellCount];

            bool anyCollider = HasAnyCollider(renderers);
            grid.UsedRendererFallback = !anyCollider;

            if (anyCollider) grid.FillFromColliders();
            else grid.FillFromRendererBounds(renderers);

            return grid;
        }

        /// <summary>Coarsens the requested cell size until the grid fits inside MaxCells.</summary>
        static float ChooseCellSize(Bounds bounds, float requested)
        {
            float cell = Mathf.Max(requested, 0.05f);

            for (int attempt = 0; attempt < 16; attempt++)
            {
                double x = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cell));
                double y = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cell));
                double z = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cell));

                if (x * y * z <= MaxCells) return cell;
                cell *= 1.5f;
            }

            return cell;
        }

        static bool HasAnyCollider(IList<RendererFacts> renderers)
        {
            if (renderers == null) return false;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i].GameObject != null && renderers[i].GameObject.GetComponent<Collider>() != null)
                    return true;
            }
            return false;
        }

        void FillFromColliders()
        {
            // The test box is shrunk slightly so a collider that merely touches a cell
            // boundary does not solidify the cell next to it - that would seal doorways
            // that are exactly one cell wide.
            Vector3 half = Vector3.one * (CellSize * 0.45f);

            for (int x = 0; x < SizeX; x++)
                for (int y = 0; y < SizeY; y++)
                    for (int z = 0; z < SizeZ; z++)
                    {
                        if (!Physics.CheckBox(CellCenter(x, y, z), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                            continue;

                        _solid[IndexOf(x, y, z)] = true;
                        SolidCount++;
                    }
        }

        void FillFromRendererBounds(IList<RendererFacts> renderers)
        {
            if (renderers == null) return;

            for (int i = 0; i < renderers.Count; i++)
            {
                RendererFacts r = renderers[i];
                if (!r.HasBounds) continue;

                Vector3Int min = ClampCell(WorldToCellUnclamped(r.WorldBounds.min));
                Vector3Int max = ClampCell(WorldToCellUnclamped(r.WorldBounds.max));

                for (int x = min.x; x <= max.x; x++)
                    for (int y = min.y; y <= max.y; y++)
                        for (int z = min.z; z <= max.z; z++)
                        {
                            int index = IndexOf(x, y, z);
                            if (_solid[index]) continue;
                            _solid[index] = true;
                            SolidCount++;
                        }
            }
        }

        // ---- addressing ----

        public int IndexOf(int x, int y, int z) { return (x * SizeY + y) * SizeZ + z; }

        public bool InRange(int x, int y, int z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ;
        }

        public bool IsSolid(int x, int y, int z)
        {
            // Out of range counts as solid, so flood fills stop at the grid edge instead
            // of needing a bounds check at every neighbour.
            if (!InRange(x, y, z)) return true;
            return _solid[IndexOf(x, y, z)];
        }

        public bool IsFree(int x, int y, int z) { return !IsSolid(x, y, z); }

        public Vector3 CellCenter(int x, int y, int z)
        {
            return new Vector3(
                WorldBounds.min.x + (x + 0.5f) * CellSize,
                WorldBounds.min.y + (y + 0.5f) * CellSize,
                WorldBounds.min.z + (z + 0.5f) * CellSize);
        }

        Vector3Int WorldToCellUnclamped(Vector3 world)
        {
            return new Vector3Int(
                Mathf.FloorToInt((world.x - WorldBounds.min.x) / CellSize),
                Mathf.FloorToInt((world.y - WorldBounds.min.y) / CellSize),
                Mathf.FloorToInt((world.z - WorldBounds.min.z) / CellSize));
        }

        public Vector3Int WorldToCell(Vector3 world) { return ClampCell(WorldToCellUnclamped(world)); }

        Vector3Int ClampCell(Vector3Int cell)
        {
            return new Vector3Int(
                Mathf.Clamp(cell.x, 0, SizeX - 1),
                Mathf.Clamp(cell.y, 0, SizeY - 1),
                Mathf.Clamp(cell.z, 0, SizeZ - 1));
        }

        // ---- queries ----

        /// <summary>
        /// Nearest free cell centre to a world position, searching outward in shells.
        ///
        /// This is how a reflection probe escapes geometry. A probe baked inside a wall is
        /// the single most common way reflection probes look wrong, and it is invisible in
        /// the Editor until the cubemap comes back black.
        /// </summary>
        public bool TryFindNearestFree(Vector3 world, int maxShellRadius, out Vector3 result)
        {
            Vector3Int c = WorldToCell(world);
            result = world;

            if (IsFree(c.x, c.y, c.z)) { result = CellCenter(c.x, c.y, c.z); return true; }

            for (int r = 1; r <= maxShellRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dz = -r; dz <= r; dz++)
                        {
                            // Shell only: skip the interior already searched.
                            if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r && Mathf.Abs(dz) != r) continue;

                            int x = c.x + dx, y = c.y + dy, z = c.z + dz;
                            if (!InRange(x, y, z)) continue;
                            if (IsSolid(x, y, z)) continue;

                            result = CellCenter(x, y, z);
                            return true;
                        }
            }

            return false;
        }

        /// <summary>
        /// Fraction of a cell set's boundary faces that open onto free space.
        ///
        /// This is what separates "room" from "courtyard": an enclosed room is bounded
        /// almost entirely by solid cells, while an open area is bounded by more free
        /// space. Probe density and reflection probe sizing both key off it.
        /// </summary>
        public float ComputeOpenness(IList<Vector3Int> cells)
        {
            if (cells == null || cells.Count == 0) return 0f;

            var member = new HashSet<int>();
            for (int i = 0; i < cells.Count; i++) member.Add(IndexOf(cells[i].x, cells[i].y, cells[i].z));

            int boundaryFaces = 0;
            int openFaces = 0;

            for (int i = 0; i < cells.Count; i++)
            {
                Vector3Int c = cells[i];
                for (int face = 0; face < 6; face++)
                {
                    Vector3Int n = Neighbour(c, face);
                    if (InRange(n.x, n.y, n.z) && member.Contains(IndexOf(n.x, n.y, n.z))) continue;

                    boundaryFaces++;
                    if (InRange(n.x, n.y, n.z) && IsFree(n.x, n.y, n.z)) openFaces++;
                }
            }

            return boundaryFaces == 0 ? 0f : (float)openFaces / boundaryFaces;
        }

        public static Vector3Int Neighbour(Vector3Int c, int face)
        {
            switch (face)
            {
                case 0: return new Vector3Int(c.x + 1, c.y, c.z);
                case 1: return new Vector3Int(c.x - 1, c.y, c.z);
                case 2: return new Vector3Int(c.x, c.y + 1, c.z);
                case 3: return new Vector3Int(c.x, c.y - 1, c.z);
                case 4: return new Vector3Int(c.x, c.y, c.z + 1);
                default: return new Vector3Int(c.x, c.y, c.z - 1);
            }
        }
    }
}
