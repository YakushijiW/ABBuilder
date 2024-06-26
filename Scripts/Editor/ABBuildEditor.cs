﻿#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEditor;

public class ABBuildEditor : Editor
{

    [MenuItem("ABBuilder/Clear AB-Asset Connection")]
    public static void ClearConnection()
    {
        BuilderConfigScriptable cfg = GetBuilderConfig();
        if (cfg == null)
        {
            Debug.LogError($"Config Not Exist");
            return;
        }
        var di = new DirectoryInfo(cfg.ResPath);
        var fis = di.GetFiles("*", SearchOption.AllDirectories);
        foreach (var file in fis)
        {
            if (file.Extension == ".meta") continue;
            if (cfg.ignoreFilePattern.Contains(file.Extension)) continue;

            var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
            // 将路径转换为Unity项目中的相对路径
            string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

            // 获取资产
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
            if (asset == null)
            {
                Debug.LogWarning("Failed to load asset at path: " + unityAssetPath);
                return;
            }

            AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
            if (string.IsNullOrEmpty(importer.assetBundleName)) continue;

            var bundleCfg = cfg.GetBundleConfig(importer.assetBundleName);
            if (bundleCfg != null)
            {
                Debug.Log(di.FullName);
                var abPath = bundleCfg.directories.Find((a) => { return a == di.FullName; });
                if (!string.IsNullOrEmpty(abPath))
                {
                    bundleCfg.directories.Remove(abPath);
                }
            }

            importer.assetBundleName = "";
        }
        Debug.Log($"Clear AB-Asset Connection Success at path [{cfg.ResPath}]");
    }

