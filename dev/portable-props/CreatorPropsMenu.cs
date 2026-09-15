// MIT. Explicit scene insertion only; no automatic scene saves or settings changes.
using System;
using UnityEditor;
using UnityEngine;

public static class CreatorPropsMenu
{
    const string Root = "Assets/CreatorCommunity/";
    [MenuItem("Tools/Creator Community/Add/Portable Lighter")]
    static void Lighter() { Insert("PortableLighter/PortableLighter.prefab"); }
    [MenuItem("Tools/Creator Community/Add/Burning Stick")]
    static void Stick() { Insert("BurningStick/BurningStick.prefab"); }
    [MenuItem("Tools/Creator Community/Add/Blam Radio")]
    static void Radio() { Insert("BlamRadio/BlamRadio.prefab"); }
    [MenuItem("Tools/Creator Community/Add/Portable Lighter", true)]
    static bool HasLighter() { return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "PortableLighter/PortableLighter.prefab"); }
    [MenuItem("Tools/Creator Community/Add/Burning Stick", true)]
    static bool HasStick() { return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "BurningStick/BurningStick.prefab"); }
    [MenuItem("Tools/Creator Community/Add/Blam Radio", true)]
    static bool HasRadio() { return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "BlamRadio/BlamRadio.prefab"); }

    public static GameObject Insert(string relativePath)
    {
        if (relativePath != "PortableLighter/PortableLighter.prefab" && relativePath != "BurningStick/BurningStick.prefab" && relativePath != "BlamRadio/BlamRadio.prefab")
            throw new ArgumentException("Unknown Creator prop.");
        int layer = LayerMask.NameToLayer("Grabbable");
        if (layer < 0) { EditorUtility.DisplayDialog("Creator SDK setup required", "Configure the Creator SDK and its Grabbable layer before adding this prop. No scene changes were made.", "OK"); return null; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + relativePath);
        if (!prefab) throw new InvalidOperationException("Import the prop package first.");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(instance, "Add Creator prop");
        foreach (var transform in instance.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = layer;
        foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!component || component.GetType().FullName != "BS.BSObjectId") continue;
            var data = new SerializedObject(component); var id = data.FindProperty("Id");
            id.stringValue = Guid.NewGuid().ToString("N"); data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        if (SceneView.lastActiveSceneView) instance.transform.position = SceneView.lastActiveSceneView.pivot + Vector3.up * 0.3f;
        Selection.activeGameObject = instance;
        return instance;
    }
}
