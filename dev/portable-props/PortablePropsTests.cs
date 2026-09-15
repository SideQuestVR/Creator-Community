// MIT. Editor graph checks; native controller, networking and headset tests are separate.
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
using Object = UnityEngine.Object;

public static partial class PortablePropsBuilder
{
    static readonly List<string> Checks = new List<string>();
    static void Check(bool ok,string label) { if(!ok)throw new Exception(label);Checks.Add(label);Debug.Log("[PROP-TEST] PASS "+label); }
    static ScriptMachine Find(GameObject root,string title) { return root.GetComponentsInChildren<ScriptMachine>(true).First(m=>m.nest.graph.title==title); }
    static GraphReference Bind(ScriptMachine m)
    {
        m.nest.SwitchToEmbed(m.nest.graph.CloneViaSerialization());
        var r=GraphReference.New(m,true);r.CreateGraphData();return r;
    }
    static void Fire(GraphReference r,ControlOutput port) { using(var f=Flow.New(r))f.Invoke(port); }
    static GameObject Instance(string name) { return Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+name+"/"+name+".prefab")); }
    public static void Test()
    {
        Guard();Checks.Clear();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var errors=new List<string>();void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        Application.logMessageReceived+=Log;
        try
        {
            var lighter=Instance("PortableLighter");var otherLighter=Instance("PortableLighter");
            var stick=Instance("BurningStick");var radio=Instance("BlamRadio");
            foreach(var root in new[]{lighter,otherLighter,stick,radio})
            {
                Check(root.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),root.name+" has no missing scripts");
                foreach(var machine in root.GetComponentsInChildren<ScriptMachine>(true))
                {
                    Check(machine.nest.graph!=null&&machine.nest.graph.units.Count>0,machine.nest.graph.title+" resolves");
                    Check(machine.nest.graph.invalidConnections.Count==0,machine.nest.graph.title+" has no invalid connections");
                }
            }
            var m=Find(lighter,"Creator Lighter - held-hand trigger toggle");var r=Bind(m);var g=m.nest.graph;var v=Variables.Object(m.gameObject);
            var grab=g.units.OfType<OnGrab>().Single();var release=g.units.OfType<OnRelease>().Single();var trigger=g.units.OfType<OnTrigger>().Single();
            void Input(float value,bool left){using(var f=Flow.New(r)){f.SetValue(trigger.input,value);f.SetValue(trigger.isLeft,left);f.Invoke(trigger.trigger);}}
            Fire(r,g.units.OfType<Start>().Single().trigger);Input(1,true);Check(!v.Get<bool>("Lit"),"Unheld trigger rejected");
            foreach(bool left in new[]{true,false})
            {
                using(var f=Flow.New(r)){f.SetValue(grab.isLeft,left);f.Invoke(grab.trigger);}
                Input(1,!left);Check(!v.Get<bool>("Lit"),"Other hand rejected "+left);
                for(int press=0;press<4;press++)
                {
                    Input(0,left);Input(1,left);bool expected=press%2==0;
                    Check(v.Get<bool>("Lit")==expected&&v.Get<GameObject>("FlameVisual").activeSelf==expected,"Trigger toggles once "+left+"/"+press);
                    Input(.98f,left);Check(v.Get<bool>("Lit")==expected,"Held trigger does not repeat "+left+"/"+press);
                }
                Input(0,left);Input(1,left);
                using(var f=Flow.New(r)){f.SetValue(release.isLeft,left);f.Invoke(release.trigger);}
                Check(!v.Get<bool>("Held")&&!v.Get<bool>("Lit")&&!v.Get<GameObject>("FlameVisual").activeSelf,"Release extinguishes "+left);
            }
            var other=Find(otherLighter,g.title);Check(Variables.Object(other.gameObject).Get<GameObject>("Grip")!=v.Get<GameObject>("Grip"),"Lighter instances have independent internal references");
            var tips=stick.GetComponentsInChildren<ScriptMachine>(true).Where(s=>s.nest.graph.title=="Creator Burning Stick - independent end ignition").ToArray();
            var refs=tips.Select(Bind).ToArray();for(int i=0;i<tips.Length;i++)Fire(refs[i],tips[i].nest.graph.units.OfType<Start>().Single().trigger);
            var collider=m.GetComponent<SphereCollider>();
            void Touch(int i,Collider c,bool stay=true)
            {
                Physics.SyncTransforms();var node=stay?(TriggerEventUnit)tips[i].nest.graph.units.OfType<OnTriggerStay>().Single():tips[i].nest.graph.units.OfType<OnTriggerEnter>().Single();
                using(var f=Flow.New(refs[i])){f.SetValue(node.collider,c);f.Invoke(node.trigger);}
            }
            lighter.transform.position+=tips[0].transform.position-m.transform.position;Physics.SyncTransforms();
            Touch(0,collider);Check(!Variables.Object(tips[0].gameObject).Get<bool>("Lit"),"Unlit source cannot ignite stick");
            v.Set("Lit",true);Touch(1,collider);Check(!Variables.Object(tips[1].gameObject).Get<bool>("Lit"),"Distant opposite stick end rejected");
            Touch(0,collider);var a=Variables.Object(tips[0].gameObject);Check(a.Get<bool>("Lit")&&a.Get<GameObject>("FlameVisual").activeSelf,"Lighter ignites touching stick end");
            a.Set("Remaining",37f);Touch(0,collider);Check(a.Get<float>("Remaining")==37f,"Contact does not reset burning timer");
            lighter.transform.position+=tips[1].transform.position-m.transform.position;Touch(1,collider,false);var b=Variables.Object(tips[1].gameObject);Check(b.Get<bool>("Lit"),"Second stick end ignites independently");
            Check(a.Get<float>("BurnSeconds")==80f&&b.Get<float>("BurnSeconds")==80f,"Both end lifetimes are 80 seconds");
            a.Set("Remaining",-.001f);Fire(refs[0],tips[0].nest.graph.units.OfType<Update>().Single().trigger);Check(!a.Get<bool>("Lit")&&!a.Get<GameObject>("FlameVisual").activeSelf&&b.Get<bool>("Lit"),"Expired end extinguishes without affecting other end");
            var plain=new GameObject("Portable_Ignition_Source_NoState").AddComponent<SphereCollider>();plain.transform.position=tips[0].transform.position;Touch(0,plain);Check(!a.Get<bool>("Lit"),"Unconfigured source is ignored without exception");
            lighter.transform.position+=tips[0].transform.position-m.transform.position;Touch(0,collider);Check(a.Get<bool>("Lit")&&a.Get<float>("Remaining")==80f,"Expired stick end relights with a fresh timer");
            var stick2=Instance("BurningStick");var target=Find(stick2,"Creator Burning Stick - independent end ignition");var targetRef=Bind(target);Fire(targetRef,target.nest.graph.units.OfType<Start>().Single().trigger);
            stick2.transform.position+=tips[1].transform.position-target.transform.position;Physics.SyncTransforms();var contact=target.nest.graph.units.OfType<OnTriggerStay>().Single();
            using(var f=Flow.New(targetRef)){f.SetValue(contact.collider,tips[1].GetComponent<SphereCollider>());f.Invoke(contact.trigger);}
            Check(Variables.Object(target.gameObject).Get<bool>("Lit"),"Burning stick can ignite another stick");
            TestRadio(radio);
            Check(BS.SDKEditor.ValidateVisualScripting.CheckVsNodes(),"Creator SDK allow-list validation passes");
            Check(errors.Count==0,"No errors during graph checks");
            File.WriteAllLines("props-tests-passed.txt",Checks);
        }
        finally {Application.logMessageReceived-=Log;}
    }
    static void TestRadio(GameObject radio)
    {
        var grip=radio.transform.Find("Grab_Handle");var collider=grip.GetComponent<CapsuleCollider>();
        Check(grip.localPosition==new Vector3(0,.438f,0)&&Mathf.Abs(Vector3.Dot(grip.up,radio.transform.right))>.999f,"Radio grip matches original horizontal collider position and axis");
        Check(collider.radius==.02f&&collider.height==.58f&&collider.direction==1&&!collider.isTrigger,"Radio preserves original collider dimensions and physical contact");
        Check(new SerializedObject(grip.GetComponent<BSGrabHandle>()).FindProperty("grabRadius").floatValue==.02f,"Radio preserves original cylinder grab radius");
        var handle=radio.transform.Find("HorizontalHandle");
        Check(handle.localPosition==grip.localPosition&&handle.GetComponent<MeshFilter>().sharedMesh.bounds.size.x>.53f,"Replacement horizontal handle follows the collider");
        var mesh=handle.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
        bool outward=true;for(int i=0;i<triangles.Length;i+=3)
        {
            var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
            outward&=Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c)/3-mesh.bounds.center)>0;
        }
        Check(outward,"Every radio handle triangle faces outward");
        radio.transform.SetPositionAndRotation(new Vector3(3,2,-4),Quaternion.Euler(0,37,0));
        var m=radio.GetComponent<ScriptMachine>();var r=Bind(m);var g=m.nest.graph;var v=Variables.Object(radio);
        var owner=g.units.OfType<InvokeMember>().Single(n=>n.member.name=="_DoIOwn");var replacement=PortablePropGraphs.N(g,new Literal(typeof(bool),true));
        foreach(var c in g.valueConnections.Where(c=>c.source==owner.result).ToArray()){var dest=c.destination;g.valueConnections.Remove(c);replacement.output.ConnectToValid(dest);}
        Fire(r,g.units.OfType<Start>().Single().trigger);var home=radio.transform.position;var rot=radio.transform.rotation;
        Check(v.Get<Vector3>("HomePosition")==home&&v.Get<Quaternion>("HomeRotation")==rot,"Radio captures this scene's placement");
        var audio=radio.GetComponentInChildren<AudioSource>();Check(audio.clip&&audio.clip.length>30&&audio.loop&&audio.spatialBlend==1,"Radio has author's track, looping spatial playback");
        var update=g.units.OfType<Update>().Single();radio.transform.position=Vector3.one*20;v.Set("LastActivity",Time.time-301);Fire(r,update.trigger);
        Check(radio.transform.position==home&&Quaternion.Angle(radio.transform.rotation,rot)<.01f,"Idle radio returns after five minutes");
        radio.transform.position=Vector3.one*20;v.Set("HeldLeft",true);v.Set("LastActivity",Time.time-400);Fire(r,update.trigger);Check(radio.transform.position!=home,"Held radio does not return");
        v.Set("HeldLeft",false);replacement.value=false;v.Set("LastActivity",Time.time-400);Fire(r,update.trigger);Check(radio.transform.position!=home,"Non-owner does not move radio");replacement.value=true;
        var finish=new GameObject("FixtureFinish").AddComponent<BoxCollider>();finish.tag="Finish";var contact=g.units.OfType<OnTriggerEnter>().Single();
        using(var f=Flow.New(r)){f.SetValue(contact.collider,finish);f.Invoke(contact.trigger);}
        Check(radio.transform.position==home,"Finish trigger returns radio to its placement");
        Check(radio.GetComponent<Rigidbody>().linearVelocity==Vector3.zero&&radio.GetComponent<Rigidbody>().angularVelocity==Vector3.zero,"Return clears linear and angular velocity");
    }
    public static void Capture()
    {
        Guard();Directory.CreateDirectory("Exports");
        foreach(var name in Names)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var go=Instance(name);go.GetComponent<Rigidbody>().isKinematic=true;
            foreach(var v in go.GetComponentsInChildren<Variables>(true))if(Variables.Object(v.gameObject).IsDefined("FlameVisual"))Variables.Object(v.gameObject).Get<GameObject>("FlameVisual").SetActive(true);
            var renderers=go.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2.2f;light.transform.rotation=Quaternion.Euler(35,-35,0);
            var fill=new GameObject("Fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.8f;fill.transform.rotation=Quaternion.Euler(25,145,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.35f,.38f,.4f);
            var camera=new GameObject("PreviewCamera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.12f,.14f);camera.orthographic=true;
            float span=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));camera.nearClipPlane=.001f;camera.farClipPlane=20;
            var angle=name=="BurningStick"?new Vector3(1,.55f,-.25f):new Vector3(.8f,.5f,-1.3f);
            camera.transform.position=bounds.center+angle*span*2;camera.transform.LookAt(bounds.center);
            float horizontal=0,vertical=0;
            foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
            {
                var corner=camera.transform.InverseTransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                horizontal=Mathf.Max(horizontal,Mathf.Abs(corner.x));vertical=Mathf.Max(vertical,Mathf.Abs(corner.y));
            }
            camera.orthographicSize=Mathf.Max(vertical,horizontal/(960f/540f))*1.22f;
            var target=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes("Exports/"+name+".png",image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);
        }
        Debug.Log("[PROPS] PREVIEWS PASS");
    }
}
