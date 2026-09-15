// MIT. Separate from the older props menu so import order cannot remove this entry.
using System;
using UnityEditor;
using UnityEngine;

public static class CreatorFlashlightMenu
{
    const string Prefab="Assets/CreatorCommunity/Flashlight/Flashlight.prefab";
    [MenuItem("Tools/Creator Community/Add/Flashlight")]
    public static void Add(){Insert();}
    [MenuItem("Tools/Creator Community/Add/Flashlight",true)]
    static bool Available(){return AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);}
    public static GameObject Insert()
    {
        int layer=LayerMask.NameToLayer("Grabbable");
        if(layer<0){EditorUtility.DisplayDialog("Creator SDK setup required","Configure the Creator SDK and its Grabbable layer first. No scene changes were made.","OK");return null;}
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if(!prefab)throw new InvalidOperationException("Import the Flashlight package first.");
        var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(root,"Add Creator flashlight");
        var grip=root.transform.Find("Grab_Handle");
        grip.gameObject.layer=layer;PrefabUtility.RecordPrefabInstancePropertyModifications(grip.gameObject);
        foreach(var c in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(!c||c.GetType().FullName!="BS.BSObjectId")continue;
            var data=new SerializedObject(c);data.FindProperty("Id").stringValue=Guid.NewGuid().ToString("N");data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
        if(SceneView.lastActiveSceneView)root.transform.position=SceneView.lastActiveSceneView.pivot+Vector3.up*.3f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        Selection.activeGameObject=root;return root;
    }
}
