// MIT. Import/placement verification only in a marked disposable SDK project.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FlashlightImportSmoke
{
    const string Active="FlashlightImportSmoke.Active", Complete="FlashlightImportSmoke.Complete";
    static FlashlightImportSmoke()
    {
        AssetDatabase.importPackageCompleted+=name=>{if(SessionState.GetBool(Active,false)){Debug.Log("[FLASHLIGHT-IMPORT] completed "+name);SessionState.SetBool(Complete,true);}};
        AssetDatabase.importPackageFailed+=(name,message)=>{if(SessionState.GetBool(Active,false))Fail(message);};
        EditorApplication.update+=Tick;
    }
    public static void Run()
    {
        if(!File.Exists("COMMUNITY_PROP_FIXTURE"))throw new Exception("Disposable fixture marker required");
        string archive=Path.GetFullPath("../CommunityProps/Exports/Flashlight-0.1.0.unitypackage");
        if(!File.Exists(archive))throw new Exception("Missing flashlight archive");
        SessionState.SetBool(Complete,false);SessionState.SetBool(Active,true);AssetDatabase.ImportPackage(archive,false);
    }
    static void Fail(string message){SessionState.SetBool(Active,false);File.WriteAllText("flashlight-import-failed.txt",message);Debug.LogError(message);EditorApplication.Exit(1);}
    static void Tick()
    {
        if(!SessionState.GetBool(Active,false)||!SessionState.GetBool(Complete,false)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        SessionState.SetBool(Active,false);
        try
        {
            PortablePropsBuilder.Test();PortablePropsBuilder.TestFlashlight();TestPlacement();
            File.WriteAllText("flashlight-import-passed.txt","Actual Unity package import and script reload, 68 existing-prop checks, 27 flashlight checks and placement checks passed.\n");
            EditorApplication.Exit(0);
        }
        catch(Exception e){Fail(e.ToString());}
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Debug.Log("[FLASHLIGHT-PLACEMENT] PASS "+message);}
    static string[] Ids(GameObject[] roots)=>roots.SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c&&c.GetType().FullName=="BS.BSObjectId").Select(c=>new SerializedObject(c).FindProperty("Id").stringValue).OrderBy(s=>s).ToArray();
    static void TestPlacement()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var menu=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("CreatorFlashlightMenu")).FirstOrDefault(t=>t!=null);
        Check(menu!=null,"Standalone flashlight menu compiled beside older props menu");
        var method=menu.GetMethod("Insert",BindingFlags.Public|BindingFlags.Static);
        var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);var layers=tags.FindProperty("layers");
        int old=LayerMask.NameToLayer("Grabbable"),changed=16;string previous=layers.GetArrayElementAtIndex(changed).stringValue;
        Check(old>=0&&old!=changed&&previous=="UserLayer12","Disposable user layer available");
        layers.GetArrayElementAtIndex(old).stringValue="";layers.GetArrayElementAtIndex(changed).stringValue="Grabbable";tags.ApplyModifiedPropertiesWithoutUndo();
        try
        {
            method.Invoke(null,null);method.Invoke(null,null);var roots=scene.GetRootGameObjects();var ids=Ids(roots);
            Check(roots.Length==2&&ids.Length==4&&ids.Distinct().Count()==4,"Two instances have separate root and grip IDs");
            Check(roots.All(r=>r.layer==0&&r.transform.Find("Grab_Handle").gameObject.layer==changed),"Only grip adopts this project's Grabbable layer");
            Check(string.IsNullOrEmpty(scene.path),"Insertion does not save the scene");
            Undo.IncrementCurrentGroup();method.Invoke(null,null);Undo.PerformUndo();Check(scene.GetRootGameObjects().Length==2,"Insert can be undone without removing earlier instances");
            EditorSceneManager.SaveScene(scene,"Assets/FlashlightPlacementSmoke.unity");scene=EditorSceneManager.OpenScene("Assets/FlashlightPlacementSmoke.unity",OpenSceneMode.Single);roots=scene.GetRootGameObjects();
            Check(ids.SequenceEqual(Ids(roots)),"Fresh IDs survive save and reopen");
            Check(roots.All(r=>r.layer==0&&r.transform.Find("Grab_Handle").gameObject.layer==changed),"Default body and remapped grip layers survive reopening");
            File.WriteAllText("flashlight-placement-passed.txt","Eight checks passed: compiled menu, fixture layer, unique IDs, grip-only layer, no auto-save, Undo, IDs and layers after reopen.\n");
        }
        finally{tags.Update();layers=tags.FindProperty("layers");layers.GetArrayElementAtIndex(old).stringValue="Grabbable";layers.GetArrayElementAtIndex(changed).stringValue=previous;tags.ApplyModifiedPropertiesWithoutUndo();}
    }
}
