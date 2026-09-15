// MIT. Native Unity physics/Update check without SDK controller/network components.
using System;
using System.IO;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PortableStickPlaySmoke
{
    const string Key="CreatorPropBurnSmoke";
    static PortableStickPlaySmoke() { EditorApplication.update+=Tick; }
    public static void Run()
    {
        if(!File.Exists("COMMUNITY_PROP_FIXTURE"))throw new Exception("Disposable fixture required");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("PhysicsFixture");root.AddComponent<Rigidbody>().isKinematic=true;
        var tip=new GameObject("BurnTip");tip.SetActive(false);tip.transform.SetParent(root.transform);tip.AddComponent<SphereCollider>().isTrigger=true;tip.GetComponent<SphereCollider>().radius=.06f;
        tip.AddComponent<Variables>();var v=Variables.Object(tip);var flame=new GameObject("TestFlame");flame.SetActive(false);
        v.Set("BodyObject",root);v.Set("Tip",tip.transform);v.Set("Lit",false);v.Set("BurnSeconds",80f);v.Set("Remaining",0f);v.Set("FlameVisual",flame);
        tip.AddComponent<ScriptMachine>().nest.SwitchToMacro(AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(PortablePropsBuilder.Root+"BurningStick/VisualScripting/BurningEnd.asset"));tip.SetActive(true);
        var source=new GameObject("Portable_Ignition_Source_PhysicsFixture");source.transform.position=new Vector3(.025f,0,0);source.AddComponent<SphereCollider>().isTrigger=true;source.GetComponent<SphereCollider>().radius=.03f;source.AddComponent<Variables>();Variables.Object(source).Set("Lit",true);
        Time.timeScale=1;SessionState.SetInt(Key,1);SessionState.SetFloat(Key+"WallDeadline",(float)EditorApplication.timeSinceStartup+110);
        EditorApplication.EnterPlaymode();
    }
    static void Fail(string message)
    {
        File.WriteAllText("stick-play-failed.txt",message);Debug.LogError(message);SessionState.SetInt(Key,4);if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();else EditorApplication.Exit(1);
    }
    static void Tick()
    {
        int state=SessionState.GetInt(Key,0);if(state==0)return;
        if(state>=3&&!EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetInt(Key,0);EditorApplication.Exit(state==3?0:1);return;}
        if((float)EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"WallDeadline",float.MaxValue)){Fail("Native burn smoke timed out");return;}
        if(!EditorApplication.isPlaying||EditorApplication.isPaused)return;
        var tip=GameObject.Find("BurnTip");if(!tip)return;var v=Variables.Object(tip);
        if(state==1)
        {
            if(v.Get<bool>("Lit"))
            {
                GameObject.Find("Portable_Ignition_Source_PhysicsFixture").transform.position=Vector3.one*10;
                SessionState.SetFloat(Key+"Ignited",Time.time);SessionState.SetInt(Key,2);Debug.Log("[STICK-PLAY] Native trigger ignition passed; checking full 80 second burnout");
            }
            else if(Time.timeSinceLevelLoad>5)Fail("Actual physics trigger did not ignite stick");
        }
        else if(state==2&&!v.Get<bool>("Lit"))
        {
            float elapsed=Time.time-SessionState.GetFloat(Key+"Ignited",0);
            if(elapsed<79||elapsed>83||v.Get<GameObject>("FlameVisual").activeSelf){Fail("Unexpected burnout timing or visual state: "+elapsed);return;}
            File.WriteAllText("stick-play-passed.txt","Native OnTrigger contact and actual Update burnout passed. Elapsed game seconds: "+elapsed+". No native controller or multiplayer tested.\n");
            Debug.Log("[STICK-PLAY] PASS elapsed="+elapsed);SessionState.SetInt(Key,3);EditorApplication.ExitPlaymode();
        }
    }
}