    [MenuItem("ABBuilder/CreateConfig")]
    public static void CreateConfig()
    {
        string path = Application.dataPath + "/ABBuilder/" + BuilderConfigScriptable.ConfigName;
        Debug.Log(path);
        var file = GetBuilderConfig();
        if (file == null)
        {
            var cfg = BuilderConfigScriptable.CreateInstance<BuilderConfigScriptable>();
            cfg.SetDefault();
            AssetDatabase.CreateAsset(cfg, BuilderConfigScriptable.GetConfigPath(true));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        Debug.Log($"Create ABBuilder Config Success");
    }

    [MenuItem("ABBuilder/OneKeyBuild")]
    public static void OneKeyBuild()
    {
        BuilderConfigScriptable cfg = GetBuilderConfig();
        if (cfg == null)
        {
            Debug.LogError($"Config Not Exist");
            return;
        }
        #region BindAB
        var abVariant = AssetBundleManager.VARIANT_AB;
        foreach (var bundleCfg in cfg.BundleConfigs)
        {
            foreach (var dir in bundleCfg.directories)
            {
                if (!Directory.Exists(dir))
                {
                    Debug.LogWarning($"NOT found path: [{dir}], assets bind to ab [{bundleCfg.bundleName}] FAILED");
                    continue;
                }
                DirectoryInfo dirInfo = new DirectoryInfo(dir);
                var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (file.Extension == ".meta") continue;
                    if (cfg.ignoreFilePattern.Contains(file.Extension)) continue;

                    var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
                    // 将路径转换为Unity项目中的相对路径
                    string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

                    // 获取资产
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
                    if (asset == null)
                    {
                        Debug.LogWarning("Failed to load asset at path: " + unityAssetPath);
                        return;
                    }

                    // 设置资产包名
                    string bundleName = GetBundleName(assetPath);
                    AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
                    importer.assetBundleName = bundleName;
                    importer.assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant;
                }
            }
        }
        Debug.Log($"Bind Finished");
        #endregion

        #region Build
        if (Directory.Exists(cfg.FinalOutputPath))
            Directory.Delete(cfg.FinalOutputPath, true);
        Directory.CreateDirectory(cfg.FinalOutputPath);
        var abm = BuildPipeline.BuildAssetBundles(cfg.FinalOutputPath, cfg.GetCompressType(), cfg.buildTargetPlatform);
        AssetDatabase.SaveAssets();
        // Refresh the Asset Database
        AssetDatabase.Refresh();
        Debug.Log("AssetBundles built successfully at: " + cfg.FinalOutputPath);
        #endregion

        #region Version File
        var catalogPath = Path.Combine(cfg.FinalOutputPath + $"/{AssetBundleManager.VERSION_FILE_NAME}");
        //if (File.Exists(catalogPath)) File.Delete(catalogPath);
        using (var fs = new FileStream(catalogPath, FileMode.OpenOrCreate))
        {
            int mainVersion = cfg.MainVersion, subVersion = cfg.SubVersion, resVersion = cfg.ResourceVersion;

            string line1 = $"{mainVersion}{AssetBundleManager.catalogFileVersionSpliter}" +
                $"{subVersion}{AssetBundleManager.catalogFileVersionSpliter}" +
                $"{resVersion}\n";

            var bytes1 = System.Text.Encoding.UTF8.GetBytes(line1);
            fs.Write(bytes1, 0, bytes1.Length);
        }
        Debug.Log("version file built successfully at: " + cfg.FinalOutputPath);
        #endregion

        #region Hash File
        var hashPath = Path.Combine(cfg.FinalOutputPath, AssetBundleManager.HASH_FILE_NAME);
        using (var fsHash = new FileStream(hashPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            string abInfos = "";
            var allabs = abm.GetAllAssetBundles();
            foreach (var ab in allabs)
            {
                var abname = ab.Substring(0, ab.Length - abVariant.Length);
                var abpath = Path.Combine(cfg.FinalOutputPath + $"/{ab}");
                var size = File.ReadAllBytes(abpath).Length;
                var md5 = Helpers.GetFileMD5(abpath);

                var info = new ABHashInfo() { 
                    abPath = abname, 
                    size = size, 
                    type = cfg.GetBundleConfig(abname).loadType, 
                    hash = md5 
                };

                abInfos += ABHashInfo.ToString(info) + '\n';
}

            var bytes1 = System.Text.Encoding.UTF8.GetBytes(abInfos);
            fsHash.Write(bytes1, 0, bytes1.Length);
        }
        Debug.Log("hash file built successfully at: " + cfg.FinalOutputPath);
        #endregion

        #region Backup Config
        if (cfg.SaveBackupOnBuild)
        {
            var strBackup = JsonUtility.ToJson(cfg);
            string saveBackupPath = BuilderConfigScriptable.GetBackupPath();
            using (var fsBackup = new FileStream(saveBackupPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(strBackup);
                fsBackup.Write(bytes, 0, bytes.Length);
            }
        }
        #endregion

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }


    [MenuItem("Assets/ABBuilder/Add to Config")]
    public static void BindToAB()
    {
        BuilderConfigScriptable cfg = GetBuilderConfig();
        if (cfg == null)
        {
            Debug.LogError($"Config Not Exist");
            return;
        }

        List<string> list = new List<string>();
        string content = "Bind all assets in folders below to which AssetBundle?\n(If it's a new bundle, LoadType will be set to [OnStart] by default)\n\n";
        foreach (var item in Selection.assetGUIDs)
        {
            var path = AssetDatabase.GUIDToAssetPath(item);
            content += path + '\n';
            list.Add(path);
        }
        if (Selection.assetGUIDs.Length > 0)
        {
            var wnd = EditorWindow.GetWindow<EditorConfirmWindow>();
            Action<string> onOK = (inpABName) =>
            {
                inpABName = inpABName.ToLower();
                BuilderConfigScriptable.BundleConfig bundleConfig = null;
                List<string> dirs = new List<string>();
                string errorLog = "";

                foreach (var path in list)
                {
                    var dir = Application.dataPath + path.Substring(6, path.Length - 6);
                    dir = dir.Replace("/", @"\");
                    foreach (var abcfg in cfg.BundleConfigs)
                    {
                        if (abcfg.directories.Contains(dir))
                        {
                            errorLog += $"Directory [{dir}] already added to assetbundle [{abcfg.bundleName}]";
                            break;
                        }
                    }
                    dirs.Add(dir);
                }
                if (!string.IsNullOrEmpty(errorLog)) { Debug.LogError("Bind resources to AssetBundle FAILED:\n" + errorLog); return; }
                var abCfg = cfg.GetBundleConfig(inpABName);
                if (abCfg != null)
                {
                    abCfg.directories.AddRange(dirs);
                }
                else
                {
                    bundleConfig = new BuilderConfigScriptable.BundleConfig();
                    bundleConfig.bundleName = inpABName;
                    bundleConfig.loadType = BundleLoadType.OnStart;
                    bundleConfig.directories = dirs;
                }
                if (bundleConfig != null)
                {
                    cfg.BundleConfigs.Add(bundleConfig);
                }

                BuilderConfigScriptable.Save(cfg);
            };
            wnd.ShowConfirmInput(content, new Vector2(400, Selection.assetGUIDs.Length * 20 + 120), onOK);
        }
    }

    [MenuItem("Assets/ABBuilder/CopyFilePath")]
    public static void CopyFilePath()
    {
        if (Selection.assetGUIDs.Length == 1)
        {
            EditorGUIUtility.systemCopyBuffer = (Application.dataPath.Substring(0, Application.dataPath.Length - 6)+ AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0])).Replace("/",@"\");
        }
    }
    [MenuItem("Assets/ABBuilder/Clear AB-Asset Connection")]
    public static void ClearConnectionAtSelectedPath()
    {
        BuilderConfigScriptable cfg = GetBuilderConfig();
        if (cfg == null)
        {
            Debug.LogError($"Config Not Exist");
            return;
        }

        bool changed = false;

        foreach (var guid in Selection.assetGUIDs)
        {
            var dir = AssetDatabase.GUIDToAssetPath(guid);

            if (!Directory.Exists(dir)) { continue; }

            var di = new DirectoryInfo(dir);
            var files = di.GetFiles("*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (file.Extension == ".meta") continue;

                var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
                // 将路径转换为Unity项目中的相对路径
                string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

                // 获取资产
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
                if (asset == null)
                {
                    Debug.LogWarning("Failed to load asset at path: " + unityAssetPath);
                    return;
                }

                AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);

                if (string.IsNullOrEmpty(importer.assetBundleName)) continue;

                var bundleCfg = cfg.GetBundleConfig(importer.assetBundleName);
                if (bundleCfg != null)
                {
                    var abPath = bundleCfg.directories.Find((a) => { return a == di.FullName; });
                    if (!string.IsNullOrEmpty(abPath)) 
                    { 
                        bundleCfg.directories.Remove(abPath);
                        changed = true;
                        Debug.Log($"Removed path [{dir}] in BuilderConfig success, AB[{importer.assetBundleName}]-" +
                            $"Asset[{file.FullName}]");
                    }
                    else
                    {
                        Debug.Log($"Clear AB[{importer.assetBundleName}]-" +
                            $"Asset[{file.FullName}] Connection Success at path [{dir}]");
                    }
                }
                importer.assetBundleVariant = "";
                importer.assetBundleName = "";

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        
        if (changed)
        {
            BuilderConfigScriptable.Save(cfg);
        }
    }

    //[MenuItem("ABBuilder/Test")]
    public static void Test()
    {

    }

    // 未完成功能
    //[MenuItem("ABBuilder/OneKeyBuildSeparately")]
    public static void OneKeyBuildSeparately()
    {
        BuilderConfigScriptable cfg = GetBuilderConfig();
        if (cfg == null)
        {
            Debug.LogError($"Config Not Found");
            return;
        }
        #region BindAB
        var abVariant = "";
        if (!Directory.Exists(cfg.ResPath))
        {
            Debug.LogWarning("directory not found at path: " + cfg.ResPath);
            return;
        }
        var di = new DirectoryInfo(cfg.ResPath);
        var fileInfos = di.GetFiles("*",SearchOption.AllDirectories);
        List<AssetBundleBuild> builds = new List<AssetBundleBuild>();
        for (int i = 0; i < fileInfos.Length; i++)
        {
            var file = fileInfos[i];
            if (file.Extension == ".meta") continue;
            if (cfg.ignoreFilePattern.Contains(file.Extension)) continue;

            var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
            // 将路径转换为Unity项目中的相对路径
            string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

            // 获取资产
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
            if (asset == null)
            {
                Debug.LogWarning("Failed to load asset at path: " + unityAssetPath);
                return;
            }

            // 设置资产包名
            string bundleName = file.FullName.Replace(@"\", "/").Replace(cfg.ResPath.Replace(@"\", "/"), "");
            bundleName = bundleName.Remove(0, 1);
            bundleName = bundleName.Split('.')[0];
            var dirs = bundleName.Split('/');
            string assetName = dirs[dirs.Length - 1];

            AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
            importer.assetBundleName = assetName;
            importer.assetBundleVariant = abVariant;
            AssetBundleBuild build = new AssetBundleBuild
            {
                assetBundleName = assetName,
                assetBundleVariant = abVariant,
                assetNames = new string[] { unityAssetPath },
                addressableNames = new string[] { assetName },
            };
            builds.Add(build);
            Debug.Log($"ab[{build.assetBundleName}]: unityAssetPath:[{unityAssetPath}]");

        }

        var buildArray = builds.ToArray();
        #endregion

        #region Build
        if (Directory.Exists(cfg.FinalOutputPath))
            Directory.Delete(cfg.FinalOutputPath, true);
        Directory.CreateDirectory(cfg.FinalOutputPath);
        var abm = BuildPipeline.BuildAssetBundles(cfg.FinalOutputPath, buildArray, cfg.GetCompressType(), cfg.buildTargetPlatform);
        AssetDatabase.SaveAssets();
        // Refresh the Asset Database
        AssetDatabase.Refresh();

        if (abm == null) { Debug.LogError($"AssetBundles build FAILED at [{cfg.FinalOutputPath}]"); return; }

        Debug.Log("AssetBundles built successfully at: " + cfg.FinalOutputPath);
        #endregion

        #region Version & Hash Files

        var verPath = Path.Combine(cfg.FinalOutputPath, AssetBundleManager.VERSION_FILE_NAME);
        using (var fs = new FileStream(verPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            string content = $"{cfg.MainVersion}{AssetBundleManager.catalogFileVersionSpliter}" +
                $"{cfg.SubVersion}{AssetBundleManager.catalogFileVersionSpliter}" +
                $"{cfg.ResourceVersion}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            fs.Write(bytes, 0, bytes.Length);
        }

        foreach (var item in abm.GetAllAssetBundles())
        {
            var abPath = Path.Combine(Application.streamingAssetsPath, "StandaloneWindows64/" + item);
            var ab = AssetBundle.LoadFromFile(abPath);
            foreach (var asset in ab.GetAllAssetNames())
            {
                Debug.Log(asset);
            }
        }
        AssetBundle.UnloadAllAssetBundles(true);
        //var hashPath = Path.Combine(cfg.FinalOutputPath, AssetbundleManager.HASH_FILE_NAME);
        //using (var fs = new FileStream(hashPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        //{
        //    string content = "";
        //    foreach (var item in abm.GetAllAssetBundles())
        //    {

        //    }
        //}
        #endregion
    }


    // 根据文件路径获取资产包名
    static string GetBundleName(string filePath)
    {
        var cfg = GetBuilderConfig();
        if (cfg == null) return "ConfigNotFound";
        // 这里可以根据您的需求来定义资产包名的逻辑

        var name = cfg.GetBundleName(filePath);
        if (!string.IsNullOrEmpty(name))
            return name.ToLower();

        return "default pack";
    }

    static BuilderConfigScriptable GetBuilderConfig()
    {
        var path = BuilderConfigScriptable.GetConfigPath();
        if (File.Exists(path))
        {
            return AssetDatabase.LoadAssetAtPath<BuilderConfigScriptable>(BuilderConfigScriptable.GetConfigPath(true));
        }
        return null;
    }
}
#endif