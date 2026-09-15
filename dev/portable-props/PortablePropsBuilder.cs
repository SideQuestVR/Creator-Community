// MIT. Run only in a disposable project containing COMMUNITY_PROP_FIXTURE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BS;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class PortablePropsBuilder
{
    public const string Root = "Assets/CreatorCommunity/";
    public const string Flame = Root + "LighterFlame/LighterFlame.prefab";
    static readonly string[] Names = { "PortableLighter", "BurningStick", "BlamRadio" };
    static void Guard() { if (!File.Exists("COMMUNITY_PROP_FIXTURE")) throw new InvalidOperationException("Disposable fixture marker required."); }
    static GameObject Child(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject(name); if (parent) go.transform.SetParent(parent, false); go.transform.localPosition = position; return go;
    }
    static T Asset<T>(T value, string path) where T : Object
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.Refresh();
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (old) { EditorUtility.CopySerialized(value, old); Object.DestroyImmediate(value); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(value, path); return value;
    }
    static Material Mat(string folder, string name, Color color, float metal = 0f)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
        mat.SetFloat("_Metallic", metal); mat.SetFloat("_Smoothness", metal > 0 ? 0.45f : 0.25f);
        return Asset(mat, folder + "/Materials/" + name + ".mat");
    }
    static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material material, Vector3 angles = default)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localEulerAngles = angles;
        Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = material; return go;
    }
    static void Fields(Component component, params object[] pairs)
    {
        var data = new SerializedObject(component);
        for (int i = 0; i < pairs.Length; i += 2)
        {
            var p = data.FindProperty((string)pairs[i]); if (p == null) throw new Exception(component.GetType().Name + "." + pairs[i]);
            if (pairs[i+1] is bool b) p.boolValue = b; else if (pairs[i+1] is float f) p.floatValue = f; else p.intValue = (int)pairs[i+1];
        }
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    static GameObject Body(string name, float mass)
    {
        var go = new GameObject(name); go.layer = LayerMask.NameToLayer("Grabbable"); if (go.layer < 0) throw new Exception("SDK Grabbable layer missing");
        var body = go.AddComponent<Rigidbody>(); body.mass = mass; body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        go.AddComponent<BSObjectId>().ForceGenerateId(); go.AddComponent<BSWorldObject>();
        Fields(go.AddComponent<BSSyncedObject>(), "syncPosition", true, "syncRotation", true, "takeOwnershipOnCollision", false, "takeOwnershipOnGrab", true, "kinematicIfNotOwned", true);
        return go;
    }
    static GameObject Grip(GameObject root, Vector3 position, float radius, float height, Vector3 angles = default, bool lighter = false)
    {
        var grip = Child("Grab_Handle", root.transform, position); grip.layer = root.layer; grip.transform.localEulerAngles = angles;
        var collider = grip.AddComponent<CapsuleCollider>(); collider.isTrigger = true; collider.radius = radius; collider.height = height;
        grip.AddComponent<BSObjectId>().ForceGenerateId();
        Fields(grip.AddComponent<BSGrabHandle>(), "grabType", 1, "grabRadius", radius);
        Fields(grip.AddComponent<BSHeldEvents>(), "sensitivity", 0.45f, "fireRate", 0.075f, "auto", false, "blockLeftTrigger", lighter, "blockRightTrigger", lighter);
        return grip;
    }
    static ScriptMachine Graph(GameObject host, FlowGraph graph, string path)
    {
        if (!host.GetComponent<Variables>()) host.AddComponent<Variables>();
        var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>(); asset.graph = graph; asset = Asset(asset, path);
        var machine = host.AddComponent<ScriptMachine>(); machine.nest.SwitchToMacro(asset); return machine;
    }
    static GameObject FlameAt(GameObject host, float scale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Flame); if (!prefab) throw new Exception("Tested original flame is missing");
        var visual = Object.Instantiate(prefab, host.transform); visual.name = "FlameVisual"; visual.transform.localPosition = Vector3.zero; visual.transform.localScale = Vector3.one * scale; visual.SetActive(false);
        Variables.Object(host).Set("FlameVisual", visual); Variables.Object(host).Set("Lit", false); return visual;
    }
    static GameObject BuildLighter()
    {
        const string folder = Root + "PortableLighter";
        var metal = Mat(folder, "BrushedMetal", new Color(.49f,.56f,.60f), .75f);
        var red = Mat(folder, "Casing", new Color(.4f,.045f,.075f), .35f);
        var black = Mat(folder, "DarkMetal", new Color(.055f,.065f,.075f), .6f);
        var root = Body("Creator Portable Lighter", .13f);
        var physical = root.AddComponent<BoxCollider>(); physical.center = new Vector3(0,.05f,0); physical.size = new Vector3(.05f,.10f,.025f);
        Primitive("Body", PrimitiveType.Cube, root.transform, new Vector3(0,.039f,0), new Vector3(.05f,.078f,.025f), red);
        Primitive("MetalHead", PrimitiveType.Cube, root.transform, new Vector3(0,.09f,0), new Vector3(.042f,.024f,.023f), metal);
        Primitive("OpenLid", PrimitiveType.Cube, root.transform, new Vector3(-.043f,.093f,0), new Vector3(.04f,.009f,.029f), metal, new Vector3(0,0,-25));
        Primitive("StrikerWheel", PrimitiveType.Cylinder, root.transform, new Vector3(-.010f,.108f,0), new Vector3(.011f,.011f,.011f), black, new Vector3(90,0,0));
        var grip = Grip(root, new Vector3(0,.044f,0), .026f,.08f, lighter:true);
        var source = Child("Portable_Ignition_Source_Lighter", root.transform, new Vector3(.009f,.105f,0)); source.AddComponent<Variables>();
        var tip = source.AddComponent<SphereCollider>(); tip.isTrigger=true; tip.radius=.03f;
        FlameAt(source,1f); var vars=Variables.Object(source); vars.Set("Grip",grip); vars.Set("Held",false); vars.Set("HeldLeft",false); vars.Set("TriggerDown",false);
        var click=source.AddComponent<AudioSource>(); click.playOnAwake=false; click.volume=.16f; click.spatialBlend=1; click.minDistance=.2f; click.maxDistance=4;
        click.clip=CreateAudio(folder+"/Audio/LighterClick.wav",true); vars.Set("Click",click);
        Graph(source,PortablePropGraphs.Lighter(),folder+"/VisualScripting/Lighter.asset");
        return root;
    }
    static Mesh StickMesh()
    {
        var v=new List<Vector3>(); var t=new List<int>(); const int rings=7,sides=8;
        for(int r=0;r<rings;r++) for(int s=0;s<sides;s++)
        {
            float u=r/(float)(rings-1),a=s*Mathf.PI*2/sides,rad=Mathf.Lerp(.031f,.021f,u)*(1+.1f*Mathf.Sin(u*Mathf.PI*3));
            v.Add(new Vector3(.018f*Mathf.Sin(u*Mathf.PI*1.4f)+Mathf.Cos(a)*rad,.012f*Mathf.Sin(u*Mathf.PI*2.2f+.6f)+Mathf.Sin(a)*rad,Mathf.Lerp(-.46f,.46f,u)));
        }
        for(int r=0;r<rings-1;r++)for(int s=0;s<sides;s++){int a=r*sides+s,b=r*sides+(s+1)%sides,c=a+sides,d=b+sides;t.AddRange(new[]{a,b,c,b,d,c});}
        for(int end=0;end<2;end++){int ring=end==0?0:rings-1;Vector3 c=Vector3.zero;for(int s=0;s<sides;s++)c+=v[ring*sides+s];int center=v.Count;v.Add(c/sides);for(int s=0;s<sides;s++){int a=ring*sides+s,b=ring*sides+(s+1)%sides;t.AddRange(end==0?new[]{center,b,a}:new[]{center,a,b});}}
        var mesh=new Mesh{name="Original closed campfire stick"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static GameObject BuildStick()
    {
        const string folder=Root+"BurningStick";
        var root=Body("Creator Burning Stick",.22f);var physical=root.AddComponent<CapsuleCollider>();physical.direction=2;physical.radius=.037f;physical.height=.96f;
        var mesh=Asset(StickMesh(),folder+"/Meshes/Stick.asset");var wood=Mat(folder,"Wood",new Color(.23f,.105f,.045f));
        var visual=Child("Stick",root.transform,Vector3.zero);visual.AddComponent<MeshFilter>().sharedMesh=mesh;visual.AddComponent<MeshRenderer>().sharedMaterial=wood;
        Grip(root,Vector3.zero,.05f,.64f,new Vector3(90,0,0));
        var graph=PortablePropGraphs.Stick();
        foreach(int direction in new[]{-1,1})
        {
            var tip=Child("Portable_Ignition_Source_Stick_"+(direction<0?"A":"B"),root.transform,new Vector3(0,0,.47f*direction));tip.AddComponent<Variables>();
            var collider=tip.AddComponent<SphereCollider>();collider.isTrigger=true;collider.radius=.06f;FlameAt(tip,2.4f);
            var vars=Variables.Object(tip);vars.Set("BodyObject",root);vars.Set("Tip",tip.transform);vars.Set("Remaining",0f);vars.Set("BurnSeconds",80f);
            Graph(tip,graph,folder+"/VisualScripting/BurningEnd.asset");
        }
        return root;
    }
    static Mesh HandleMesh()
    {
        // Triangular cross-section extruded along the horizontal carrying bar.
        var cross=new[]{new Vector2(-.012f,-.017f),new Vector2(-.012f,.017f),new Vector2(.016f,0)};
        var v=new List<Vector3>();foreach(float x in new[]{-.27f,.27f})foreach(var p in cross)v.Add(new Vector3(x,p.x,p.y));
        var t=new List<int>{0,2,1,3,4,5};
        for(int i=0;i<3;i++){int j=(i+1)%3;t.AddRange(new[]{i,j,j+3,i,j+3,i+3});}
        for(int i=0;i<t.Count;i+=3){int swap=t[i+1];t[i+1]=t[i+2];t[i+2]=swap;}
        var mesh=new Mesh{name="Original horizontal radio handle"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static GameObject BuildRadio()
    {
        const string folder=Root+"BlamRadio";
        var root=Body("Creator Blam Radio",1.2f);var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,.17f,0);box.size=new Vector3(.64f,.32f,.15f);
        var body=Mat(folder,"Body",new Color(.09f,.14f,.16f),.18f);var dark=Mat(folder,"Speaker",new Color(.02f,.025f,.03f));var metal=Mat(folder,"Trim",new Color(.48f,.53f,.56f),.8f);var red=Mat(folder,"Dial",new Color(.63f,.04f,.075f),.25f);
        Primitive("RadioBody",PrimitiveType.Cube,root.transform,new Vector3(0,.17f,0),new Vector3(.64f,.32f,.15f),body);
        foreach(float x in new[]{-.205f,.205f})
        {
            Primitive("SpeakerTrim",PrimitiveType.Cylinder,root.transform,new Vector3(x,.17f,-.079f),new Vector3(.20f,.008f,.20f),metal,new Vector3(90,0,0));
            Primitive("SpeakerCone",PrimitiveType.Cylinder,root.transform,new Vector3(x,.17f,-.089f),new Vector3(.177f,.005f,.177f),dark,new Vector3(90,0,0));
        }
        Primitive("TuningWindow",PrimitiveType.Cube,root.transform,new Vector3(0,.21f,-.079f),new Vector3(.1f,.025f,.009f),metal);
        Primitive("VolumeDial",PrimitiveType.Cylinder,root.transform,new Vector3(0,.12f,-.09f),new Vector3(.04f,.014f,.04f),red,new Vector3(90,0,0));
        Primitive("Antenna",PrimitiveType.Cylinder,root.transform,new Vector3(.265f,.435f,.02f),new Vector3(.006f,.14f,.006f),metal,new Vector3(0,0,-12));
        foreach(float x in new[]{-.27f,.27f})Primitive("HandleSupport",PrimitiveType.Cube,root.transform,new Vector3(x,.384f,0),new Vector3(.025f,.108f,.034f),metal);
        var handle=Child("HorizontalHandle",root.transform,new Vector3(0,.438f,0));handle.AddComponent<MeshFilter>().sharedMesh=Asset(HandleMesh(),folder+"/Meshes/HorizontalHandle.asset");handle.AddComponent<MeshRenderer>().sharedMaterial=metal;
        // Match Blam's existing boom_grab collider, not the replacement mesh's bounds.
        var grip=Grip(root,new Vector3(0,.438f,0),.02f,.58f,new Vector3(0,0,90));
        grip.GetComponent<CapsuleCollider>().isTrigger=false;
        var audio=Child("Audio",root.transform,new Vector3(0,.15f,-.08f)).AddComponent<AudioSource>();audio.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(folder+"/Audio/BlamTrack.mp3");if(!audio.clip)throw new Exception("The author's radio track is missing");audio.loop=true;audio.playOnAwake=true;audio.spatialBlend=1;audio.volume=.244f;audio.minDistance=.5f;audio.maxDistance=14.5f;audio.rolloffMode=AudioRolloffMode.Logarithmic;audio.dopplerLevel=0;
        root.AddComponent<Variables>();var vars=Variables.Object(root);vars.Set("Root",root.transform);vars.Set("Body",root.GetComponent<Rigidbody>());vars.Set("Sync",root.GetComponent<BSSyncedObject>());vars.Set("Grip",grip);vars.Set("HomePosition",Vector3.zero);vars.Set("HomeRotation",Quaternion.identity);vars.Set("LastActivity",0f);vars.Set("HeldLeft",false);vars.Set("HeldRight",false);vars.Set("ReturnAfterSeconds",300f);
        Graph(root,PortablePropGraphs.Radio(),folder+"/VisualScripting/RadioReturn.asset");return root;
    }
    static AudioClip CreateAudio(string path,bool click)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));const int rate=22050;int count=click?rate/14:rate*4;var rng=new System.Random(3107);
        using(var stream=File.Create(path))using(var w=new BinaryWriter(stream))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(count*2);
            float[] notes={220,261.6256f,329.6276f,293.6648f,220,196,261.6256f,246.9417f};
            for(int i=0;i<count;i++) {double t=i/(double)rate;double sample;if(click)sample=(rng.NextDouble()*2-1)*Math.Exp(-t*80)*.35;else{double phase=t%.5;double env=Math.Sin(Math.PI*Math.Min(phase/.5,1))*.17;sample=env*(Math.Sin(2*Math.PI*notes[Math.Min(7,(int)(t*2))]*t)+.2*Math.Sin(2*Math.PI*110*t));}w.Write((short)(Math.Max(-1,Math.Min(1,sample))*32767));}
        }
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
    public static void Build()
    {
        Guard();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var makers=new Func<GameObject>[] {BuildLighter,BuildStick,BuildRadio};
        for(int i=0;i<Names.Length;i++)
        {
            var go=makers[i]();foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("Grabbable");
            string path=Root+Names[i]+"/"+Names[i]+".prefab";PrefabUtility.SaveAsPrefabAsset(go,path);Object.DestroyImmediate(go);Debug.Log("[PROPS] BUILT "+path);
        }
        AssetDatabase.SaveAssets();File.WriteAllText("build-passed.txt","Three original portable prefabs built.\n");
    }
    public static void BuildTestCaptureExport() { Build(); Test(); Capture(); Export(); }
    public static void RebuildRadioTestCaptureExport()
    {
        Guard();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var radio=BuildRadio();foreach(var t in radio.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("Grabbable");
        PrefabUtility.SaveAsPrefabAsset(radio,Root+"BlamRadio/BlamRadio.prefab");Object.DestroyImmediate(radio);AssetDatabase.SaveAssets();
        Test();Capture();Export();
    }
    public static void Export()
    {
        Guard();if(!File.Exists("props-tests-passed.txt"))throw new Exception("Tests must pass before export");
        Directory.CreateDirectory("Exports");
        foreach(string name in Names)
        {
            string prefab=Root+name+"/"+name+".prefab";
            var dependencies=AssetDatabase.GetDependencies(prefab,true);
            foreach(string p in dependencies.Where(p=>p.StartsWith("Assets/")))
                if(!p.StartsWith(Root+name+"/")&&!p.StartsWith(Root+"LighterFlame/"))throw new Exception("Unexpected dependency "+p);
            var docs=new[]{Root+name+"/LICENSE.txt",Root+name+"/README.md"};
            foreach(string doc in docs)if(!File.Exists(doc))throw new Exception("Missing package documentation "+doc);
            var assets=dependencies.Where(p=>p.StartsWith("Assets/")).Concat(docs).Concat(new[]{Root+"PortableProps/Editor/CreatorPropsMenu.cs"}).Distinct().ToArray();
            AssetDatabase.ExportPackage(assets,Path.GetFullPath("Exports/"+name+"-0.1.0.unitypackage"),ExportPackageOptions.Default);
            File.WriteAllLines("Exports/"+name+"-contents.txt",assets);
        }
        Debug.Log("[PROPS] EXPORT PASS");
    }
}
