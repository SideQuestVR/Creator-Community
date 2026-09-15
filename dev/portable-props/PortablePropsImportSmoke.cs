// MIT. Import the exported archives into an empty, disposable SDK project.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PortablePropsImportSmoke
{
    const string Active="PortablePropsImportSmoke.Active", Index="PortablePropsImportSmoke.Index", Flight="PortablePropsImportSmoke.Flight";
    static readonly string[] Names={"PortableLighter","BurningStick","BlamRadio"};
    static PortablePropsImportSmoke()
    {
        AssetDatabase.importPackageCompleted+=name=>
        {
            if(!SessionState.GetBool(Active,false))return;
            Debug.Log("[PROPS-IMPORT] completed "+name);
            SessionState.SetInt(Index,SessionState.GetInt(Index,0)+1);SessionState.SetBool(Flight,false);
        };
        AssetDatabase.importPackageFailed+=(name,message)=>{if(SessionState.GetBool(Active,false))Fail(name+": "+message);};
        EditorApplication.update+=Tick;
    }
    public static void Run()
    {
        if(!File.Exists("COMMUNITY_PROP_FIXTURE"))throw new Exception("Disposable fixture marker required");
        SessionState.SetInt(Index,0);SessionState.SetBool(Flight,false);SessionState.SetBool(Active,true);
    }
    static void Fail(string message)
    {
        SessionState.SetBool(Active,false);File.WriteAllText("import-failed.txt",message);Debug.LogError(message);EditorApplication.Exit(1);
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Active,false)||SessionState.GetBool(Flight,false)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        try
        {
            int i=SessionState.GetInt(Index,0);
            if(i==Names.Length)
            {
                foreach(string name in Names)
                    if(!AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CreatorCommunity/"+name+"/"+name+".prefab"))throw new Exception("Missing imported "+name);
                SessionState.SetBool(Active,false);File.WriteAllText("import-completed.txt","Three actual importPackageCompleted events and imported prefabs verified.\n");EditorApplication.Exit(0);return;
            }
            string archive=Path.GetFullPath("../CommunityProps/Exports/"+Names[i]+"-0.1.0.unitypackage");
            if(!File.Exists(archive))throw new Exception("Missing "+archive);
            SessionState.SetBool(Flight,true);AssetDatabase.ImportPackage(archive,false);
        }
        catch(Exception e){Fail(e.ToString());}
    }
}
