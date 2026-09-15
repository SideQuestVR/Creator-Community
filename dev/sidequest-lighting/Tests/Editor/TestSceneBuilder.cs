// SideQuest Lighting Tools - MIT
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SideQuest.LightingTools.Tests
{
    /// <summary>
    /// Generates the fixture scenes the suite is tested against.
    ///
    /// Written as a generator rather than committed .unity files for two reasons. A scene
    /// asset is an opaque diff, so a change to the fixture would be unreviewable; and the
    /// interesting properties here are deliberate traps - a probe group on a rotated,
    /// off-origin transform, doorways narrow enough to need real erosion, gloss spread
    /// across shaders that do and do not sample probes. Those are far clearer stated as
    /// code than buried in serialised YAML.
    ///
    /// These scenes are not shipped. They live outside the package tree, and the build
    /// script copies only Assets/SideQuest/LightingTools.
    /// </summary>
    public static class TestSceneBuilder
    {
        const string SceneFolder = "Assets/SideQuestTests/Scenes";
        const string MaterialFolder = "Assets/SideQuestTests/Materials";

        const float RoomWidth = 8f;
        const float RoomDepth = 8f;
        const float RoomHeight = 3f;
        const float WallThickness = 0.2f;

        /// <summary>
        /// Deliberately narrower than the zone voxel size is comfortable with. If the
        /// segmenter cannot resolve this, all four rooms merge into one zone and every
        /// per-room decision in the suite collapses - which is exactly the failure this
        /// fixture exists to catch.
        /// </summary>
        const float DoorWidth = 0.9f;

        [MenuItem("Tools/SideQuest/Lighting/Tests/Build T1 Small Room", false, 1)]
        public static void BuildT1()
        {
            Scene scene = NewScene();
            Materials materials = LoadMaterials();

            BuildRoom(Vector3.zero, materials, Doors.None, "Room");
            AddProp(new Vector3(2f, 0.5f, 2f), Vector3.one, materials.Matte, "Crate");
            AddProp(new Vector3(-2f, 0.5f, -1f), new Vector3(1f, 1f, 2f), materials.Glossy, "GlossyBlock");

            AddLight(new Vector3(0f, 2.5f, 0f), LightType.Point, 3f, 12f, "RoomLight");
            AddDirectionalLight();

            Save(scene, "T1_SmallRoom");
        }

        /// <summary>
        /// The regression scene.
        ///
        /// Four rooms around a corridor, connected by doorways at the width real interiors
        /// use. Gloss is spread across a mirror floor, a glass pane and an alpha-clipped
        /// foliage card, so reflection weighting, occluder classification and surface
        /// typing all have something real to get wrong.
        ///
        /// Its point is the Light Probe Group: parented to a transform that is both moved
        /// and rotated. The prior-art placer wrote world positions straight into
        /// probePositions, which is local space, and passed its own testing solely because
        /// the group sat at the origin. Here that bug misplaces every probe.
        /// </summary>
        [MenuItem("Tools/SideQuest/Lighting/Tests/Build T2 Multi Room", false, 2)]
        public static void BuildT2()
        {
            Scene scene = NewScene();
            Materials materials = LoadMaterials();

            float pitch = RoomWidth + WallThickness;

            // A ring: A-B, A-C, B-D, C-D. Each doorway is declared on both rooms that
            // share the wall, so the four zones end up genuinely connected.
            BuildRoom(new Vector3(0f, 0f, 0f), materials, Doors.Of(true, false, true, false), "RoomA");
            BuildRoom(new Vector3(pitch, 0f, 0f), materials, Doors.Of(true, false, false, true), "RoomB");
            BuildRoom(new Vector3(0f, 0f, pitch), materials, Doors.Of(false, true, true, false), "RoomC");
            BuildRoom(new Vector3(pitch, 0f, pitch), materials, Doors.Of(false, true, false, true), "RoomD");

            // Room A: a mirror floor. The strongest reflection weight in the scene, so it
            // should attract the highest-resolution probe.
            AddProp(new Vector3(0f, 0.02f, 0f), new Vector3(RoomWidth - 1f, 0.04f, RoomDepth - 1f),
                materials.Mirror, "RoomA_MirrorFloor");

            // Room B: a glass pane. Transparent, so it is an occludee and never an occluder.
            AddProp(new Vector3(pitch, 1.5f, 0f), new Vector3(3f, 2f, 0.05f),
                materials.Glass, "RoomB_GlassPane");

            // Room C: alpha-clipped foliage. Opaque by RenderType and full of holes, which
            // is exactly the case a size-based occluder test gets wrong.
            AddProp(new Vector3(0f, 1.2f, pitch), new Vector3(2f, 2.4f, 0.02f),
                materials.Foliage, "RoomC_FoliageCard");

            // Room D: matte only, to confirm it is NOT given a reflection probe.
            AddProp(new Vector3(pitch - 2f, 0.5f, pitch), Vector3.one, materials.Matte, "RoomD_Crate1");
            AddProp(new Vector3(pitch + 2f, 0.5f, pitch + 1f), new Vector3(1.5f, 1f, 1f), materials.Matte, "RoomD_Crate2");

            // A prop that can move: should have its occlusion flags cleared and be refused
            // Contribute GI, however large it is.
            GameObject mover = AddProp(new Vector3(2f, 0.5f, 2f), Vector3.one * 1.5f, materials.Matte, "MovingCrate");
            mover.AddComponent<Rigidbody>();

            AddDirectionalLight();
            AddLight(new Vector3(0f, 2.5f, 0f), LightType.Point, 4f, 10f, "LightA");
            AddLight(new Vector3(pitch, 2.5f, 0f), LightType.Point, 2f, 8f, "LightB");
            AddLight(new Vector3(0f, 2.5f, pitch), LightType.Spot, 6f, 12f, "LightC");
            AddLight(new Vector3(pitch, 2.5f, pitch), LightType.Point, 1.5f, 6f, "LightD");

            AddRotatedProbeGroup();

            Save(scene, "T2_MultiRoom");
        }

        [MenuItem("Tools/SideQuest/Lighting/Tests/Build T3 Stress", false, 3)]
        public static void BuildT3()
        {
            Scene scene = NewScene();
            Materials materials = LoadMaterials();

            // Fixed seed: report size and timing assertions have to be comparable between
            // runs, which they are not if the scene changes every time it is built.
            Random.InitState(20260915);

            const int gridSize = 5;
            float pitch = RoomWidth + WallThickness;

            for (int x = 0; x < gridSize; x++)
                for (int z = 0; z < gridSize; z++)
                {
                    BuildRoom(new Vector3(x * pitch, 0f, z * pitch), materials,
                        Doors.Of(z < gridSize - 1, z > 0, x < gridSize - 1, x > 0),
                        string.Format("Room_{0}_{1}", x, z));
                }

            for (int i = 0; i < 600; i++)
            {
                var position = new Vector3(
                    Random.Range(-RoomWidth * 0.5f, (gridSize - 0.5f) * pitch),
                    Random.Range(0.25f, 2f),
                    Random.Range(-RoomDepth * 0.5f, (gridSize - 0.5f) * pitch));

                Material material = Random.value < 0.25f ? materials.Glossy : materials.Matte;
                AddProp(position, Vector3.one * Random.Range(0.3f, 1.5f), material, "Prop_" + i);
            }

            AddDirectionalLight();
            for (int i = 0; i < 40; i++)
            {
                AddLight(
                    new Vector3(Random.Range(0f, gridSize * pitch), 2.5f, Random.Range(0f, gridSize * pitch)),
                    LightType.Point, Random.Range(1f, 5f), Random.Range(5f, 15f), "Light_" + i);
            }

            Save(scene, "T3_Stress");
        }

        [MenuItem("Tools/SideQuest/Lighting/Tests/Build All", false, 20)]
        public static void BuildAll()
        {
            BuildT1();
            BuildT2();
            BuildT3();
            Debug.Log("[SQLT-TEST] built T1, T2 and T3 under " + SceneFolder);
        }

        /// <summary>Which walls of a room have a doorway. Shared walls must agree on both sides.</summary>
        struct Doors
        {
            public bool North, South, East, West;

            public static Doors None { get { return default(Doors); } }

            public static Doors Of(bool north, bool south, bool east, bool west)
            {
                return new Doors { North = north, South = south, East = east, West = west };
            }
        }

        // ---- construction helpers ----

        static Scene NewScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void Save(Scene scene, string name)
        {
            EnsureFolder(SceneFolder);
            string path = SceneFolder + "/" + name + ".unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[SQLT-TEST] saved " + path);
        }

        /// <summary>
        /// Builds one closed room, optionally with doorways north and east.
        ///
        /// A doorway is a gap between two wall segments rather than a hole in one wall:
        /// nothing here generates meshes, and two cubes with a gap collide exactly the way
        /// a real doorway does, which is what the occupancy grid reads.
        /// </summary>
        static void BuildRoom(Vector3 origin, Materials materials, Doors doors, string name)
        {
            var root = new GameObject(name);
            root.transform.position = origin;

            float halfW = RoomWidth * 0.5f;
            float halfD = RoomDepth * 0.5f;
            float halfH = RoomHeight * 0.5f;

            AddBox(root.transform, new Vector3(0f, -WallThickness * 0.5f, 0f),
                new Vector3(RoomWidth, WallThickness, RoomDepth), materials.Floor, "Floor");

            AddBox(root.transform, new Vector3(0f, RoomHeight + WallThickness * 0.5f, 0f),
                new Vector3(RoomWidth, WallThickness, RoomDepth), materials.Matte, "Ceiling");

            // Every wall is built, including ones shared with a neighbour. A doorway must
            // therefore be punched through BOTH rooms' copies - the first version of this
            // fixture opened a door on one side only, and the neighbour's solid wall sealed
            // it again. Four perfectly separate rooms is a plausible-looking result, which
            // is what made it worth stating explicitly here.
            BuildWallAlongX(root.transform, new Vector3(0f, halfH, halfD), doors.North, materials, "Wall_N");
            BuildWallAlongX(root.transform, new Vector3(0f, halfH, -halfD), doors.South, materials, "Wall_S");
            BuildWallAlongZ(root.transform, new Vector3(halfW, halfH, 0f), doors.East, materials, "Wall_E");
            BuildWallAlongZ(root.transform, new Vector3(-halfW, halfH, 0f), doors.West, materials, "Wall_W");
        }

        static void BuildWallAlongX(Transform parent, Vector3 centre, bool door, Materials materials, string name)
        {
            if (!door)
            {
                AddBox(parent, centre, new Vector3(RoomWidth, RoomHeight, WallThickness), materials.Wall, name);
                return;
            }

            float segment = (RoomWidth - DoorWidth) * 0.5f;
            float offset = (DoorWidth + segment) * 0.5f;

            AddBox(parent, centre + new Vector3(-offset, 0f, 0f),
                new Vector3(segment, RoomHeight, WallThickness), materials.Wall, name + "_a");
            AddBox(parent, centre + new Vector3(offset, 0f, 0f),
                new Vector3(segment, RoomHeight, WallThickness), materials.Wall, name + "_b");
        }

        static void BuildWallAlongZ(Transform parent, Vector3 centre, bool door, Materials materials, string name)
        {
            if (!door)
            {
                AddBox(parent, centre, new Vector3(WallThickness, RoomHeight, RoomDepth), materials.Wall, name);
                return;
            }

            float segment = (RoomDepth - DoorWidth) * 0.5f;
            float offset = (DoorWidth + segment) * 0.5f;

            AddBox(parent, centre + new Vector3(0f, 0f, -offset),
                new Vector3(WallThickness, RoomHeight, segment), materials.Wall, name + "_a");
            AddBox(parent, centre + new Vector3(0f, 0f, offset),
                new Vector3(WallThickness, RoomHeight, segment), materials.Wall, name + "_b");
        }

        static GameObject AddBox(Transform parent, Vector3 localPosition, Vector3 size, Material material, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;

            // Static by default: occlusion and GI both need it, and a fixture that is not
            // static would silently exercise none of the static-flag logic.
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
            return go;
        }

        static GameObject AddProp(Vector3 position, Vector3 size, Material material, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
            return go;
        }

        static void AddDirectionalLight()
        {
            var go = new GameObject("DirectionalLight");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        static void AddLight(Vector3 position, LightType type, float intensity, float range, string name)
        {
            var go = new GameObject(name);
            go.transform.position = position;

            Light light = go.AddComponent<Light>();
            light.type = type;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
            light.lightmapBakeType = LightmapBakeType.Baked;

            if (type == LightType.Spot)
            {
                light.spotAngle = 70f;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        /// <summary>
        /// The trap. A probe group whose transform is moved, rotated and non-uniformly
        /// placed, so world and local space differ on every axis.
        /// </summary>
        static void AddRotatedProbeGroup()
        {
            var holder = new GameObject("SQ_LightProbes");
            holder.transform.position = new Vector3(-13.37f, 2.5f, 7.91f);
            holder.transform.rotation = Quaternion.Euler(17f, 143f, -9f);
            holder.AddComponent<LightProbeGroup>();
        }

        // ---- materials ----

        sealed class Materials
        {
            public Material Wall, Floor, Matte, Glossy, Mirror, Glass, Foliage;
        }

        static Materials LoadMaterials()
        {
            EnsureFolder(MaterialFolder);

            return new Materials
            {
                Wall = Lit("Wall", new Color(0.72f, 0.70f, 0.66f), 0.15f, 0f),
                Floor = Lit("Floor", new Color(0.35f, 0.34f, 0.33f), 0.25f, 0f),
                Matte = Lit("Matte", new Color(0.55f, 0.45f, 0.35f), 0.1f, 0f),
                Glossy = Lit("Glossy", new Color(0.4f, 0.45f, 0.5f), 0.75f, 0.4f),
                Mirror = Lit("Mirror", new Color(0.9f, 0.9f, 0.92f), 0.97f, 0.95f),
                Glass = Transparent("Glass", new Color(0.8f, 0.9f, 1f, 0.25f), 0.95f),
                Foliage = Cutout("Foliage", new Color(0.25f, 0.5f, 0.2f), 0.2f)
            };
        }

        static Material Lit(string name, Color color, float smoothness, float metallic)
        {
            Material material = GetOrCreate(name);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Transparent(string name, Color color, float smoothness)
        {
            Material material = Lit(name, color, smoothness, 0f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.renderQueue = 3000;
            material.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Cutout(string name, Color color, float smoothness)
        {
            Material material = Lit(name, color, smoothness, 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = 2450;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetOrCreate(string name)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[SQLT-TEST] URP Lit shader not found - is this a URP project?");
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string[] parts = assetPath.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
