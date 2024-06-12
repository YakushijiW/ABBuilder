#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public enum ABCompressType
{
    None,
    LZ4,
    LZMA,
}

[System.Serializable]
public class BuilderConfigScriptable : ScriptableObject
{
    public const string ConfigName = "BuilderConfig.asset";
    public const string CatalogABName = "catalog";

    public string ResPath = "";
    public string OutputPath = "";
    public string FinalOutputPath
    {
        get
        {
            return System.IO.Path.Combine(OutputPath + "/" +buildTargetPlatform);
        }
    }
    public BuildTarget buildTargetPlatform = BuildTarget.StandaloneWindows64;
    public ABCompressType compressType = ABCompressType.LZ4;
    public int MainVersion, SubVersion, ResourceVersion;
    public bool SaveBackupOnBuild = true;

    [Space]
    public List<string> ignoreFilePattern = new List<string>();
    [Space]
    public List<BundleConfig> BundleConfigs = new List<BundleConfig>();
    public void SetDefault()
    {
        OutputPath = System.IO.Path.Combine(Application.streamingAssetsPath);
        MainVersion = SubVersion = ResourceVersion = 1;
    }
    public static string GetConfigPath(bool unityPath = false)
    {
        var path = GetABBuilderPath() + ConfigName;
        if (unityPath)
        {
            path = path.Replace(Application.dataPath, "Assets");
        }
        return path;
    }
    public static string GetABBuilderPath()
    {
        DirectoryInfo di = new DirectoryInfo(Application.dataPath);
        var diArr = di.GetDirectories("ABBuilder", SearchOption.AllDirectories);
        if (diArr.Length > 1) { Debug.Log($"FolderName[ABBuilder] Duplicated"); return null; }
        return diArr[0].FullName.Replace(@"\","/")+"/";
    }
    public static string GetConfigUnityAssetPath()
    {
        return "Assets/" + GetConfigPath().Replace(Application.dataPath, "");
    }
    public static string GetBackupPath()
    {
        return Path.Combine(Application.dataPath, ConfigName + ".json");
    }
    public BuildAssetBundleOptions GetCompressType()
    {
        switch (compressType)
        {
            case ABCompressType.LZ4:
                return BuildAssetBundleOptions.ChunkBasedCompression;
            case ABCompressType.LZMA:
                return BuildAssetBundleOptions.None;
            default:
                return BuildAssetBundleOptions.UncompressedAssetBundle;
        }
    }

    public BundleConfig GetBundleConfig(string abName)
    {
        return BundleConfigs.Find((a) => { return a.bundleName == abName.ToLower(); });
    }

    public static BuilderConfigScriptable Save(BuilderConfigScriptable a)
    {
        var cfgnew = CreateInstance<BuilderConfigScriptable>();
        cfgnew.ResPath = a.ResPath;
        cfgnew.OutputPath = a.OutputPath;
        cfgnew.buildTargetPlatform = a.buildTargetPlatform;
        cfgnew.compressType = a.compressType;
        cfgnew.MainVersion = a.MainVersion;
        cfgnew.SubVersion = a.SubVersion;
        cfgnew.ResourceVersion = a.ResourceVersion;
        cfgnew.SaveBackupOnBuild = a.SaveBackupOnBuild;
        cfgnew.ignoreFilePattern = a.ignoreFilePattern;
        cfgnew.BundleConfigs = a.BundleConfigs;
        var cfgPath = GetConfigPath();
        var metaPath = cfgPath + ".meta";
        if (File.Exists(cfgPath))
        {
            File.Delete(cfgPath);
            if (File.Exists(metaPath))
                File.Delete(metaPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        AssetDatabase.CreateAsset(cfgnew, GetConfigUnityAssetPath());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return cfgnew;
    }

    [System.Serializable]
    public class BundleConfig
    {
        public string bundleName = "";
        public List<string> directories = new List<string>();
        public BundleLoadType loadType;
    }

    public string GetBundleName(string fileName)
    {
        var res = BundleConfigs.Find((bnd) =>
        {
            var d = bnd.directories.Find((dir) =>
            {
                return fileName.Contains(dir.Replace('/', '\\'));
            });
            return d != null;
        });
        return res == null ? "" : res.bundleName;
    }
}

#endif
