using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class AssetBundleManager : MonoBehaviour
{
    public enum CheckVersionResult
    {
        success,

        //updateMain, // 进入游戏后检测

        updateApp,

        updateRestart,

        updateEnter,

        updateCatalog,

        error,
    }

    public bool isCustomCatalog = false;
    public const string MARK_SUCCESS = "success";
    public string remoteAddress { get { return "localhost"; } }
    public int remotePort { get { return 80; } }

    public string verFileName { get { return ABBuildConfig.VERSION_FILE_NAME; } }
    public string hashFileName { get { return ABBuildConfig.HASH_FILE_NAME; } }
    public char hashSpliter { get { return ABBuildConfig.HASH_FILE_SPLITER; } }
    public string variantAB { get { return ABBuildConfig.VARIANT_AB; } }

    public string remoteABDirectory { get { return Path.Combine($"{remoteAddress}:{remotePort}/{ABDirectoryName}"); } }
    public string localABDirectory { get { return ABBuildConfig.LocalABDirectory; } }
    public string appABDirectory { get { return ABBuildConfig.AppABDirectory; } }
    public string tempDownloadDirectory { get { return Path.Combine(Application.persistentDataPath + $"/temp"); } }
    public string ABDirectoryName { get { return ABBuildConfig.ABDirectoryName; } }

    AssetBundleManifest manifestLocal;
    AssetBundleCatalog catalogLocal;
    AssetBundleManifest manifestApp;
    AssetBundleCatalog catalogApp;

    private static AssetBundleManager instance;

    Dictionary<string, System.Action<AssetBundleRef>> loadingAssetBundles = new Dictionary<string, System.Action<AssetBundleRef>>();
    private Dictionary<string, AssetBundleRef> loadedAssetBundles = new Dictionary<string, AssetBundleRef>();
    Dictionary<string, List<string>> dicABDependencies = new Dictionary<string, List<string>>();
    Dictionary<string, ABHashInfo> dicABHashInfo = new();
    Dictionary<string, string> dicSceneAssetBundle = new Dictionary<string, string>();
    Dictionary<string, string> dicAssetABName = new Dictionary<string, string>();

    public static AssetBundleManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = GameObject.FindAnyObjectByType<AssetBundleManager>();
                if (instance == null)
                {
                    GameObject obj = new GameObject("AssetBundleManager");
                    instance = obj.AddComponent<AssetBundleManager>();
                }
            }
            return instance;
        }
    }
    private void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
    }
    private void OnDestroy()
    {
        UnloadAllAssetBundles();
    }

    #region InnerFunctions
    AssetBundle LoadEncryptedAssetBundle(string path)
    {
        // 读取加密文件
        byte[] encryptedData = File.ReadAllBytes(path);
        var data = Helpers.DecryptAES(encryptedData, ABBuildConfig.BundleEncryptKey, ABBuildConfig.BundleEncryptIV);
        var ab = AssetBundle.LoadFromMemory(data);
        return ab;
    }
    IEnumerator CoLoadEncryptedAssetBundle(string path, System.Action<AssetBundle> cb)
    {
        // 读取加密文件
        var opRead = File.ReadAllBytesAsync(path);
        yield return opRead;
        byte[] encryptedAB = opRead.Result;

        var data = Helpers.DecryptAES(encryptedAB, ABBuildConfig.BundleEncryptKey, ABBuildConfig.BundleEncryptIV);
        var opAB = AssetBundle.LoadFromMemoryAsync(data);
        yield return opAB;
        cb?.Invoke(opAB.assetBundle);
    }
    IEnumerator CoLoadAsset<T>(AssetBundleRef abr, string assetName, System.Action<T> onGet) where T : Object
    {
        var abReq = abr.bundle.LoadAssetAsync<T>(assetName);
        yield return abReq;
        onGet?.Invoke((T)abReq.asset);
    }
    IEnumerator CoLoadAsset(AssetBundleRef abr, string assetName, System.Type type, System.Action<UnityEngine.Object> onGet)
    {
        var abReq = abr.bundle.LoadAssetAsync(assetName, type);
        yield return abReq;
        onGet?.Invoke(abReq.asset);
    }
    IEnumerator CoHandleAssetBundleManifest(bool isLocal = false)
    {
        var Catalog = isLocal ? catalogLocal : catalogApp;
        var manifest = isLocal ? manifestLocal : manifestApp;
        var handleBundles = System.Linq.Enumerable.ToList(Catalog.bundles.Values).FindAll((a) =>
        {
            return true;//(isLocal ? (a.type == ABType.Basic || a.type == ABType.BasicAndHotfix) : true);
        });
        foreach (var abInfo in handleBundles)
        {
            var abFileName = abInfo.ABFileName;
            string abName = abInfo.abName;
            dicABHashInfo[abName] = abInfo;
            
            var abPath = Path.Combine((isLocal ? localABDirectory : appABDirectory) + "/" + abFileName);
            if (!File.Exists(abPath)) { Debug.LogWarning($"Not found ab[{(isLocal ? "local":"app")}]: {abName}"); continue; }

            var depedencies = new List<string>();
            foreach (var dp in manifest.GetAllDependencies(abInfo.ABFileName))
                depedencies.Add(dp.Replace(ABBuildConfig.VARIANT_AB, ""));
            dicABDependencies[abName] = depedencies;

            // 颗粒化的AssetBundle直接标注Asset名称(不包括文件后缀名)，不使用load加载
            // 例如：var asset = ab.LoadAllAssets()[0];
            // 注意：场景AssetBundle不要使用颗粒化打包
            if (abName.Contains('/'))
            {
                dicAssetABName[abName] = abName;
                Debug.Log($"AB[{abName}] assetName[{abName}]");
                continue;
            }

            AssetBundle ab = null;
            if (abInfo.IsEncrypted)
            {
                abPath = abPath.Replace(variantAB, ABBuildConfig.VARIANT_AB_ENCRYPT);
                yield return CoLoadEncryptedAssetBundle(abPath, a => ab = a);
            }
            else
            {
                var abReq = AssetBundle.LoadFromFileAsync(abPath);
                yield return abReq;
                ab = abReq.assetBundle;
            }
            if (ab == null) { Debug.Log($"AssetBundle LoadFromFile [{abPath}] Null"); continue; }

            HandleAssetABName(ab, abName);
            // asset and scene cannot be packed in same assetbundle
            HandleSceneABName(ab, abName);
            ab.Unload(false);
        }
    }
    void HandleAssetABName(AssetBundle ab, string abName)
    {
        foreach (var assetName in ab.GetAllAssetNames())
        {
            dicAssetABName[assetName] = abName;
            Debug.Log($"AB[{abName}] assetName[{assetName}]");
        }
    }
    void HandleSceneABName(AssetBundle ab, string abName)
    {
        var scenePaths = ab.GetAllScenePaths();
        foreach (var scene in scenePaths)
        {
            dicSceneAssetBundle[scene] = abName;
            Debug.Log($"AB[{scene}] assetName[{abName}]");
        }
    }
    long GetLocalABSize(string abName)
    {
        long size = 0;
        var abfile = abName + variantAB;

        var localABFile = Path.Combine(localABDirectory, abfile);

        var tempABFile = localABFile + ABDownloadTask.TEMP_VARIANT;

        if (File.Exists(localABFile))
        {
            size += File.ReadAllBytes(localABFile).LongLength;
        }
        else if (File.Exists(tempABFile))
        {
            size += File.ReadAllBytes(tempABFile).LongLength;
        }

        return size;
    }
    #endregion

    #region Public functions
    public bool GetABNameByAssetName(ref string assetName, out string bundleName)
    {
        bool res = dicAssetABName.TryGetValue(assetName.ToLower(), out var bName);
        bundleName = bName;
        return res;
    }
    public List<AssetBundleRef> GetAllLoadedAB()
    {
        return System.Linq.Enumerable.ToList(loadedAssetBundles.Values);
    }
    public bool UnloadAssetBundle(string abName, bool unloadAsset = false)
    {
        if (loadedAssetBundles.TryGetValue(abName, out var abref))
        {
            if (abref.type == ABType.Basic) return false;
            abref.bundle.Unload(unloadAsset);
            foreach (var r in abref.dependencies)
            {
                r.touchCount--;
                if (r.touchCount == 0)
                    UnloadAssetBundle(r.bundle.name, unloadAsset); // 待测试
            }
            loadedAssetBundles.Remove(abName);
            return true;
        }
        return false;
    }
    public void UnloadAssetBundleAsync(string abName, bool unloadAsset = false, System.Action<string, bool> callback = null)
    {
        if (loadedAssetBundles.TryGetValue(abName, out var abref))
        {
            abref.bundle.UnloadAsync(unloadAsset).completed += (op) =>
            {
                foreach (var r in abref.dependencies)
                {
                    r.touchCount--;
                    if (r.touchCount == 0)
                        UnloadAssetBundleAsync(r.bundle.name, unloadAsset, callback); // 待测试
                }
                loadedAssetBundles.Remove(abName);
                callback?.Invoke(abName, true);
            };
        }
        else callback?.Invoke(abName, false);
    }
    public IEnumerator CoUnloadAssetBundle(string abName, bool unloadAsset = false, System.Action<string, bool> callback = null)
    {
        if (loadedAssetBundles.TryGetValue(abName, out var abref))
        {
            var op = abref.bundle.UnloadAsync(unloadAsset);
            yield return op;
            foreach (var r in abref.dependencies)
            {
                r.touchCount--;
                if (r.touchCount == 0)
                    yield return CoUnloadAssetBundle(r.bundle.name, unloadAsset, callback); // 待测试
            }
            loadedAssetBundles.Remove(abName);
            callback?.Invoke(abName, true);
        }
        else callback?.Invoke(abName, false);
    }
    public void UnloadAllAssetBundles()
    {
        var list = GetAllLoadedAB().FindAll((abref) => { return abref.type != ABType.Basic; });
        foreach (var bundle in list)
        {
            bundle.bundle.Unload(true);
            loadedAssetBundles.Remove(bundle.bundle.name);
        }
    }
    public void UnloadAllAssetBundlesAsync(System.Action<string> onFinished = null)
    {
        StartCoroutine(CoUnloadAllAssetBundles(onFinished));
    }
    public IEnumerator CoUnloadAllAssetBundles(System.Action<string> onFinished = null)
    {
        var list = GetAllLoadedAB().FindAll((abref) => { return abref.type != ABType.Basic; });
        uint unloadCount = 0;
        foreach (var abref in list)
        {
            var op = abref.bundle.UnloadAsync(true);
            yield return op;
            loadedAssetBundles.Remove(abref.bundle.name);
            unloadCount++;
        }
        onFinished?.Invoke($"CoUnloadAllAssetBundles unloadCount count: {unloadCount}");
    }
    public List<string> GetAllAssetNames()
    {
        return System.Linq.Enumerable.ToList(dicAssetABName.Keys);
    }
    public string GetStrVersion()
    {
        if (catalogLocal == null) { return "内测版本"; }
        return $"{catalogLocal.MainVersion}.{catalogLocal.SubVersion}.{catalogLocal.ResVersion}";
    }
    #endregion

    #region Seperate Bundle Support
    Dictionary<string, List<AssetBundleRef>> dicSeperates;
    public void InitSeperateBundleModuleAsync(string seperateBundleName, System.Action<string> onFinished = null)
    {
        if (dicSeperates == null)
            dicSeperates = new Dictionary<string, List<AssetBundleRef>>();
        StartCoroutine(CoInitSeperateBundleModule(seperateBundleName, onFinished));
    }
    public IEnumerator CoInitSeperateBundleModule(string seperateBundleName, System.Action<string> onFinished = null)
    {
        if (dicSeperates.TryGetValue(seperateBundleName, out var listAbr))
        {
            onFinished?.Invoke("init already finished");
            yield break;
        }
        seperateBundleName = seperateBundleName.ToLower();
        //dicABHashInfo.TryGetValue(separateBundleName, out var hashInfo);
        var localABPath = Path.Combine(localABDirectory, seperateBundleName);
        var appABPath = Path.Combine(localABDirectory, seperateBundleName);
        var finalABPath = "";
        if (Directory.Exists(localABPath))
        {
            finalABPath = localABPath;
        }
        else if (Directory.Exists(appABPath))
        {
            finalABPath = appABPath;
        }
        else
        {
            onFinished?.Invoke("");
            Debug.Log($"no seperate bundle name: {seperateBundleName}");
            yield break;
        }
        List<AssetBundleRef> abrList = new List<AssetBundleRef>();
        var di = new DirectoryInfo(finalABPath.Replace("/", @"\"));
        string msg = "";
        foreach (var abFile in di.GetFiles("*"+ABBuildConfig.VARIANT_AB, SearchOption.AllDirectories))
        {
            var abName = seperateBundleName + "/" + abFile.Name.Replace(abFile.Extension, "");
            yield return CoLoadAssetBundleRef(abName, (abr) =>
            {
                if (abr != null)
                {
                    abrList.Add(abr);
                    msg += abr.GetFirstAssetName() + ',';
                }
                else
                {
                    Debug.LogError($"Init Seperate Bundle FAILED: not found bundle [{abName}]");
                }
            });
        }
        if (abrList.Count > 0)
            dicSeperates.Add(seperateBundleName, abrList);
        if (msg.EndsWith(',')) msg = msg.Remove(msg.Length - 1, 1);
        onFinished?.Invoke(msg);
        yield break;
    }
    public void DisposeSeperateBundleModuleAsync(string seperateBundleName, System.Action<string> onFinished = null)
    {
        if (dicSeperates == null)
            dicSeperates = new Dictionary<string, List<AssetBundleRef>>();
        StartCoroutine(CoDisposeSeperateBundleModule(seperateBundleName, onFinished));
    }
    public IEnumerator CoDisposeSeperateBundleModule(string seperateBundleName, System.Action<string> onFinished = null)
    {
        if (dicSeperates.TryGetValue(seperateBundleName, out var abrList))
        {
            int finishCount = 0;
            int allCount = abrList.Count;
            string msg = "";
            foreach (var assetBundleRef in abrList)
            {
                yield return CoUnloadAssetBundle(assetBundleRef.bundle.name.Replace(variantAB, ""), false, (abName, suc) =>
                {
                    if (suc)
                    {
                        msg += abName + ',';
                        finishCount++;
                    }
                    else
                    {
                        msg += assetBundleRef.bundle.name + "_FAILED" + ',';
                    }
                });
            }
            if (msg.EndsWith(',')) msg = msg.Remove(msg.Length - 1, 1);
            onFinished?.Invoke(msg);
        }
        else
        {
            onFinished?.Invoke("");
            Debug.Log($"SeperateBundleName Not found: {seperateBundleName}");
        }
        yield break;
    }
    public void GetAssetFromSepBundleAsync(string bundleName, System.Action<Object> callback, System.Type type = null)
    {
        if (callback == null) return;
        bundleName = bundleName.ToLower();
        var arr = bundleName.Split('/');
        var dirName = arr[0];
        var abName = arr[^1];
        if (dicSeperates.TryGetValue(dirName, out var abrList))
        {
            var abr = abrList.Find((a) =>
            {
                return a.bundle.name == bundleName + variantAB;
            });
            if (abr == null)
            {
                callback(null);
                Debug.LogWarning($"Seperate cell bundle not found: {bundleName}");
                return;
            }
            var op = abr.bundle.LoadAssetAsync(abr.GetFirstAssetName(), type);
            op.completed += (a) =>
            {
                callback(op.asset);
            };
        }
        else callback(null);
    }
    #endregion

    #region Developing functions

    public void ClearAllCache()
    {
        ClearTempCache();
        ClearABCache();
    }
    public void ClearTempCache()
    {
        if (!Directory.Exists(tempDownloadDirectory)) return;
        Directory.Delete(tempDownloadDirectory, true);
        Directory.CreateDirectory(tempDownloadDirectory);

        if (!Directory.Exists(localABDirectory)) return;
        var files = new DirectoryInfo(localABDirectory).GetFiles("*.tmp");
        foreach (var file in files) File.Delete(file.FullName);
    }
    public void ClearABCache()
    {
        if (!Directory.Exists(localABDirectory)) return;
        Directory.Delete(localABDirectory, true);
        Directory.CreateDirectory(localABDirectory);
    }

    /// <summary>
    /// 暂不支持该功能，总是return true
    /// </summary>
    /// <param name="size">unit: byte</param>
    /// <param name="saveDir">finalDirectory</param>
    /// <returns></returns>
    public bool CheckFreeSpace(long size, string saveDir = null)
    {
        if (string.IsNullOrEmpty(saveDir)) { saveDir = localABDirectory; }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        /*
        long freeSpace = 0;
        var dis = DriveInfo.GetDrives(); // 该功能无法通过编译
        var diskName = saveDir.Split(':')[0];
        foreach (var di in dis)
            if (di.Name.Split(':')[0] == diskName) { freeSpace = di.TotalFreeSpace; break; }
        return freeSpace > size;
        */
        return true;
#elif UNITY_ANROID
        // unchecked
        return true;
#elif UNITY_IOS
        // unchecked
        return true;
#else
        // etc platforms
        return true;
#endif
    }

    /// <summary>
    /// Download AssetBundles to LocalABPath
    /// </summary>
    /// <param name="onProgress">FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed(kb/s)</param>
    /// <param name="abList">Hash AssetBundle Name List</param>
    /// <param name="onFinished">copy files finished</param>
    /// <param name="onDownloadFinished">download finished</param>
    public void DownloadABInGame(List<string> abList, ABDownloadTask.OnABDownloadProgress onProgress, System.Action<string> onFinished = null)
    {
        List<string> remoteABPaths = new List<string>();
        for (int i = 0; i < abList.Count; i++)
            remoteABPaths.Add(Path.Combine(remoteABDirectory, abList[i] + variantAB).Replace(@"\","/"));

        StartCoroutine(DownloadABListRemote(remoteABPaths, onProgress, (msg) =>
        {
            if (string.IsNullOrEmpty(msg))
                InitializeInGame(abList);
            onFinished?.Invoke(msg);
        }));

    }
    IEnumerator DownloadABListRemote(List<string> abList, ABDownloadTask.OnABDownloadProgress onProgress, System.Action<string> onFinished)
    {
        foreach (var item in abList)
        {
            Debug.Log($"DownloadABListRemote: ablist[{item}]");
        }
        ABDownloadTask task = new ABDownloadTask(abList, localABDirectory);
        task.onProgress += onProgress;
        string msg = "";
        task.onDownloadFinished += (s) => { msg = s; };
        StartCoroutine(task.Start());
        while (!task.isDownloadFinished)
            yield return null;
        onFinished?.Invoke(msg);
        yield break;
    }
    void InitializeInGame(List<string> abNames)
    {
        foreach (var abName in abNames)
        {
            var abFile = abName + variantAB;
            var abPath = Path.Combine(localABDirectory + "/" + abFile);
            var ab = AssetBundle.LoadFromFile(abPath);
            if (ab == null) { return; }

            foreach (var item in ab.GetAllAssetNames())
            {
                var dirs = item.Split('/');
                var assetFile = dirs[dirs.Length - 1];
                var parts = assetFile.Split('.');
                var assetName = parts[0];

                string variant = "";
                for (int i = 1; i < parts.Length; i++)
                    variant += parts[i];

                Debug.Log($"AB[{abName}] assetName[{assetName}]");
                if (variant == ".unity")
                {
                    dicSceneAssetBundle[assetName] = abName;
                    Debug.Log($"AB[{abName}] Scene[{assetName}]");
                }

                // Tips: asset name cannot be the same
                dicAssetABName[assetName] = abName;
            }

            var scenePaths = ab.GetAllScenePaths();
            foreach (var scene in scenePaths)
            {
                var parts = scene.Split('/');
                var fileName = parts[parts.Length - 1];
                var sceneName = fileName.Substring(0, fileName.Length - 6); // ".unity".Length
                dicSceneAssetBundle[sceneName] = abName;
            }
            ab.Unload(false);
        }

    }

    #endregion

    #region Resource Update pipeline
    public IEnumerator CoInit()
    {
        yield return CoInitCatalog(false);
        yield return CoInitCatalog(true);
    }
    public IEnumerator CoInitCatalog(bool isLocal)
    {
        var verFilePath = isLocal ? ABBuildConfig.LocalVerFilePath : ABBuildConfig.AppVerFilePath;
        var hashFilePath = isLocal ? ABBuildConfig.LocalHashFilePath : ABBuildConfig.AppHashFilePath;
        string ver, hash;
        if (File.Exists(verFilePath) && File.Exists(hashFilePath))
        {
            var t1 = File.ReadAllTextAsync(verFilePath, System.Text.Encoding.UTF8);
            var t2 = File.ReadAllTextAsync(hashFilePath, System.Text.Encoding.UTF8);
            yield return t1;
            yield return t2;
            ver = t1.Result;
            hash = t2.Result;
        }
        else ver = hash = null;
        if (string.IsNullOrEmpty(ver) || string.IsNullOrEmpty(hash))
        {
            Debug.LogError($"Cannot find Version or Hash file[{(isLocal?"local":"app")}]!!");
            yield break;
        }
        var Catalog = AssetBundleCatalog.Parse(ver, hash);
        if (Catalog == null || Catalog.MainVersion == 0)
        {
            Debug.LogError($"InitABRefrence FAILED: mainVersion NOT valid]");
            yield break;
        }

        string manifestPath = Path.Combine((isLocal ? localABDirectory : appABDirectory), ABDirectoryName);
        if (!File.Exists(manifestPath)) { Debug.Log($"InitAppAssetBundlesAsync FAILED!! manifest not found"); yield break; }
        var reqMainAB = AssetBundle.LoadFromFileAsync(manifestPath);
        yield return reqMainAB;
        var mainAB = reqMainAB.assetBundle;

        var reqManifest = mainAB.LoadAssetAsync<AssetBundleManifest>("AssetBundleManifest");
        yield return reqManifest;
        var manifest = reqManifest.asset as AssetBundleManifest;
        if (manifest == null) { Debug.Log($"InitAppAssetBundlesAsync FAILED!! manifest not found"); yield break; }

        if (isLocal)
        {
            manifestLocal = manifest;
            catalogLocal = Catalog;
        }
        else
        {
            manifestApp = manifest;
            catalogApp = Catalog;
        }
        mainAB.Unload(false);

        yield return CoHandleAssetBundleManifest(isLocal);
    }
    public void CheckUpdateAsync(System.Action<CheckVersionResult, string> onFinished)
    {
        if (isCustomCatalog) return;

        //StartCoroutine(CheckVersion(onFinished));
        StartCoroutine(CoCheckUpdate(onFinished));
    }
    IEnumerator CoCheckUpdate(System.Action<CheckVersionResult, string> onFinished = null)
    {
        int localMainVer = catalogLocal.MainVersion, localSubVer = catalogLocal.SubVersion, localResVer = catalogLocal.ResVersion,
            remoteMainVer = 0, remoteSubVer = 0, remoteResVer = 0;

        var remoteVersionPath = Path.Combine(remoteABDirectory, verFileName).Replace(@"\", "/");
        var remoteHashFilePath = Path.Combine(remoteABDirectory, hashFileName).Replace(@"\", "/");
        var reqVer = UnityWebRequest.Get(remoteVersionPath);
        yield return reqVer.SendWebRequest();
        if (!string.IsNullOrEmpty(reqVer.error)) { onFinished?.Invoke(CheckVersionResult.error, reqVer.error); yield break; }

        var strRemoteVer = reqVer.downloadHandler.text;
        var remoteVers = strRemoteVer.Split(ABBuildConfig.VERSION_FILE_SPLITER);
        remoteMainVer = int.Parse(remoteVers[0]);
        remoteSubVer = int.Parse(remoteVers[1]);
        remoteResVer = int.Parse(remoteVers[2]);

        CheckVersionResult result;
        if (localMainVer < remoteMainVer)
        {
            result = CheckVersionResult.updateApp;
            onFinished?.Invoke(result, "");
            yield break;
        }
        else if (localSubVer >= remoteSubVer && localResVer >= remoteResVer)
        {
            result = CheckVersionResult.success;
            onFinished?.Invoke(result, "");
            yield break;
        }

        UnityWebRequest reqHash = UnityWebRequest.Get(remoteHashFilePath);
        yield return reqHash.SendWebRequest();
        if (!string.IsNullOrEmpty(reqVer.error)) { onFinished?.Invoke(CheckVersionResult.error, reqVer.error); yield break; }
        var strRemoteHash = reqHash.downloadHandler.text;
        var remoteCatalog = AssetBundleCatalog.Parse(strRemoteVer, strRemoteHash);

        List<ABHashInfo> updateABList = new List<ABHashInfo>();
        bool needRestart = false;
        foreach (var item in remoteCatalog.bundles)
        {
            var abName = item.Key;
            var remoteInfo = item.Value;
            if (remoteInfo == null) { onFinished?.Invoke(CheckVersionResult.error, "Parse Remote ABHashInfo error"); yield break; };
            if (remoteInfo.type != ABType.Hotfix || remoteInfo.type != ABType.HotfixRestart) continue;

            var localABFile = Path.Combine(localABDirectory, remoteInfo.abName) + variantAB;
            var tempABFile = localABFile + ABDownloadTask.TEMP_VARIANT;
            bool hasTemp = File.Exists(tempABFile), hasLocal = File.Exists(localABFile);
            if (!hasLocal)
            {
                ABHashInfo hash = new ABHashInfo();
                if (!hasTemp)
                {
                    hash = remoteInfo;
                }
                else
                {
                    // 计算实际所需下载大小
                    var size = hasTemp ? remoteInfo.size - File.ReadAllBytes(tempABFile).LongLength : remoteInfo.size;
                    // 跳过已经下载完成的
                    if (size < 1) continue;
                    hash.size = size;
                    hash.hash = remoteInfo.hash;
                    hash.abName = remoteInfo.abName;
                    hash.type = remoteInfo.type;
                    // tips: 未下载完的tmp文件如果被篡改可能导致无法继续下载, 可以通过检测文件完整性函数[CheckComplete]解决
                }
                updateABList.Add(hash);
                needRestart = needRestart ? needRestart : remoteInfo.type == ABType.HotfixRestart;
            }
            else
            {
                catalogLocal.bundles.TryGetValue(remoteInfo.abName, out var localInfo);
                // 若本地缺少该项则直接重新下载，重新校验hash值可能消耗较多时间
                if (localInfo == null) { updateABList.Add(remoteInfo); continue; }
                // 检测是否需要更新
                if (localInfo.hash == remoteInfo.hash) { continue; }
                ABHashInfo hash;
                if (!hasTemp)
                {
                    hash = remoteInfo;
                }
                else
                {
                    var size = hasTemp ? remoteInfo.size - File.ReadAllBytes(tempABFile).LongLength : remoteInfo.size;
                    if (size < 1) continue;
                    hash = new ABHashInfo();
                    hash.size = size;
                    hash.hash = remoteInfo.hash;
                    hash.abName = remoteInfo.abName;
                    hash.type = remoteInfo.type;
                    hash.Encrypt = remoteInfo.Encrypt;
                }
                updateABList.Add(hash);
                needRestart = needRestart ? needRestart : remoteInfo.type == ABType.HotfixRestart;
            }
        }

        ulong totalSize = 0;
        string strUpdateList = MARK_SUCCESS + hashSpliter;
        foreach (var item in updateABList)
        {
            strUpdateList += ABHashInfo.ToString(item) + hashSpliter;
            totalSize += (ulong)item.size;
        }
        if (totalSize == 0) { onFinished?.Invoke(CheckVersionResult.updateCatalog, strUpdateList); yield break; }

        strUpdateList += totalSize;

        onFinished?.Invoke(needRestart ? CheckVersionResult.updateRestart : CheckVersionResult.updateEnter, strUpdateList);
    }
    /// <summary>
    /// Check remote AssetBundle size
    /// </summary>
    /// <param name="abList">ab name list</param>
    /// <param name="onGetSize">callback</param>
    public void CheckRemoteABSizeAsync(List<string> abList, System.Action<long> onGetSize)
    {
        List<string> remoteABPaths = new List<string>();
        long existedSize = 0;
        for (int i = 0; i < abList.Count; i++)
        {
            var abfile = abList[i] + variantAB;
            existedSize += GetLocalABSize(abList[i]);
            remoteABPaths.Add(Path.Combine(remoteABDirectory, abfile));
        }
        if (onGetSize == null) { onGetSize?.Invoke(0); return; }
        StartCoroutine(CoCheckRemoteABSize(remoteABPaths, onGetSize, existedSize));
    }
    IEnumerator CoCheckRemoteABSize(List<string> abList, System.Action<long> onGetSize, long existedSize = 0)
    {
        long totalSize = 0;
        List<string> remoteABPaths = new List<string>();
        for (int i = 0; i < abList.Count; i++)
        {
            var remoteABPath = abList[i];
            UnityWebRequest req = UnityWebRequest.Head(remoteABPath);
            yield return req.SendWebRequest();
            if (!string.IsNullOrEmpty(req.error))
            {
                onGetSize?.Invoke(0);
                yield break;
            }
            var contentLength = req.GetResponseHeader("Content-Length");
            long.TryParse(contentLength, out var size);
            totalSize += size;
        }
        onGetSize?.Invoke(System.Math.Max(0, totalSize - existedSize));
    }
    public void UpdateGameAsync(string abInfoList, System.Action<string> onFinished = null, ABDownloadTask.OnABDownloadProgress onProgress = null)
    {
        StartCoroutine(CoUpdateGame(abInfoList, onFinished, onProgress));
    }
    IEnumerator CoUpdateGame(string abInfoList, System.Action<string> onFinished = null, ABDownloadTask.OnABDownloadProgress onProgress = null)
    {
        List<string> urls = new List<string>();
        var arr = abInfoList.Split(hashSpliter);
        foreach (var item in arr)
        {
            var info = ABHashInfo.Parse(item);
            if (info != null)
            {
                var url = Path.Combine(remoteABDirectory, info.abName).Replace(@"\", "/") + variantAB;
                urls.Add(url);
            }
        }

        if (!Directory.Exists(localABDirectory))
            Directory.CreateDirectory(localABDirectory);
        if (urls.Count > 0)
        {
            string downloadError = "";
            var dt = new ABDownloadTask(urls, localABDirectory);
            dt.onDownloadFinished += (str) => { downloadError = str; };
            dt.onProgress += onProgress;
            yield return dt.Start();
            if (!string.IsNullOrEmpty(downloadError)) { onFinished?.Invoke("DownloadError:" + downloadError); yield break; }
            Debug.Log($"Update AssetBundles finished");
        }

        var localVersionFilePath = Path.Combine(localABDirectory, verFileName).Replace(@"\", "/");
        var remoteVersionPath = Path.Combine(remoteABDirectory, verFileName).Replace(@"\", "/");
        var reqVer = UnityWebRequest.Get(remoteVersionPath);
        yield return reqVer.SendWebRequest();
        if (!string.IsNullOrEmpty(reqVer.error)) { onFinished?.Invoke("version file not found"); yield break; }

        var localHashFilePath = Path.Combine(localABDirectory, hashFileName).Replace(@"\", "/");
        var remoteHashFilePath = Path.Combine(remoteABDirectory, hashFileName).Replace(@"\", "/");
        var reqHash = UnityWebRequest.Get(remoteHashFilePath);
        yield return reqHash.SendWebRequest();
        if (!string.IsNullOrEmpty(reqHash.error)) { onFinished?.Invoke("hash file not found"); yield break; }
        string strHash = reqHash.downloadHandler.text;

        using (var fsHash = new FileStream(localHashFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            fsHash.Write(reqHash.downloadHandler.data, 0, reqHash.downloadHandler.text.Length);
        }
        using (var fsVer = new FileStream(localVersionFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            fsVer.Write(reqVer.downloadHandler.data, 0, reqVer.downloadHandler.text.Length);
        }
        Debug.Log($"Update Version & Hash finished");
        catalogLocal = AssetBundleCatalog.Parse(reqVer.downloadHandler.text, reqHash.downloadHandler.text);
        reqVer.Dispose();
        reqHash.Dispose();

        var remoteABMainPath = Path.Combine(remoteABDirectory, ABDirectoryName).Replace(@"\", "/");
        var localABMainPath = Path.Combine(localABDirectory, ABDirectoryName).Replace(@"\", "/");
        var reqMain = UnityWebRequestAssetBundle.GetAssetBundle(remoteABMainPath);
        yield return reqMain.SendWebRequest();
        if (!string.IsNullOrEmpty(reqMain.error)) { onFinished?.Invoke("Get MainAssetBundle Error:" + reqMain.error); yield break; }
        var mainABData = reqMain.downloadHandler.data;
        var mainAB = AssetBundle.LoadFromMemory(mainABData);
        if (mainAB == null) { onFinished?.Invoke("Get MainAssetBundle NULL"); yield break; }
        var manifest = mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        if (manifest == null) { onFinished?.Invoke("Get manifest NULL"); yield break; }
        using (var fsAB = new FileStream(localABMainPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            yield return fsAB.WriteAsync(mainABData, 0, mainABData.Length);
        }
        mainAB.Unload(true);
        reqHash.Dispose();
        Debug.Log($"Update MainAssetBundle finished");

        //var handler = new DownloadHandlerFile(localABMainPath);
        //reqMain.downloadHandler = handler;
        //reqMain.disposeDownloadHandlerOnDispose = true;
        //yield return reqMain.SendWebRequest();

        onFinished?.Invoke(MARK_SUCCESS);
    }
    public void CheckCompleteAsync(System.Action<string> onFinished)
    {
        StartCoroutine(CoCheckComplete(onFinished));
    }
    IEnumerator CoCheckComplete(System.Action<string> onFinished)
    {
        var remoteHashPath = Path.Combine(remoteABDirectory, hashFileName).Replace(@"\", "/");
        UnityWebRequest reqHash = UnityWebRequest.Get(remoteHashPath);
        yield return reqHash.SendWebRequest();
        if (!string.IsNullOrEmpty(reqHash.error)) { onFinished?.Invoke(reqHash.error); yield break; }
        if (!Directory.Exists(localABDirectory))
            Directory.CreateDirectory(localABDirectory);

        var list = reqHash.downloadHandler.text.Split(hashSpliter);
        string broken = "";
        foreach (var item in list)
        {
            if (string.IsNullOrEmpty(item)) continue;
            ABHashInfo info = ABHashInfo.Parse(item);
            var localABPath = Path.Combine(localABDirectory, info.ABFileName + variantAB).Replace(@"\", "/");
            if (!File.Exists(localABPath))
            {
                if (info != null && info.type == ABType.Extra) continue;
                broken += item + hashSpliter;
                Debug.Log($"AB[{info.ABFileName}] no local file, remoteHash = {info.hash}");
            }
            else
            {
                var hash = Helpers.ParseToMD5(localABPath);
                if (info == null) continue;
                if (hash != info.hash) broken += item + hashSpliter;
                Debug.Log($"AB[{info.ABFileName}] localHash = {hash}, remoteHash = {info.hash}");
            }
        }

        if (broken.Length > 0)
        {
            onFinished?.Invoke(MARK_SUCCESS + hashSpliter + broken.Substring(0, broken.Length - 1));
        }
        else
        {
            onFinished?.Invoke(MARK_SUCCESS);
        }
    }
    public IEnumerator CoInitializeOnStart(System.Action<string> onFinished = null)
    {
        var mainABPath = Path.Combine(localABDirectory, ABDirectoryName);
        if (!File.Exists(mainABPath))
        {
            Debug.Log($"InitABRefrence FAILED: MainAB NOT found at path[{mainABPath}]");
            onFinished?.Invoke($"InitABRefrence FAILED: MainAB NOT found at path[{mainABPath}]");
            yield break;
        }
        else
        {
            mainABPath = Path.Combine(appABDirectory, ABDirectoryName);
            if (!File.Exists(mainABPath))
            {
                Debug.Log($"InitABRefrence FAILED: MainAB NOT found at path[{mainABPath}]");
                onFinished?.Invoke($"InitABRefrence FAILED: MainAB NOT found at path[{mainABPath}]");
                yield break;
            }
        }

        var mReq = AssetBundle.LoadFromFileAsync(mainABPath);
        yield return mReq;
        var mainABLocal = mReq.assetBundle;
        if (mainABLocal == null) { 
            Debug.Log("InitABRefrence FAILED: MainAB Null");
            onFinished?.Invoke("InitABRefrence FAILED: MainAB Null");
            yield break; 
        }

        var mReq2 = mainABLocal.LoadAssetAsync<AssetBundleManifest>("AssetBundleManifest");
        yield return mReq2;
        manifestLocal = mReq2.asset as AssetBundleManifest;
        if (manifestLocal == null) { 
            Debug.Log("InitABRefrence FAILED: AssetBundleManifest Null");
            onFinished?.Invoke("InitABRefrence FAILED: AssetBundleManifest Null");
            yield break;
        }
        mainABLocal.Unload(false);

        yield return CoHandleAssetBundleManifest(true);

        Debug.Log("CoInitializeOnStart success");
        onFinished?.Invoke("CoInitializeOnStart success");
    }
    #endregion

    #region Direct
    public T GetAsset<T>(string assetName) where T : Object
    {
        T res = null;

        if (!GetABNameByAssetName(ref assetName, out var bundleName)) return res;

        var abref = LoadAssetBundle(bundleName);
        if (abref == null)
            return res;
        if (abref.bundle == null)
            return res;
        res = abref.bundle.LoadAsset<T>(assetName);

        return res;
    }
    public UnityEngine.Object GetAsset(string assetName, System.Type type)
    {
        UnityEngine.Object res = null;
        if (!GetABNameByAssetName(ref assetName, out var bundleName)) return res;
        var abref = LoadAssetBundle(bundleName);
        if (abref == null)
            return res;
        if (abref.bundle == null)
            return res;
        res = abref.bundle.LoadAsset(assetName, type);

        return res;
    }
    public AssetBundleRef LoadAssetBundle(string bundleName)
    {
        if (loadedAssetBundles.TryGetValue(bundleName, out var abr)) return abr;

        dicABDependencies.TryGetValue(bundleName, out var dependencies);
        List<AssetBundleRef> dependenciesList = new List<AssetBundleRef>();
        if (dependencies != null)
        {
            foreach (var dependency in dependencies)
            {
                dependenciesList.Add(LoadAssetBundle(dependency));
            }
        }
        dicABHashInfo.TryGetValue(bundleName, out var hashInfo);
        string fileName = hashInfo.ABFileName;
        string path = Path.Combine(hashInfo.type == ABType.Basic ? appABDirectory : localABDirectory, fileName);
        if (!File.Exists(path))
        {
            if (hashInfo.type == ABType.BasicAndHotfix)
            {
                path = Path.Combine(appABDirectory, fileName);
            }
            else
            {
                Debug.LogError($"AssetBundle NOT found!! Path: [{path}]");
                return null;
            }
        }
        AssetBundle ab;
        if (hashInfo.IsEncrypted) ab = LoadEncryptedAssetBundle(path);
        else ab = AssetBundle.LoadFromFile(path);
        if (ab == null)
        {
            Debug.Log("LoadAssetBundle FAILED at path: " + path);
            return null;
        }
        var loaded = new AssetBundleRef(ab)
        {
            dependencies = dependenciesList,
            type = hashInfo.type,
        };
        loadedAssetBundles[bundleName] = loaded;
        return loaded;
    }
    #endregion
    #region AsyncLoad
    public void LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, System.Action<string> onLoaded = null, System.Action<float> progress = null)
    {
        dicSceneAssetBundle.TryGetValue(sceneName, out var abName);
        if (string.IsNullOrEmpty(abName)) { progress?.Invoke(1); onLoaded?.Invoke("fail"); return; }
        LoadAssetBundleAsync(abName, (abr) =>
        {
            if (abr.IsSingleAsset) { sceneName = abr.GetFirstAssetName(); }
            StartCoroutine(CoLoadScene(sceneName, mode, onLoaded, progress));
        });
    }
    public void LoadSceneAsync(string sceneName, int mode = 0, System.Action<string> onLoaded = null, System.Action<float> progress = null)
    {
        dicSceneAssetBundle.TryGetValue(sceneName, out var abName);
        if (string.IsNullOrEmpty(abName)) { progress?.Invoke(1); onLoaded?.Invoke("fail"); return; }
        LoadAssetBundleAsync(abName, (abr) =>
        {
            if (abr.IsSingleAsset) { sceneName = abr.GetFirstAssetName(); }
            StartCoroutine(CoLoadScene(sceneName, (LoadSceneMode)mode, onLoaded, progress));
        });
    }
    public void GetAssetAsync<T>(string assetName, System.Action<T> onGet) where T : Object
    {
        if (!GetABNameByAssetName(ref assetName, out string bundleName)) { onGet?.Invoke(null); return; }
        LoadAssetBundleAsync(bundleName, (abr) =>
        {
            if (abr == null) { onGet?.Invoke(null); return; }
            if (abr.IsSingleAsset) { assetName = abr.GetFirstAssetName(); }
            StartCoroutine(CoLoadAsset(abr, assetName, onGet));
        });
    }
    public void GetAssetAsync(string assetName, System.Type assetType, System.Action<UnityEngine.Object> onGet)
    {
        if (!GetABNameByAssetName(ref assetName, out string bundleName)) { onGet?.Invoke(null); return; }
        LoadAssetBundleAsync(bundleName, (abr) =>
        {
            if (abr == null) { onGet?.Invoke(null); return; }
            if (abr.IsSingleAsset) { assetName = abr.GetFirstAssetName(); }
            StartCoroutine(CoLoadAsset(abr, assetName, assetType, onGet));
        });
    }
    public void LoadAssetBundleAsync(string bundleName, System.Action<AssetBundleRef> callback)
    {
        StartCoroutine(CoLoadAssetBundleRef(bundleName, callback));
    }
    #endregion
    #region CoLoad
    public IEnumerator CoLoadAsset(string assetName, System.Action<UnityEngine.Object> callback)
    {
        UnityEngine.Object res = null;

        if (!GetABNameByAssetName(ref assetName, out string bundleName)) { callback?.Invoke(res); yield break; }

        AssetBundleRef abref = null;
        yield return CoLoadAssetBundleRef(bundleName, (abr) => {
            abref = abr;
        });
        if (abref == null || abref.bundle == null) { callback?.Invoke(res); yield break; }
        if (abref.IsSingleAsset) { assetName = abref.GetFirstAssetName(); }
        var op = abref.bundle.LoadAssetAsync(assetName);
        yield return op;
        res = op.asset;
        callback?.Invoke(res);
    }
    public IEnumerator CoLoadAssetBundleRef(string bundleName, System.Action<AssetBundleRef> callback)
    {
        loadedAssetBundles.TryGetValue(bundleName, out var abr);
        if (abr != null)
        {
            callback?.Invoke(abr);
            yield break;
        }
        dicABDependencies.TryGetValue(bundleName, out var dependencies);
        int dependencyCount = 0;
        List<AssetBundleRef> dependenciesList = new List<AssetBundleRef>();
        if (dependencies != null)
        {
            dependencyCount = dependencies.Count;
            foreach (var dependency in dependencies)
            {
                yield return CoLoadAssetBundleRef(dependency, (ab) => { dependenciesList.Add(ab); });
            }
        }

        while (dependenciesList.Count < dependencyCount)
            yield return null;

        if (loadingAssetBundles.ContainsKey(bundleName))
        {
            loadingAssetBundles[bundleName] += callback;
            yield break;
        }

        dicABHashInfo.TryGetValue(bundleName, out var hashInfo);
        if (hashInfo == null) { callback?.Invoke(null); yield break; }
        string path = Path.Combine(hashInfo.type == ABType.Basic ? appABDirectory : localABDirectory, hashInfo.ABFileName);
        if (!File.Exists(path))
        {
            if (hashInfo.type == ABType.BasicAndHotfix)
            {
                path = Path.Combine(appABDirectory, bundleName) + variantAB;
            }
            else
            {
                Debug.LogError($"AssetBundle NOT found!! Path: [{path}]");
                callback?.Invoke(null);
                yield break;
            }
        }
        AssetBundle ab = null;
        loadingAssetBundles.Add(bundleName, null);
        if (hashInfo.IsEncrypted)
        {
            path = path.Replace(variantAB, ABBuildConfig.VARIANT_AB_ENCRYPT);
            yield return CoLoadEncryptedAssetBundle(path, a => ab = a);
        }
        else
        {
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path);
            yield return request;
            ab = request.assetBundle;
        }
        if (ab != null)
        {
            var loaded = new AssetBundleRef(ab)
            {
                dependencies = dependenciesList
            };
            loadedAssetBundles[bundleName] = loaded;
            callback?.Invoke(loaded);
            loadingAssetBundles[bundleName]?.Invoke(loaded);
        }
        else
        {
            callback?.Invoke(null);
            loadingAssetBundles[bundleName]?.Invoke(null);
            Debug.LogError("Failed to load AssetBundle at path: " + path);
        }
        loadingAssetBundles.Remove(bundleName);
    }
    IEnumerator CoLoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, System.Action<string> onLoaded = null, System.Action<float> progress = null)
    {
        int disableProgress = 0;
        var op = SceneManager.LoadSceneAsync(sceneName, mode);
        op.allowSceneActivation = false;
        int toProgress;
        //op.progress 只能获取到90%，最后10%获取不到，需要自己处理
        while (op.progress < 0.9f)
        {
            ////获取真实的加载进度
            //toProgress = (int)(op.progress * 100);
            //while (disableProgress < toProgress)
            //{
            //    ++disableProgress;
            //    progress?.Invoke(disableProgress / 100.0f);//0.01开始
            //    yield return new WaitForEndOfFrame();
            //}
            progress?.Invoke(op.progress / 100.0f);
            yield return null;
        }
        //因为op.progress 只能获取到90%，所以后面的值不是实际的场景加载值了
        toProgress = 100;
        while (disableProgress < toProgress)
        {
            ++disableProgress;
            progress?.Invoke(disableProgress / 100.0f);
            yield return new WaitForEndOfFrame();
        }
        op.allowSceneActivation = true;
        onLoaded?.Invoke("");
    }
    #endregion

}

[System.Serializable]
public class AssetBundleCatalog
{
    public Dictionary<string, ABHashInfo> bundles = new Dictionary<string, ABHashInfo>();
    public int MainVersion { get; private set; } = 0;
    public int SubVersion { get; private set; } = 0;
    public int ResVersion { get; private set; } = 0;
    public static AssetBundleCatalog Parse(string ver, string hash)
    {
        AssetBundleCatalog res = new AssetBundleCatalog();
        try
        {
            var versionInfoLine = ver;
            var parts1 = versionInfoLine.Split(ABBuildConfig.VERSION_FILE_SPLITER);
            res.MainVersion = int.Parse(parts1[0]);
            res.SubVersion = int.Parse(parts1[1]);
            res.ResVersion = int.Parse(parts1[2]);
            var lines = hash.Split(ABBuildConfig.HASH_FILE_SPLITER);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                ABHashInfo info = ABHashInfo.Parse(line);
                if (info == null) { Debug.Log($"Parse hash line[{i}] FAILED"); continue; }
                res.bundles[info.abName] = info;
            }
        }
        catch
        {
            return null;
        }

        return res;
    }
}

