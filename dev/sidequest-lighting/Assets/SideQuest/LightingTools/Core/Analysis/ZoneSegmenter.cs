// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEngine;

namespace SideQuest.LightingTools.Core
{
    /// <summary>
    /// One spatial region of the scene - roughly, a room.
    ///
    /// Zones are the unit a report reasons about. A 500-renderer scene collapses to
    /// eight or twenty zones, which is the difference between a report an LLM can read
    /// and a data dump it cannot.
    /// </summary>
    public sealed class Zone
    {
        public int Id;
        public Bounds Bounds;
        public List<Vector3Int> Cells = new List<Vector3Int>();

        public int RendererCount;
        public long TriangleCount;
        public float FloorArea;

        /// <summary>0 = fully enclosed room, 1 = open space. Drives probe density and probe sizing.</summary>
        public float Openness;

        public float MeanSmoothness;
        public float MaxSmoothness;
        public float MetallicFraction;
        public float ReflectionWeight;

        public int DirectionalLights, PointLights, SpotLights, AreaLights;
        public bool HasNavMesh;

        /// <summary>Estimated variance of indirect lighting across the zone. High means probes must be denser.</summary>
        public float GradientScore;

        public List<ZoneConnection> Connections = new List<ZoneConnection>();

        public float FloorY { get { return Bounds.min.y; } }
        public int LightCount { get { return DirectionalLights + PointLights + SpotLights + AreaLights; } }
    }

    public struct ZoneConnection
    {
        public int ZoneId;

        /// <summary>Narrowest free width at the passage, in metres. Feeds occlusion's smallestHole.</summary>
        public float GapWidth;
    }

    /// <summary>
    /// Segments free space into zones by erosion.
    ///
    /// A plain flood fill of free space is useless here: any open doorway merges every
    /// room in a building into one component. Eroding free space first closes narrow
    /// passages, so the surviving cores are the rooms; those cores are then grown back
    /// over the full free space to label every cell. The passages that vanished during
    /// erosion are exactly the doorways, and their width is what occlusion culling needs
    /// for smallestHole.
    /// </summary>
    public static class ZoneSegmenter
    {
        /// <summary>Cores smaller than this are absorbed rather than becoming their own zone.</summary>
        public const int MinCoreCells = 8;

        public const int MaxZones = 64;

        public static List<Zone> Segment(OccupancyGrid grid, int erosionPasses = 1)
        {
            var zones = new List<Zone>();
            if (grid == null || grid.CellCount == 0) return zones;

            bool[] core = Erode(grid, erosionPasses);
            int[] labels = LabelCores(grid, core);
            GrowLabels(grid, labels);
            BuildZones(grid, labels, zones);
            FindConnections(grid, labels, zones);

            return zones;
        }

        /// <summary>Removes free cells adjacent to solid, repeatedly. Closes doorways; leaves room cores.</summary>
        static bool[] Erode(OccupancyGrid grid, int passes)
        {
            var current = new bool[grid.CellCount];

            for (int x = 0; x < grid.SizeX; x++)
                for (int y = 0; y < grid.SizeY; y++)
                    for (int z = 0; z < grid.SizeZ; z++)
                        current[grid.IndexOf(x, y, z)] = grid.IsFree(x, y, z);

            for (int pass = 0; pass < passes; pass++)
            {
                var next = new bool[grid.CellCount];

                for (int x = 0; x < grid.SizeX; x++)
                    for (int y = 0; y < grid.SizeY; y++)
                        for (int z = 0; z < grid.SizeZ; z++)
                        {
                            int index = grid.IndexOf(x, y, z);
                            if (!current[index]) continue;

                            bool keep = true;
                            for (int face = 0; face < 6 && keep; face++)
                            {
                                Vector3Int n = OccupancyGrid.Neighbour(new Vector3Int(x, y, z), face);
                                if (!grid.InRange(n.x, n.y, n.z) || !current[grid.IndexOf(n.x, n.y, n.z)])
                                    keep = false;
                            }

                            next[index] = keep;
                        }

                current = next;
            }

            return current;
        }

        /// <summary>Connected components of the eroded cores. -1 means unlabelled.</summary>
        static int[] LabelCores(OccupancyGrid grid, bool[] core)
        {
            var labels = new int[grid.CellCount];
            for (int i = 0; i < labels.Length; i++) labels[i] = -1;

            int nextLabel = 0;
            var queue = new Queue<Vector3Int>();
            var component = new List<int>();

            for (int x = 0; x < grid.SizeX; x++)
                for (int y = 0; y < grid.SizeY; y++)
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        int start = grid.IndexOf(x, y, z);
                        if (!core[start] || labels[start] != -1) continue;
                        if (nextLabel >= MaxZones) return labels;

                        component.Clear();
                        queue.Clear();
                        queue.Enqueue(new Vector3Int(x, y, z));
                        labels[start] = nextLabel;
                        component.Add(start);

                        while (queue.Count > 0)
                        {
                            Vector3Int c = queue.Dequeue();
                            for (int face = 0; face < 6; face++)
                            {
                                Vector3Int n = OccupancyGrid.Neighbour(c, face);
                                if (!grid.InRange(n.x, n.y, n.z)) continue;

                                int index = grid.IndexOf(n.x, n.y, n.z);
                                if (!core[index] || labels[index] != -1) continue;

                                labels[index] = nextLabel;
                                component.Add(index);
                                queue.Enqueue(n);
                            }
                        }

                        // A core of a handful of cells is a nook, not a room. Unlabel it
                        // and let the region growing pass fold it into a real neighbour.
                        if (component.Count < MinCoreCells)
                        {
                            for (int i = 0; i < component.Count; i++) labels[component[i]] = -1;
                        }
                        else
                        {
                            nextLabel++;
                        }
                    }

