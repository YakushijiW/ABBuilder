#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEditor;

public class ABBuildEditor : Editor
{
    #region Build
    [MenuItem("ABBuilder/Build All", false, 2)]
    public static void OneKeyBuildAll()
    {
        BuildBasic();
        BuildHotfix();
    }

    [MenuItem("ABBuilder/Build Basic Bundles", false, 3)]
    public static void BuildBasic()
    {
        var cfg = GetBuilderConfig();
        var basicBundleDatas = cfg.BundleDatas.FindAll((a) => { return a.bundleType == ABType.Basic || a.bundleType == ABType.BasicAndHotfix; });
        var sepList = basicBundleDatas.FindAll((a) =>
        {
            return !a.packTogether;
        });
        var togList = basicBundleDatas.FindAll((a) =>
        {
            return a.packTogether;
        });

        var ipost = cfg.ignoreFilePattern;
        var idir = cfg.ignoreDirectory;

        var abVariant = ABBuildConfig.VARIANT_AB;
        string outputPath = Application.streamingAssetsPath;
        string mainABName = cfg.buildTargetPlatform.ToString();
        string finalOutputPath = Path.Combine(outputPath, mainABName);
        List<AssetBundleBuild> listBuilds = new List<AssetBundleBuild>();
        foreach (var sep in sepList)
        {
            void BindABName(FileInfo file)
            {
                var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
                // 将路径转换为Unity项目中的相对路径
                string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

                // 获取资产
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
                if (asset == null)
                {
                    Debug.LogWarning("Failed to load asset at path: " + file.FullName);
                    return;
                }
                // 设置资产包名
                string saveBundleName = file.Name;
                if (file.Extension.Length > 0)
                    saveBundleName = saveBundleName.Remove(file.Name.Length - file.Extension.Length, file.Extension.Length);
                var curBundleName = sep.bundleName + $"/{saveBundleName}";
                AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
                importer.assetBundleName = curBundleName;
                importer.assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant;
                listBuilds.Add(new AssetBundleBuild()
                {
                    assetBundleName = curBundleName,
                    assetBundleVariant = importer.assetBundleVariant,
                    addressableNames = new string[] { },
                    assetNames = new string[] { unityAssetPath },
                });
            }

            foreach (var dir in sep.directories)
            {
                string resPath = Application.dataPath.Replace("Assets", "") + dir;
                Helpers.HandleFilesInDirectory(new DirectoryInfo(resPath), false, BindABName, null, ipost, idir);
            }
        }
        foreach (var tog in togList)
        {
            AssetBundleBuild curBuild = new AssetBundleBuild()
            {
                assetBundleName = tog.bundleName,
                addressableNames = new string[] { },
                assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant,
            };
            List<string> arrAsset = new List<string>();
            void BindABName(FileInfo file)
            {
                var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
                // 将路径转换为Unity项目中的相对路径
                string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

                // 获取资产
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
                if (asset == null)
                {
                    Debug.LogWarning("Failed to load asset at path: " + file.FullName);
                    return;
                }
                // 设置资产包名
                var curBundleName = tog.bundleName;
                AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
                importer.assetBundleName = curBundleName;
                importer.assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant;
                arrAsset.Add(unityAssetPath);
            }
            foreach (var dir in tog.directories)
            {
                string resPath = Application.dataPath.Replace("Assets", "") + dir;
                Helpers.HandleFilesInDirectory(new DirectoryInfo(resPath), false, BindABName, null, ipost, idir);
                curBuild.assetNames = arrAsset.ToArray();
            }
            listBuilds.Add(curBuild);
        }
        AssetBundleBuild[] buildList = listBuilds.ToArray();
        if (Directory.Exists(finalOutputPath))
            Directory.Delete(finalOutputPath, true);
        Directory.CreateDirectory(finalOutputPath);
        var manifest = BuildPipeline.BuildAssetBundles(finalOutputPath, buildList, cfg.GetCompressType(), cfg.buildTargetPlatform);
        string slog = $"Asset Bundle Manifest[App]: \n";
        foreach (var bundle in manifest.GetAllAssetBundles())
        {
            slog += $"bundle[{bundle}] hash: " + manifest.GetAssetBundleHash(bundle) + "\n";
        }
        Debug.Log(slog);

        #region Version File
        var verPath = Path.Combine(finalOutputPath + $"/{ABBuildConfig.VERSION_FILE_NAME}");
        //if (File.Exists(catalogPath)) File.Delete(catalogPath);
        using (var fs = new FileStream(verPath, FileMode.OpenOrCreate))
        {
            // app包version.txt资源版本永远不高于hotfix包version.txt资源版本, 因此设置为1
            int mainVersion = cfg.MainVersion, subVersion = cfg.SubVersion, resVersion = 1;

            string line1 = $"{mainVersion}{ABBuildConfig.VERSION_FILE_SPLITER}" +
                $"{subVersion}{ABBuildConfig.VERSION_FILE_SPLITER}" +
                $"{resVersion}\n";

            var bytes1 = System.Text.Encoding.UTF8.GetBytes(line1);
            fs.Write(bytes1, 0, bytes1.Length);
        }
        Debug.Log("version file[App] built successfully at: " + cfg.FinalOutputPath);
        #endregion

        #region Hash File
        var hashPath = Path.Combine(finalOutputPath, ABBuildConfig.HASH_FILE_NAME);
        List<string> BasicBundlePaths = new List<string>();
        using (var fsHash = new FileStream(hashPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            string abInfos = "";
            var allabs = manifest.GetAllAssetBundles();
            foreach (var ab in allabs)
            {
                var abname = ab.Substring(0, ab.Length - abVariant.Length);
                string dataName = abname;
                if (dataName.Contains('/'))
                    dataName = dataName.Split('/')[0];
                var abpath = Path.Combine(finalOutputPath + $"/{ab}");
                var size = File.ReadAllBytes(abpath).Length;
                var md5 = Helpers.ParseToMD5(abpath);
                var data = cfg.GetBundleData(dataName);
                if (data == null)
                {
                    Debug.LogError($"Build Failed: Not found AssetBundle Name in BuilderConfig: {dataName}");
                    return;
                }
                var loadType = data.bundleType;
                var encrypt = data.encrypt;
                if (loadType == ABType.Basic)
                    BasicBundlePaths.Add(abpath);

                var info = new ABHashInfo()
                {
                    abName = abname,
                    size = size,
                    type = loadType,
                    hash = md5,
                    Encrypt = encrypt,
                };

                abInfos += ABHashInfo.ToString(info) + '\n';
            }

            var bytes1 = System.Text.Encoding.UTF8.GetBytes(abInfos);
            fsHash.Write(bytes1, 0, bytes1.Length);
        }
        Debug.Log("hash file[App] built successfully at: " + finalOutputPath);
        #endregion

        #region Encrypt
        string key = ABBuildConfig.BundleEncryptKey, iv = ABBuildConfig.BundleEncryptIV;
        var sepEncrypt = sepList.FindAll(a => a.encrypt > 0);
        var togEncrypt = togList.FindAll(a => a.encrypt > 0);
        void EncryptAB(FileInfo file)
        {
            var encrypted = Helpers.EncryptAES(File.ReadAllBytes(file.FullName), key, iv);
            File.WriteAllBytes(file.FullName.Replace(file.Extension, ABBuildConfig.VARIANT_AB_ENCRYPT), encrypted);
            string metaFile = file.FullName + ".meta";
            if (File.Exists(metaFile))
                File.Delete(metaFile);
            file.Delete();
        }
        foreach (var item in sepEncrypt)
        {
            var dir = new DirectoryInfo(finalOutputPath + $"/{item.bundleName}");
            Helpers.HandleFilesInDirectory(dir, false, EncryptAB, abVariant, ipost, idir);
        }
        foreach (var item in togEncrypt)
        {
            var file = new FileInfo(finalOutputPath + $"/{item.bundleName}{abVariant}");
            EncryptAB(file);
        }
        #endregion


        AssetDatabase.Refresh();
    }
    [MenuItem("ABBuilder/Build Hotfix Bundles", false, 4)]
    public static void BuildHotfix()
    {
        var cfg = GetBuilderConfig();
        var basicBundleDatas = cfg.BundleDatas.FindAll((a) => { return a.bundleType != ABType.Basic; });
        var sepList = basicBundleDatas.FindAll((a) =>
        {
            return !a.packTogether;
        });
        var togList = basicBundleDatas.FindAll((a) =>
        {
            return a.packTogether;
        });
        var ipost = cfg.ignoreFilePattern;
        var idir = cfg.ignoreDirectory;

        var abVariant = ABBuildConfig.VARIANT_AB;
        string outputPath = cfg.OutputPath;
        string mainABName = cfg.buildTargetPlatform.ToString();
        string finalOutputPath = Path.Combine(outputPath, mainABName);
        List<AssetBundleBuild> listBuilds = new List<AssetBundleBuild>();
        foreach (var sep in sepList)
        {
            void BindABName(FileInfo file)
            {
                var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
                // 将路径转换为Unity项目中的相对路径
                string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

                // 获取资产
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
                if (asset == null)
                {
                    Debug.LogWarning("Failed to load asset at path: " + file.FullName);
                    return;
                }
                // 设置资产包名
                string saveBundleName = file.Name;
                if (file.Extension.Length > 0)
                    saveBundleName = saveBundleName.Remove(file.Name.Length - file.Extension.Length, file.Extension.Length);
                var curBundleName = sep.bundleName + $"/{saveBundleName}";
                AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
                importer.assetBundleName = curBundleName;
                importer.assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant;
                listBuilds.Add(new AssetBundleBuild()
                {
                    assetBundleName = curBundleName,
                    assetBundleVariant = importer.assetBundleVariant,
                    addressableNames = new string[] { },
                    assetNames = new string[] { unityAssetPath },
                });
            }

            foreach (var dir in sep.directories)
            {
                string resPath = Application.dataPath.Replace("Assets", "") + dir;
                Helpers.HandleFilesInDirectory(new DirectoryInfo(resPath), false, BindABName, null, ipost, idir);
            }
        }
        foreach (var tog in togList)
        {
            AssetBundleBuild curBuild = new AssetBundleBuild()
            {
                assetBundleName = tog.bundleName,
                addressableNames = new string[] { },
                assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant,
            };
            List<string> arrAsset = new List<string>();
            void BindABName(FileInfo file)
            {
                var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
                // 将路径转换为Unity项目中的相对路径
                string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

                // 获取资产
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
                if (asset == null)
                {
                    Debug.LogWarning("Failed to load asset at path: " + file.FullName);
                    return;
                }
                // 设置资产包名
                var curBundleName = tog.bundleName;
                AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
                importer.assetBundleName = curBundleName;
                importer.assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant;
                arrAsset.Add(unityAssetPath);
            }
            foreach (var dir in tog.directories)
            {
                string resPath = Application.dataPath.Replace("Assets", "") + dir;
                Helpers.HandleFilesInDirectory(new DirectoryInfo(resPath), false, BindABName, null, ipost, idir);
                curBuild.assetNames = arrAsset.ToArray();
            }
            listBuilds.Add(curBuild);
        }
        AssetBundleBuild[] buildList = listBuilds.ToArray();
        if (Directory.Exists(finalOutputPath))
            Directory.Delete(finalOutputPath, true);
        Directory.CreateDirectory(finalOutputPath);
        var manifest = BuildPipeline.BuildAssetBundles(finalOutputPath, buildList, cfg.GetCompressType(), cfg.buildTargetPlatform);
        string slog = $"Asset Bundle Manifest[Hotfix]: \n";
        foreach (var bundle in manifest.GetAllAssetBundles())
        {
            slog += $"bundle[{bundle}] hash: " + manifest.GetAssetBundleHash(bundle) + "\n";
        }
        Debug.Log(slog);

        #region Version File
        var verPath = Path.Combine(finalOutputPath + $"/{ABBuildConfig.VERSION_FILE_NAME}");
        //if (File.Exists(catalogPath)) File.Delete(catalogPath);
        using (var fs = new FileStream(verPath, FileMode.OpenOrCreate))
        {
            // hotfix包的resVersion永远高于basic包的resVersion
            int mainVersion = cfg.MainVersion, subVersion = cfg.SubVersion, resVersion = cfg.ResourceVersion;

            string line1 = $"{mainVersion}{ABBuildConfig.VERSION_FILE_SPLITER}" +
                $"{subVersion}{ABBuildConfig.VERSION_FILE_SPLITER}" +
                $"{resVersion}\n";

            var bytes1 = System.Text.Encoding.UTF8.GetBytes(line1);
            fs.Write(bytes1, 0, bytes1.Length);
        }
        Debug.Log("version file[Hotfix] built successfully at: " + cfg.FinalOutputPath);
        #endregion

        #region Hash File
        var hashPath = Path.Combine(finalOutputPath, ABBuildConfig.HASH_FILE_NAME);
        List<string> BasicBundlePaths = new List<string>();
        using (var fsHash = new FileStream(hashPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            string abInfos = "";
            var allabs = manifest.GetAllAssetBundles();
            foreach (var ab in allabs)
            {
                var abname = ab.Substring(0, ab.Length - abVariant.Length);
                string dataName = abname;
                if (dataName.Contains('/'))
                    dataName = dataName.Split('/')[0];
                var abpath = Path.Combine(finalOutputPath + $"/{ab}");
                var size = File.ReadAllBytes(abpath).Length;
                var md5 = Helpers.ParseToMD5(abpath);
                var data = cfg.GetBundleData(dataName);
                if (data == null)
                {
                    Debug.LogError($"Build Failed: Not found AssetBundle Name in BuilderConfig: {dataName}");
                    return;
                }
                var loadType = data.bundleType;
                var encrypt = data.encrypt;
                if (loadType == ABType.Basic)
                    BasicBundlePaths.Add(abpath);

                var info = new ABHashInfo()
                {
                    abName = abname,
                    size = size,
                    type = loadType,
                    hash = md5,
                    Encrypt = encrypt,
                };

                abInfos += ABHashInfo.ToString(info) + '\n';
            }

            var bytes1 = System.Text.Encoding.UTF8.GetBytes(abInfos);
            fsHash.Write(bytes1, 0, bytes1.Length);
        }
        Debug.Log("hash file[Hotfix] built successfully at: " + finalOutputPath);
        #endregion

        #region Encrypt
        string key = ABBuildConfig.BundleEncryptKey, iv = ABBuildConfig.BundleEncryptIV;
        var sepEncrypt = sepList.FindAll(a => a.encrypt > 0);
        var togEncrypt = togList.FindAll(a => a.encrypt > 0);
        void EncryptAB(FileInfo file)
        {
            var encrypted = Helpers.EncryptAES(File.ReadAllBytes(file.FullName), key, iv);
            File.WriteAllBytes(file.FullName.Replace(file.Extension, ABBuildConfig.VARIANT_AB_ENCRYPT), encrypted);
            string metaFile = file.FullName + ".meta";
            if (File.Exists(metaFile))
                File.Delete(metaFile);
            file.Delete();
        }
        foreach (var item in sepEncrypt)
        {
            var dir = new DirectoryInfo(finalOutputPath + $"/{item.bundleName}");
            Helpers.HandleFilesInDirectory(dir, false, EncryptAB, abVariant, ipost, idir);
        }
        foreach (var item in togEncrypt)
        {
            var file = new FileInfo(finalOutputPath + $"/{item.bundleName}{abVariant}");
            EncryptAB(file);
        }
        #endregion

        AssetDatabase.Refresh();
    }
    #endregion
    
    #region Tools
    // // HybridClr和XLua方案无法完美兼容，XLua有大量internal方法(AOT)，gen的cs代码(Hotfix)无法访问。
    // // 除非确保打包出去的LuaCallCSharp部分不会被HybridClr更新，全部置于AOT中。
    [MenuItem("ABBuilder/Tools/Move XLua Gen")]
    public static void MoveXLuaGen()
    {
        // 源文件夹路径
        string sourceDirectory = @"E:\UnityProjects\my_xlua_framework\Assets\XLua\Gen";

        // 目标文件夹路径
        string targetDirectory = @"E:\UnityProjects\my_xlua_framework\Assets\Scripts\AOT\XLua\Gen";

        // 空文件夹或不存在则不处理
        if (Directory.Exists(sourceDirectory))
        {
            var dir = new DirectoryInfo(sourceDirectory);
            if (dir.GetFiles("*", SearchOption.AllDirectories).Length == 0)
                return;
        }
        else { Debug.LogWarning($"source dir NOT exist: {sourceDirectory}"); return; }

        // 调用剪切文件夹的方法
        CutAndPaste(sourceDirectory, targetDirectory, true);
    }
    static void CutAndPaste(string sourceDirectory, string targetDirectory, bool deleteSourceDir = true)
    {
        try
        {
            if (deleteSourceDir)
            {
                // 确保目标文件夹不存在
                if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, true);
                }

                // 移动文件夹
                Directory.Move(sourceDirectory, targetDirectory);
                Debug.Log($"文件夹已成功移动到：{targetDirectory}");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return;
            }

            // 确保目标文件夹存在
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // 复制所有文件
            foreach (var file in Directory.GetFiles(sourceDirectory))
            {
                string destFile = Path.Combine(targetDirectory, Path.GetFileName(file));
                File.Copy(file, destFile, true);  // 如果目标文件已存在，覆盖它
            }

            // 复制所有子文件夹
            foreach (var subDir in Directory.GetDirectories(sourceDirectory))
            {
                string destSubDir = Path.Combine(targetDirectory, Path.GetFileName(subDir));
                Directory.CreateDirectory(destSubDir);
                // 递归复制子文件夹中的内容
                CopyDirectory(subDir, destSubDir);
            }

            // 删除源文件夹
            Directory.Delete(sourceDirectory, true);  // 第二个参数为true表示删除文件夹及其内容

            Debug.Log($"文件夹已成功移动到：{targetDirectory}");
        }
        catch (Exception ex)
        {
            Debug.Log($"发生错误：{ex.Message}");
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
    static void CopyDirectory(string sourceDir, string targetDir)
    {
        // 复制文件
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);  // 如果目标文件已存在，覆盖它
        }

        // 递归复制子文件夹
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            Directory.CreateDirectory(destSubDir);
            CopyDirectory(subDir, destSubDir);  // 递归复制
        }
    }

    [MenuItem("ABBuilder/Tools/Copy HybridClr Gen to .bytes")]
    public static void CopyHybridGensToBytes()
    {
        string sourceDir = @"E:\UnityProjects\my_xlua_framework\HybridCLRData\HotUpdateDlls\StandaloneWindows64";
        string targetDir = @"E:\UnityProjects\my_xlua_framework\Assets\Res\DLLs";
        List<string> ignore = new List<string>
        {

        };
        string postfix = "*.dll";
        string pattern = $"*{postfix}";
        string addPostfix = ".bytes";

        if (!Directory.Exists(sourceDir)) { Debug.Log($"source path NOT exists: [{sourceDir}]"); return; }
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);
        var dirInfo = new DirectoryInfo(sourceDir);
        var files = dirInfo.GetFiles(pattern, SearchOption.AllDirectories);
        string log = $"Start Copy DLL Files:\n";
        foreach (var file in files)
        {
            if (ignore.Contains(file.Name)) continue;
            var tarFinalPath = targetDir + '\\' + file.Name + addPostfix;
            File.Copy(file.FullName, tarFinalPath);
            log += $"[{file.Name}] Copied to path [{tarFinalPath}]\n";
        }
        Debug.Log($"{log}");
        AssetDatabase.Refresh();
    }
    [MenuItem("ABBuilder/Tools/Clear Persistent Path")]
    public static void ClearPersistentPath()
    {
        var buildPath = Application.persistentDataPath;
        if (Directory.Exists(buildPath))
        {
            EditorConfirmWindow wnd = (EditorConfirmWindow)EditorWindow.GetWindow(typeof(EditorConfirmWindow), true);
            wnd.titleContent.text = $"Clear Persistent Path?";
            wnd.ShowConfirm($"Delete all files in [{buildPath}]?", new Vector2(900, 60), () =>
            {
                Directory.Delete(buildPath, true);
                Directory.CreateDirectory(buildPath);
                Debug.Log($"Cleared");
                wnd.Close();
            }, () => { wnd.Close(); }, "");
        }
    }
    [MenuItem("ABBuilder/Tools/Clear Streaming Assets Path")]
    public static void ClearStreamingAssetsPath()
    {
        var buildPath = Application.streamingAssetsPath;
        if (Directory.Exists(buildPath))
        {
            EditorConfirmWindow wnd = (EditorConfirmWindow)EditorWindow.GetWindow(typeof(EditorConfirmWindow), true);
            wnd.titleContent.text = $"Clear Persistent Path?";
            wnd.ShowConfirm($"Delete all files in [{buildPath}]?", new Vector2(900, 60), () =>
            {
                Directory.Delete(buildPath, true);
                Directory.CreateDirectory(buildPath);
                Debug.Log($"Cleared");
                AssetDatabase.Refresh();
                wnd.Close();
            }, () => { wnd.Close(); }, "");
        }
    }
    [MenuItem("ABBuilder/Tools/Delete Gen .Manifest Files")]
    public static void DeleteGenManifestFiles()
    {
        var cfg = GetBuilderConfig();
        if (cfg == null) { Debug.LogError($"DeleteManifestFiles error: config file NOT found"); return; }
        string path1 = cfg.FinalOutputPath;
        string path2 = ABBuildConfig.AppPath;
        if (Directory.Exists(path1))
        {
            Helpers.HandleFilesInDirectory(new DirectoryInfo(path1), false, (file) =>
            {
                string meta = file.FullName + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);
                file.Delete();
            }, "*.manifest", null, null);
        }
        Debug.Log($"Cleared at path: [{path1}]");
        if (Directory.Exists(path2))
        {
            Helpers.HandleFilesInDirectory(new DirectoryInfo(path2), false, (file) =>
            {
                string meta = file.FullName + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);
                file.Delete();
            }, "*.manifest", null, null);
        }
        Debug.Log($"Cleared at path: [{path2}]");
        AssetDatabase.Refresh();
    }
    #endregion

    #region Handle Hotfix AssetBundles
    [MenuItem("ABBuilder/Handle Hotfix/Open Local Path", false, 30)]
    public static void OpenLocalABPath()
    {
        string path = ABBuildConfig.LocalABDirectory;
        if (Directory.Exists(path))
        {
            System.Diagnostics.Process.Start("explorer", path.Replace("/", @"\"));
        }
        else Debug.Log($"Local AssetBundle Path NOT Exists: {path}");
    }
    [MenuItem("ABBuilder/Handle Hotfix/Delete Local Path AssetBundles", false, 31)]
    public static void DeleteLocalABFiles()
    {
        string path = ABBuildConfig.LocalPath;
        if (Directory.Exists(path))
        {
            Helpers.HandleFilesInDirectory(new DirectoryInfo(path), false, (file) =>
            {
                file.Delete();
            });
        }
    }
    [MenuItem("ABBuilder/Handle Hotfix/Copy Hotfix AssetBundles to Local Path", false, 32)]
    public static void CopyHotfixAB2LocalPath()
    {
        var cfg = GetBuilderConfig();
        if (cfg == null) { Debug.LogError($"DeleteManifestFiles error: config file NOT found"); return; }
        string buildPath = cfg.FinalOutputPath;
        string pathSource = buildPath.Replace("/",@"\");
        string pathTarget = ABBuildConfig.LocalABDirectory.Replace("/", @"\");
        try
        {
            if (!Directory.Exists(pathTarget))
                Directory.CreateDirectory(pathTarget);
            CopyDirectory(pathSource, pathTarget);
            Debug.Log($"Copy Hotfix AssetBundles to Local Path: Finished");
        }
        catch(Exception e)
        {
            Debug.LogError($"CopyHotfixAB2LocalPath ERROR: \n{e.ToString()}");
        }
    }
    #endregion

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

            var bundleCfg = cfg.GetBundleData(importer.assetBundleName);
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
    [MenuItem("ABBuilder/Clear All AB-Asset Connection")]
    public static void ClearAllConnection()
    {
        BuilderConfigScriptable cfg = GetBuilderConfig();
        if (cfg == null)
        {
            Debug.LogError($"Config Not Exist");
            return;
        }
        var di = new DirectoryInfo(Application.dataPath);
        List<string> iDirs = new List<string>();
        foreach (var item in cfg.ignoreDirectory)
        {
            var path = item.Replace("/", @"\");
            if (path.EndsWith('\\')) path = item.Remove(item.Length - 1, 1);
            iDirs.Add(path);
        }
        void ClearBind(FileInfo file)
        {
            var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, "")).Replace(@"\", "/");
            // 将路径转换为Unity项目中的相对路径
            string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
            var dirRoots = System.Linq.Enumerable.ToList(unityAssetPath.Split('/'));
            if (!string.IsNullOrEmpty(dirRoots.Find((a) =>
            {
                return a.EndsWith('~') || a == ".git";
            }))) return;

            AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
            if (string.IsNullOrEmpty(importer.assetBundleName)) return;
            var bundleCfg = cfg.GetBundleData(importer.assetBundleName);
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
        Helpers.HandleFilesInDirectory(di, false, ClearBind, null, cfg.ignoreFilePattern, iDirs);
        var files = di.GetFiles("*", SearchOption.AllDirectories);
        Debug.Log($"Clear All AB-Asset Connection Success");
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
    #region Obsolete
    //    [MenuItem("ABBuilder/OneKeyBuild")]
    //    public static void OneKeyBuild()
    //    {
    //        BuilderConfigScriptable cfg = GetBuilderConfig();
    //        if (cfg == null)
    //        {
    //            Debug.LogError($"Config Not Exist");
    //            return;
    //        }
    //        #region BindAB
    //        var abVariant = ABBuildConfig.VARIANT_AB;
    //        List<string> iDirs = new List<string>();
    //        foreach (var item in cfg.ignoreDirectory)
    //        {
    //            var path = item.Replace("/", @"\");
    //            if (path.EndsWith('\\')) path = item.Remove(item.Length - 1, 1);
    //            iDirs.Add(path);
    //        }
    //        foreach (var bundleData in cfg.BundleDatas)
    //        {
    //            foreach (var dir in bundleData.directories)
    //            {
    //                if (!Directory.Exists(dir))
    //                {
    //                    Debug.LogWarning($"NOT found path: [{dir}], assets bind to ab [{bundleData.bundleName}] FAILED");
    //                    continue;
    //                }
    //                DirectoryInfo dirInfo = new DirectoryInfo(dir);
    //                void BindABName(FileInfo file)
    //                {
    //                    var assetPath = Path.Combine("Assets/", file.FullName.Replace(Application.dataPath, ""));
    //                    // 将路径转换为Unity项目中的相对路径
    //                    string unityAssetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);

    //                    // 获取资产
    //                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityAssetPath);
    //                    if (asset == null)
    //                    {
    //                        Debug.LogWarning("Failed to load asset at path: " + file.FullName);
    //                        return;
    //                    }

    //                    // 设置资产包名
    //                    string bundleName = GetBundleName(assetPath);
    //                    AssetImporter importer = AssetImporter.GetAtPath(unityAssetPath);
    //                    importer.assetBundleName = bundleName;
    //                    importer.assetBundleVariant = abVariant.Length > 0 ? abVariant.Substring(1, abVariant.Length - 1) : abVariant;
    //                }
    //                Helpers.HandleFilesInDirectory(dirInfo, false, BindABName, "*", cfg.ignoreFilePattern, iDirs);
    //                var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
    //            }
    //        }

    //        Debug.Log($"Bind Finished");
    //        #endregion

    //        #region Build
    //        if (Directory.Exists(cfg.FinalOutputPath))
    //            Directory.Delete(cfg.FinalOutputPath, true);
    //        Directory.CreateDirectory(cfg.FinalOutputPath);
    //        var abm = BuildPipeline.BuildAssetBundles(cfg.FinalOutputPath, cfg.GetCompressType(), cfg.buildTargetPlatform);
    //        AssetDatabase.SaveAssets();
    //        // Refresh the Asset Database
    //        //AssetDatabase.Refresh();
    //        Debug.Log("AssetBundles built successfully at: " + cfg.FinalOutputPath);
    //        #endregion

    //        #region Version File
    //        var catalogPath = Path.Combine(cfg.FinalOutputPath + $"/{ABBuildConfig.VERSION_FILE_NAME}");
    //        //if (File.Exists(catalogPath)) File.Delete(catalogPath);
    //        using (var fs = new FileStream(catalogPath, FileMode.OpenOrCreate))
    //        {
    //            int mainVersion = cfg.MainVersion, subVersion = cfg.SubVersion, resVersion = cfg.ResourceVersion;

    //            string line1 = $"{mainVersion}{ABBuildConfig.VERSION_FILE_SPLITER}" +
    //                $"{subVersion}{ABBuildConfig.VERSION_FILE_SPLITER}" +
    //                $"{resVersion}\n";

    //            var bytes1 = System.Text.Encoding.UTF8.GetBytes(line1);
    //            fs.Write(bytes1, 0, bytes1.Length);
    //        }
    //        Debug.Log("version file built successfully at: " + cfg.FinalOutputPath);
    //        #endregion

    //        #region Hash File
    //        var hashPath = Path.Combine(cfg.FinalOutputPath, ABBuildConfig.HASH_FILE_NAME);
    //        List<string> BasicBundlePaths = new List<string>();
    //        using (var fsHash = new FileStream(hashPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    //        {
    //            string abInfos = "";
    //            var allabs = abm.GetAllAssetBundles();
    //            foreach (var ab in allabs)
    //            {
    //                var abname = ab.Substring(0, ab.Length - abVariant.Length);
    //                var abpath = Path.Combine(cfg.FinalOutputPath + $"/{ab}");
    //                var size = File.ReadAllBytes(abpath).Length;
    //                var md5 = Helpers.ParseToMD5(abpath);
    //                var data = cfg.GetBundleData(abname);
    //                if (data == null)
    //                {
    //                    Debug.LogError($"Build Failed: Not found AssetBundle Name in BuilderConfig: {abname}");
    //                    return;
    //                }
    //                var loadType = data.bundleType;
    //                var encrypt = data.encrypt;
    //                if (loadType == ABType.Basic)
    //                    BasicBundlePaths.Add(abpath);

    //                var info = new ABHashInfo() {
    //                    abName = abname,
    //                    size = size,
    //                    type = loadType,
    //                    hash = md5,
    //                    Encrypt = encrypt,
    //                };

    //                abInfos += ABHashInfo.ToString(info) + '\n';
    //}

    //            var bytes1 = System.Text.Encoding.UTF8.GetBytes(abInfos);
    //            fsHash.Write(bytes1, 0, bytes1.Length);
    //        }
    //        Debug.Log("hash file built successfully at: " + cfg.FinalOutputPath);
    //        #endregion

    //        #region HandleBasicBundles
    //        if (!Directory.Exists(Application.streamingAssetsPath))
    //            Directory.CreateDirectory(Application.streamingAssetsPath);
    //        var appFilePath = ABBuildConfig.AppABDirectory;
    //        if (!Directory.Exists(appFilePath))
    //            Directory.CreateDirectory(appFilePath);
    //        if (File.Exists(catalogPath) && File.Exists(hashPath))
    //        {
    //            File.Copy(catalogPath, appFilePath + '/' + ABBuildConfig.VERSION_FILE_NAME, true);
    //            File.Copy(hashPath, appFilePath + '/' +ABBuildConfig.HASH_FILE_NAME, true);
    //        }
    //        else Debug.LogError($"ver/hash NOT found :\nver: {catalogPath}\nhash: {hashPath}");
    //        var mainABPath = Path.Combine(cfg.FinalOutputPath, cfg.buildTargetPlatform.ToString());
    //        if (File.Exists(mainABPath))
    //        {
    //            File.Copy(mainABPath, appFilePath + '/' + cfg.buildTargetPlatform.ToString(), true);
    //            File.Copy(mainABPath + ".manifest", appFilePath + '/' + cfg.buildTargetPlatform.ToString()+".manifest", true);
    //        }
    //        else Debug.LogError($"main AssetBundle NOT found at path: {mainABPath}");
    //        foreach (var path in BasicBundlePaths)
    //        {
    //            if (File.Exists(path))
    //            {
    //                var arr = path.Replace(@"\", "/").Split('/');
    //                var fileName = arr[arr.Length - 1];
    //                var finalPath = appFilePath + '/' + fileName;
    //                File.Copy(path, finalPath, true);
    //                File.Copy(path+".manifest", finalPath+".manifest", true);
    //                File.Delete(path);
    //                File.Delete(path + ".meta");
    //                File.Delete(path + ".manifest");
    //                File.Delete(path + ".manifest.meta");
    //            }
    //        }
    //        #endregion

    //        #region Backup Config
    //        if (cfg.SaveBackupOnBuild)
    //        {
    //            var strBackup = JsonUtility.ToJson(cfg);
    //            string saveBackupPath = BuilderConfigScriptable.GetBackupPath();
    //            using (var fsBackup = new FileStream(saveBackupPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
    //            {
    //                var bytes = System.Text.Encoding.UTF8.GetBytes(strBackup);
    //                fsBackup.Write(bytes, 0, bytes.Length);
    //            }
    //        }
    //        #endregion

    //        AssetDatabase.SaveAssets();
    //        AssetDatabase.Refresh();
    //    }
    #endregion
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
                BundleData bdata = null;
                List<string> dirs = new List<string>();
                string errorLog = "";

                foreach (var path in list)
                {
                    var dir = Application.dataPath + path.Substring(6, path.Length - 6);
                    dir = dir.Replace("/", @"\");
                    foreach (var data in cfg.BundleDatas)
                    {
                        if (data.directories.Contains(dir))
                        {
                            errorLog += $"Directory [{dir}] already added to assetbundle [{data.bundleName}]";
                            break;
                        }
                    }
                    dirs.Add(dir);
                }
                if (!string.IsNullOrEmpty(errorLog)) { Debug.LogError("Bind resources to AssetBundle FAILED:\n" + errorLog); return; }
                var bundleData = cfg.GetBundleData(inpABName);

                if (bundleData != null)
                {
                    bundleData.directories.AddRange(dirs);
                }
                else
                {
                    bdata = new BundleData();
                    bdata.bundleName = inpABName;
                    bdata.bundleType = ABType.Hotfix;
                    bdata.directories = dirs;
                    bdata.encrypt = 0;
                }
                if (bdata != null)
                {
                    cfg.BundleDatas.Add(bdata);
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

                var bundleCfg = cfg.GetBundleData(importer.assetBundleName);
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