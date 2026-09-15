// MIT. Build and test only in a marked disposable project. Never opens Forest.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BS;
using BS.VisualScripting;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using static PortablePropGraphs;

public static partial class PortablePropsBuilder
{
    const string FlashlightFolder=Root+"Flashlight";
    const string FlashlightPrefab=FlashlightFolder+"/Flashlight.prefab";
    static FlowGraph FlashlightGraph()
    {
        var g=new FlowGraph{title="Creator Flashlight - trigger toggle"};
        ControlOutput State(ControlOutput enter,bool enabled)
        {
            var state=Set(g,"IsOn",L(g,enabled),enter).assigned;
            state=Write(g,typeof(Light),"enabled",V(g,"Spot"),L(g,enabled),state);
            return Active(g,state,V(g,"LensGlow"),enabled);
        }
        ControlOutput Own(ControlOutput enter)
        {
            var n=N(g,new InvokeMember(new Member(typeof(BSSyncedObject),"_TakeOwnership",Type.EmptyTypes)));
            enter.ConnectToValid(n.enter);V(g,"Sync").ConnectToValid(n.target);return n.exit;
        }
        State(N(g,new Start()).trigger,false);
        var grab=N(g,new OnGrab());V(g,"Grip").ConnectToValid(grab.banterHeldEvents);
        Write(g,typeof(Rigidbody),"useGravity",V(g,"Body"),L(g,true),Own(grab.trigger));
        var trigger=N(g,new OnGunTrigger());V(g,"Grip").ConnectToValid(trigger.banterHeldEvents);
        var branch=Gate(g,Own(trigger.trigger),V(g,"IsOn"));State(branch.ifTrue,false);State(branch.ifFalse,true);
        return g;
    }
    public static void BuildFlashlightPackage()
    {
        Guard();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var bodyMat=Mat(FlashlightFolder,"Casing",new Color(.065f,.10f,.09f),.3f);
        var rubber=Mat(FlashlightFolder,"Grip",new Color(.022f,.029f,.028f));
        var metal=Mat(FlashlightFolder,"Bezel",new Color(.53f,.58f,.59f),.8f);
        var lens=Mat(FlashlightFolder,"Lens",new Color(.19f,.24f,.25f),.05f);
        var glowMat=Asset(new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="Lens glow",color=new Color(1.25f,1.55f,2.1f)},FlashlightFolder+"/Materials/LensGlow.mat");
        var root=Body("Creator Flashlight",.36f);root.layer=0;
        var body=root.GetComponent<Rigidbody>();body.useGravity=false;body.linearDamping=.14f;body.angularDamping=.24f;
        var physical=root.AddComponent<BoxCollider>();physical.center=new Vector3(0,.00025f,0);physical.size=new Vector3(.10255328f,.3050203f,.09042174f);
        Primitive("Body",PrimitiveType.Cylinder,root.transform,new Vector3(0,-.045f,0),new Vector3(.063f,.101f,.063f),bodyMat);
        Primitive("GripSleeve",PrimitiveType.Cylinder,root.transform,new Vector3(0,-.055f,0),new Vector3(.066f,.065f,.066f),rubber);
        foreach(float y in new[]{-.116f,-.09f,-.065f,-.04f,-.014f})Primitive("GripRing",PrimitiveType.Cylinder,root.transform,new Vector3(0,y,0),new Vector3(.069f,.002f,.069f),bodyMat);
        Primitive("EndCap",PrimitiveType.Cylinder,root.transform,new Vector3(0,-.142f,0),new Vector3(.067f,.005f,.067f),metal);
        Primitive("HeadNeck",PrimitiveType.Cylinder,root.transform,new Vector3(0,.066f,0),new Vector3(.073f,.014f,.073f),bodyMat);
        Primitive("Head",PrimitiveType.Cylinder,root.transform,new Vector3(0,.11f,0),new Vector3(.09f,.03f,.09f),bodyMat);
        Primitive("Bezel",PrimitiveType.Cylinder,root.transform,new Vector3(0,.142f,0),new Vector3(.092f,.007f,.092f),metal);
        Primitive("Lens",PrimitiveType.Cylinder,root.transform,new Vector3(0,.1495f,0),new Vector3(.075f,.001f,.075f),lens);
        Primitive("Switch",PrimitiveType.Cube,root.transform,new Vector3(0,.018f,-.035f),new Vector3(.018f,.029f,.008f),metal);
        var grip=Grip(root,new Vector3(0,-.045f,0),.052f,.19f);grip.layer=LayerMask.NameToLayer("Grabbable");
        Fields(grip.GetComponent<BSGrabHandle>(),"grabRadius",.05f);
        Fields(grip.GetComponent<BSHeldEvents>(),"fireRate",.12f);
        var glow=Primitive("Lens_Glow",PrimitiveType.Cylinder,root.transform,new Vector3(0,.151f,0),new Vector3(.074f,.001f,.074f),glowMat);
        glow.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;glow.GetComponent<Renderer>().receiveShadows=false;glow.SetActive(false);
        var spot=Child("Flashlight_Spot",root.transform,new Vector3(0,.15f,0)).AddComponent<Light>();
        spot.transform.localRotation=Quaternion.LookRotation(Vector3.up,Vector3.forward);spot.type=LightType.Spot;spot.lightmapBakeType=LightmapBakeType.Realtime;
        spot.color=new Color(.86f,.93f,1);spot.intensity=1.45f;spot.range=16;spot.spotAngle=48;spot.innerSpotAngle=30;spot.shadows=LightShadows.None;
        spot.useBoundingSphereOverride=true;spot.boundingSphereOverride=new Vector4(0,0,0,16);spot.bounceIntensity=0;spot.renderMode=LightRenderMode.ForcePixel;spot.enabled=false;
        root.AddComponent<Variables>();var v=Variables.Object(root);v.Set("IsOn",false);v.Set("Grip",grip);v.Set("Body",body);v.Set("Sync",root.GetComponent<BSSyncedObject>());v.Set("Spot",spot);v.Set("LensGlow",glow);
        Graph(root,FlashlightGraph(),FlashlightFolder+"/VisualScripting/FlashlightToggle.asset");
        PrefabUtility.SaveAsPrefabAsset(root,FlashlightPrefab);Object.DestroyImmediate(root);AssetDatabase.SaveAssets();
        TestFlashlight();CaptureFlashlight();ExportFlashlight();
    }
    public static void TestFlashlight()
    {
        Guard();Checks.Clear();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var errors=new List<string>();void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        Application.logMessageReceived+=Log;
        try
        {
            var one=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FlashlightPrefab));
            var two=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FlashlightPrefab));
            Check(one.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"Flashlight has no missing scripts");
            var body=one.GetComponent<Rigidbody>();var grip=one.transform.Find("Grab_Handle");var col=grip.GetComponent<CapsuleCollider>();
            Check(!body.useGravity&&body.mass==.36f,"Initial mounted gravity and original mass preserved");
            Check(one.layer==0&&grip.gameObject.layer==LayerMask.NameToLayer("Grabbable"),"Physical body and grab layers preserved");
            Check(grip.localPosition==new Vector3(0,-.045f,0)&&col.radius==.052f&&col.height==.19f&&col.direction==1&&col.isTrigger,"Original cylindrical grab collider preserved");
            Check(grip.GetComponent<BSGrabHandle>().GrabRadius==.05f,"Original grab radius preserved");
            var held=grip.GetComponent<BSHeldEvents>();Check(!held.Auto&&held.Sensitivity==.45f&&held.FireRate==.12f&&!held.BlockLeftTrigger&&!held.BlockRightTrigger,"Original single-shot trigger settings preserved");
            var sync=new SerializedObject(one.GetComponent<BSSyncedObject>());
            Check(sync.FindProperty("syncPosition").boolValue&&sync.FindProperty("syncRotation").boolValue&&sync.FindProperty("takeOwnershipOnGrab").boolValue,"SDK transform sync and grab ownership configured");
            var machine=one.GetComponent<ScriptMachine>();Check(machine.nest.graph.invalidConnections.Count==0,"Flashlight graph connections valid");
            Check(machine.nest.graph.units.OfType<OnRelease>().Count()==0,"Dropping keeps the current light state, as in Forest");
            var reference=Bind(machine);var g=machine.nest.graph;var v=Variables.Object(one);var spot=v.Get<Light>("Spot");var glow=v.Get<GameObject>("LensGlow");
            Check(spot.type==LightType.Spot&&spot.range==16&&spot.spotAngle==48&&spot.innerSpotAngle==30&&spot.shadows==LightShadows.None,"Original no-shadow beam parameters preserved");
            Check(Vector3.Dot(spot.transform.forward,one.transform.up)>.999f&&spot.useBoundingSphereOverride,"Beam points out of the lens with culling override");
            var calls=g.units.OfType<InvokeMember>().Where(n=>n.member.name=="_TakeOwnership").ToArray();Check(calls.Length==2,"Native ownership calls remain in the shipped graph");
            foreach(var call in calls)
            {
                var pass=N(g,new Sequence{outputCount=1});
                foreach(var c in g.controlConnections.Where(c=>c.destination==call.enter).ToArray()){var source=c.source;g.controlConnections.Remove(c);source.ConnectToValid(pass.enter);}
                foreach(var c in g.controlConnections.Where(c=>c.source==call.exit).ToArray()){var dest=c.destination;g.controlConnections.Remove(c);pass.multiOutputs[0].ConnectToValid(dest);}
                g.units.Remove(call);
            }
            foreach(bool left in new[]{true,false})
            {
                Fire(reference,g.units.OfType<Start>().Single().trigger);Check(!v.Get<bool>("IsOn")&&!spot.enabled&&!glow.activeSelf,"Starts with beam and glow off "+left);
                var grab=g.units.OfType<OnGrab>().Single();using(var f=Flow.New(reference)){f.SetValue(grab.isLeft,left);f.Invoke(grab.trigger);}
                Check(body.useGravity,"First grab enables gravity "+left);
                var trigger=g.units.OfType<OnGunTrigger>().Single();
                for(int i=0;i<4;i++)
                {
                    using(var f=Flow.New(reference)){f.SetValue(trigger.isLeft,left);f.Invoke(trigger.trigger);}
                    bool on=i%2==0;Check(v.Get<bool>("IsOn")==on&&spot.enabled==on&&glow.activeSelf==on,"Trigger keeps state, beam and glow together "+left+"/"+i);
                }
            }
            Check(Variables.Object(two).Get<GameObject>("Grip")!=v.Get<GameObject>("Grip")&&!Variables.Object(two).Get<Light>("Spot").enabled,"Separate flashlight references and light state");
            Check(BS.SDKEditor.ValidateVisualScripting.CheckVsNodes(),"Creator SDK allow-list passes including flashlight");
            Check(errors.Count==0,"No errors during flashlight graph checks");File.WriteAllLines("flashlight-tests-passed.txt",Checks);
        }
        finally{Application.logMessageReceived-=Log;}
    }
    static Color[] FlashlightPixels(Camera camera,string filename=null)
    {
        var target=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
        if(filename!=null)File.WriteAllBytes(filename,image.EncodeToPNG());var pixels=image.GetPixels();
        camera.targetTexture=null;RenderTexture.active=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);return pixels;
    }
    public static void CaptureFlashlight()
    {
        Guard();Directory.CreateDirectory("Exports");EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FlashlightPrefab));root.GetComponent<Rigidbody>().isKinematic=true;
        var v=Variables.Object(root);var spot=v.Get<Light>("Spot");v.Get<GameObject>("LensGlow").SetActive(true);root.transform.rotation=Quaternion.Euler(0,0,-30);
        var key=new GameObject("Preview key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=2.2f;key.transform.rotation=Quaternion.Euler(35,-35,0);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.38f,.4f);
        var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.12f,.14f);camera.orthographic=true;camera.orthographicSize=.205f;camera.nearClipPlane=.001f;camera.farClipPlane=20;
        camera.transform.position=new Vector3(.36f,.32f,-.65f);camera.transform.LookAt(Vector3.zero);FlashlightPixels(camera,"Exports/Flashlight.png");
        root.transform.rotation=Quaternion.identity;key.enabled=false;RenderSettings.ambientLight=Color.black;
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,.9f,0);wall.transform.localScale=new Vector3(1,.02f,1);
        var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=new Color(.6f,.6f,.6f)};wall.GetComponent<Renderer>().sharedMaterial=mat;
        camera.orthographicSize=.6f;camera.transform.position=new Vector3(0,.15f,-1.2f);camera.transform.LookAt(wall.transform.position);
        spot.enabled=false;var off=FlashlightPixels(camera);spot.enabled=true;var on=FlashlightPixels(camera,"Exports/Flashlight-beam-test.png");
        double delta=0;int changed=0;for(int i=0;i<off.Length;i++){double d=on[i].grayscale-off[i].grayscale;delta+=d;if(d>.02)changed++;}
        if(delta/off.Length<.001||changed<100)throw new Exception("Spotlight did not illuminate the target: "+delta/off.Length+", changed="+changed);
        File.WriteAllText("flashlight-render-passed.txt","Actual spotlight on/off pixel check: mean luminance increase="+delta/off.Length+"; brighter pixels="+changed+".\n");
        Debug.Log("[FLASHLIGHT] RENDER PASS "+delta/off.Length+", pixels="+changed);
    }
    public static void ExportFlashlight()
    {
        Guard();if(!File.Exists("flashlight-tests-passed.txt")||!File.Exists("flashlight-render-passed.txt"))throw new Exception("Flashlight tests required");
        var dependencies=AssetDatabase.GetDependencies(FlashlightPrefab,true).Where(p=>p.StartsWith("Assets/")).ToArray();
        foreach(string p in dependencies)if(!p.StartsWith(FlashlightFolder+"/"))throw new Exception("Unexpected flashlight dependency "+p);
        var docs=new[]{FlashlightFolder+"/README.md",FlashlightFolder+"/LICENSE.txt",FlashlightFolder+"/Editor/CreatorFlashlightMenu.cs"};
        foreach(string p in docs)if(!File.Exists(p))throw new Exception("Missing "+p);
        var paths=dependencies.Concat(docs).Distinct().ToArray();AssetDatabase.ExportPackage(paths,Path.GetFullPath("Exports/Flashlight-0.1.0.unitypackage"),ExportPackageOptions.Default);
        File.WriteAllLines("Exports/Flashlight-contents.txt",paths);Debug.Log("[FLASHLIGHT] EXPORT PASS");
    }
}
