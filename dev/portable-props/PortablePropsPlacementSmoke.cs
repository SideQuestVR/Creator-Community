// MIT. Only runs in a marked disposable fixture, never in a user world.
using System;
using System.IO;
using System.Linq;
using BS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PortablePropsPlacementSmoke
{
    public static void RunAll(){PortablePropsBuilder.Test();Run();}
    static void Check(bool result,string message){if(!result)throw new Exception(message);Debug.Log("[PROP-PLACEMENT] PASS "+message);}
    public static void Run()
    {
        if(!File.Exists("COMMUNITY_PROP_FIXTURE"))throw new Exception("Disposable fixture marker required");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers=tags.FindProperty("layers");int old=LayerMask.NameToLayer("Grabbable"),layer=16;
        string previous=layers.GetArrayElementAtIndex(layer).stringValue;
        Check(old>=0&&old!=layer&&previous=="UserLayer12","Disposable user layer available");
        layers.GetArrayElementAtIndex(old).stringValue="";layers.GetArrayElementAtIndex(layer).stringValue="Grabbable";tags.ApplyModifiedPropertiesWithoutUndo();
        try
        {
            foreach(string name in new[]{"PortableLighter","BurningStick","BlamRadio"})
                for(int i=0;i<2;i++)CreatorPropsMenu.Insert(name+"/"+name+".prefab");
            var roots=scene.GetRootGameObjects();Check(roots.Length==6,"Two instances of each imported prop inserted");
            Check(roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).All(t=>t.gameObject.layer==layer),"Insertion uses this project's layer");
            var ids=roots.SelectMany(g=>g.GetComponentsInChildren<BSObjectId>(true)).Select(x=>x.Id).OrderBy(x=>x).ToArray();
            Check(ids.Length==ids.Distinct().Count(),"All inserted IDs unique");
            Check(string.IsNullOrEmpty(scene.path),"Insertion did not save the scene");
            EditorSceneManager.SaveScene(scene,"Assets/PlacementSmoke.unity");
            scene=EditorSceneManager.OpenScene("Assets/PlacementSmoke.unity",OpenSceneMode.Single);roots=scene.GetRootGameObjects();
            Check(roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).All(t=>t.gameObject.layer==layer),"Layer overrides survive scene reopening");
            var reopened=roots.SelectMany(g=>g.GetComponentsInChildren<BSObjectId>(true)).Select(x=>x.Id).OrderBy(x=>x).ToArray();
            Check(ids.SequenceEqual(reopened),"Fresh IDs survive scene reopening");
            File.WriteAllText("placement-passed.txt","Six imported instances: unique IDs, changed layer, no automatic save, scene reopen persistence passed.\n");
        }
        finally
        {
            tags.Update();layers=tags.FindProperty("layers");layers.GetArrayElementAtIndex(layer).stringValue=previous;layers.GetArrayElementAtIndex(old).stringValue="Grabbable";tags.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