            return labels;
        }

        /// <summary>
        /// Multi-source BFS from every labelled core over all free cells, so each free
        /// cell joins its nearest room. This is what puts the doorway cells back.
        /// </summary>
        static void GrowLabels(OccupancyGrid grid, int[] labels)
        {
            var queue = new Queue<Vector3Int>();

            for (int x = 0; x < grid.SizeX; x++)
                for (int y = 0; y < grid.SizeY; y++)
                    for (int z = 0; z < grid.SizeZ; z++)
                        if (labels[grid.IndexOf(x, y, z)] >= 0)
                            queue.Enqueue(new Vector3Int(x, y, z));

            while (queue.Count > 0)
            {
                Vector3Int c = queue.Dequeue();
                int label = labels[grid.IndexOf(c.x, c.y, c.z)];

                for (int face = 0; face < 6; face++)
                {
                    Vector3Int n = OccupancyGrid.Neighbour(c, face);
                    if (!grid.InRange(n.x, n.y, n.z)) continue;
                    if (grid.IsSolid(n.x, n.y, n.z)) continue;

                    int index = grid.IndexOf(n.x, n.y, n.z);
                    if (labels[index] != -1) continue;

                    labels[index] = label;
                    queue.Enqueue(n);
                }
            }
        }

        static void BuildZones(OccupancyGrid grid, int[] labels, List<Zone> zones)
        {
            var byLabel = new Dictionary<int, Zone>();

            for (int x = 0; x < grid.SizeX; x++)
                for (int y = 0; y < grid.SizeY; y++)
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        int label = labels[grid.IndexOf(x, y, z)];
                        if (label < 0) continue;

                        Zone zone;
                        if (!byLabel.TryGetValue(label, out zone))
                        {
                            zone = new Zone { Id = label, Bounds = new Bounds(grid.CellCenter(x, y, z), Vector3.zero) };
                            byLabel[label] = zone;
                            zones.Add(zone);
                        }

                        zone.Cells.Add(new Vector3Int(x, y, z));
                        zone.Bounds.Encapsulate(grid.CellCenter(x, y, z));
                    }

            zones.Sort((a, b) => a.Id.CompareTo(b.Id));

            for (int i = 0; i < zones.Count; i++)
            {
                Zone zone = zones[i];
                zone.Bounds.Expand(grid.CellSize);
                zone.Openness = grid.ComputeOpenness(zone.Cells);
                zone.FloorArea = zone.Bounds.size.x * zone.Bounds.size.z;
            }
        }

        /// <summary>
        /// Records which zones touch, and how narrow the passage between them is.
        ///
        /// Gap width is measured as the shortest free run through the boundary cell along
        /// any axis. At a doorway, the run across the door is short while the run through
        /// it is long, so the minimum is the door width - which is exactly the number
        /// occlusion culling needs and the one creators most often get wrong by hand.
        /// </summary>
        static void FindConnections(OccupancyGrid grid, int[] labels, List<Zone> zones)
        {
            var widths = new Dictionary<long, float>();

            for (int x = 0; x < grid.SizeX; x++)
                for (int y = 0; y < grid.SizeY; y++)
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        int a = labels[grid.IndexOf(x, y, z)];
                        if (a < 0) continue;

                        for (int face = 0; face < 6; face++)
                        {
                            Vector3Int n = OccupancyGrid.Neighbour(new Vector3Int(x, y, z), face);
                            if (!grid.InRange(n.x, n.y, n.z)) continue;

                            int b = labels[grid.IndexOf(n.x, n.y, n.z)];
                            if (b < 0 || b == a) continue;

                            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                            float width = MeasureFreeWidth(grid, x, y, z);

                            float existing;
                            if (!widths.TryGetValue(key, out existing) || width < existing)
                                widths[key] = width;
                        }
                    }

            foreach (var kv in widths)
            {
                int a = (int)(kv.Key >> 32);
                int b = (int)(kv.Key & 0xFFFFFFFF);

                Zone za = FindZone(zones, a);
                Zone zb = FindZone(zones, b);
                if (za == null || zb == null) continue;

                za.Connections.Add(new ZoneConnection { ZoneId = b, GapWidth = kv.Value });
                zb.Connections.Add(new ZoneConnection { ZoneId = a, GapWidth = kv.Value });
            }
        }

        static Zone FindZone(List<Zone> zones, int id)
        {
            for (int i = 0; i < zones.Count; i++) if (zones[i].Id == id) return zones[i];
            return null;
        }

        /// <summary>Shortest contiguous free run through a cell, over the three axes, in metres.</summary>
        static float MeasureFreeWidth(OccupancyGrid grid, int x, int y, int z)
        {
            int runX = FreeRun(grid, x, y, z, 1, 0, 0) + FreeRun(grid, x, y, z, -1, 0, 0) + 1;
            int runY = FreeRun(grid, x, y, z, 0, 1, 0) + FreeRun(grid, x, y, z, 0, -1, 0) + 1;
            int runZ = FreeRun(grid, x, y, z, 0, 0, 1) + FreeRun(grid, x, y, z, 0, 0, -1) + 1;

            int shortest = Mathf.Min(runX, Mathf.Min(runY, runZ));
            return shortest * grid.CellSize;
        }

        static int FreeRun(OccupancyGrid grid, int x, int y, int z, int dx, int dy, int dz)
        {
            const int limit = 64;

            int count = 0;
            for (int step = 1; step <= limit; step++)
            {
                int nx = x + dx * step, ny = y + dy * step, nz = z + dz * step;
                if (!grid.InRange(nx, ny, nz) || grid.IsSolid(nx, ny, nz)) break;
                count++;
            }
            return count;
        }
    }
}
