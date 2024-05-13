using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public enum BundleLoadType
{
    OnStart,
    InGame_Static,
    InGame_Dynamic,
}

public class AssetBundleManager : MonoBehaviour
{
    public enum CheckVersionResult
    {
        success,

        //updateMain, // 进入游戏后检测

        update,

        error,
    }


    public const char catalogFileVersionSpliter = '.';
    public const char catalogFileABInfoSpliter = '=';
    public bool isCustomCatalog = false;
    public const string MARK_SUCCESS = "success";

    public const string VERSION_FILE_NAME = "ver.txt";
    public const char VERSION_FILE_SPLITER = '.';
    public const string HASH_FILE_NAME = "hash.txt";
    public const char HASH_FILE_SPLITER = '\n';
    public const string VARIANT_AB = ".asset";

    public string remoteAddress { get { return "localhost"; } }
    public int remotePort { get { return 80; } }
    public string remoteABDirectory { get { return Path.Combine($"{remoteAddress}:{remotePort}/{ABDirectoryName}"); } }
    public string localABDirectory { get { return Path.Combine(Application.persistentDataPath + $"/{ABDirectoryName}"); } }
    public string tempDownloadDirectory { get { return Path.Combine(Application.persistentDataPath + $"/temp"); } }
    
    public string localVerFilePath { get{ return Path.Combine(localABDirectory, VERSION_FILE_NAME).Replace(@"\", "/"); } }
    public string localHashFilePath { get{ return Path.Combine(localABDirectory, HASH_FILE_NAME).Replace(@"\", "/"); } }
    
    public string ABDirectoryName
    {
        get
        {
#if UNITY_STANDALONE_WIN
            return "StandaloneWindows64";
#elif UNITY_ANDROID
            return "Android";
#elif UNITY_IOS
            return "iOS";
#else
            Debug.Log($"UNKNOWN PLATFORM, AssetbundleManager Initialize FAILED!");
            return "";
#endif
        }
    }

    AssetBundle main;
    AssetBundleManifest manifest;
    AssetBundleCatalog Catalog;

    private static AssetBundleManager instance;

    Dictionary<string, System.Action<AssetBundleRef>> loadingAssetBundles = new Dictionary<string, System.Action<AssetBundleRef>>();
    private Dictionary<string, AssetBundleRef> loadedAssetBundles = new Dictionary<string, AssetBundleRef>();
    bool isInitABRef;
    Dictionary<string, List<string>> dicABDependencies = new Dictionary<string, List<string>>();
    Dictionary<string, string> dicSceneAssetBundle = new Dictionary<string, string>();
    Dictionary<string, string> dicAssetABName = new Dictionary<string, string>();
    Dictionary<string, BundleLoadType> dicABLoadType = new Dictionary<string, BundleLoadType>();

    public static AssetBundleManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<AssetBundleManager>();
                if (instance == null)
                {
                    GameObject obj = new GameObject("AssetBundleManager");
                    instance = obj.AddComponent<AssetBundleManager>();
                    DontDestroyOnLoad(instance.gameObject);
                }
            }
            return instance;
        }
    }

    private void OnDestroy()
    {
        UnloadAllAssetBundles();
    }

    #region HandleABFiles

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

    public void CheckUpdate(System.Action<CheckVersionResult, string> onFinished)
    {
        if (isCustomCatalog) return;

        //StartCoroutine(CheckVersion(onFinished));
        StartCoroutine(CheckVersion(onFinished));
    }

    IEnumerator CheckVersion(System.Action<CheckVersionResult, string> onFinished = null)
    {
        var localVersionFilePath = localVerFilePath;
        var remoteVersionPath = Path.Combine(remoteABDirectory, VERSION_FILE_NAME).Replace(@"\", "/");

        var localHashPath = localHashFilePath;
        var remoteHashFilePath = Path.Combine(remoteABDirectory, HASH_FILE_NAME).Replace(@"\", "/");

        int localMainVer = 0, localSubVer = 0, localResVer = 0,
            remoteMainVer = 0, remoteSubVer = 0, remoteResVer = 0;
        bool canEnterGame = File.Exists(localVersionFilePath);
        if (canEnterGame)
        {
            var line1 = File.ReadAllLines(localVersionFilePath)[0];
            var vers = line1.Split('.');
            localMainVer = int.Parse(vers[0]);
            localSubVer = int.Parse(vers[1]);
            localResVer = int.Parse(vers[2]);
        }

        var reqVer = UnityWebRequest.Get(remoteVersionPath);
        yield return reqVer.SendWebRequest();
        if (!string.IsNullOrEmpty(reqVer.error)) { onFinished?.Invoke(CheckVersionResult.error, reqVer.error); yield break; }

        var remoteVers = reqVer.downloadHandler.text.Split('.');
        remoteMainVer = int.Parse(remoteVers[0]);
        remoteSubVer = int.Parse(remoteVers[1]);
        remoteResVer = int.Parse(remoteVers[2]);

        if (localMainVer < remoteMainVer || localSubVer < remoteSubVer || localResVer < remoteResVer)
        {
            canEnterGame = false;
        }

        if (canEnterGame)
        {
            onFinished?.Invoke(CheckVersionResult.success, "");
            yield break;
        }

        //var newApkName = $"gamename_{remoteMainVer}.{remoteSubVer}.{remoteResVer}.apk";
        //var remoteNewApkPath = Path.Combine($"{remoteAddress}:{remotePort}/Apk/{newApkName}");
        //var reqNewApp = UnityWebRequest.Head(remoteNewApkPath);
        //yield return reqNewApp.SendWebRequest();
        //if (string.IsNullOrEmpty(reqNewApp.error)) { onFinished?.Invoke(CheckVersionResult.updateMain,""); yield break; }

        // CompareHashes
        UnityWebRequest reqHash = UnityWebRequest.Get(remoteHashFilePath);
        yield return reqHash.SendWebRequest();
        if (!string.IsNullOrEmpty(reqVer.error)) { onFinished?.Invoke(CheckVersionResult.error, reqVer.error); yield break; }

        var strHashRemote = reqHash.downloadHandler.text;
        var lines = strHashRemote.Split(HASH_FILE_SPLITER);

        var updateABList = new List<ABHashInfo>();

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;

            ABHashInfo remoteInfo = ABHashInfo.Parse(line);
            if (remoteInfo == null) { onFinished?.Invoke(CheckVersionResult.error, "Parse Remote ABHashInfo error"); yield break; };

            if (remoteInfo.type == BundleLoadType.InGame_Static) continue;

            var localABFile = Path.Combine(localABDirectory, remoteInfo.abPath).Replace(@"\", "/") + VARIANT_AB;
            var tempABFile = localABFile + ABDownloadTask.TEMP_VARIANT;
            bool hasTemp = File.Exists(tempABFile), hasLocal = File.Exists(localABFile);

            if (!hasLocal)
            {
                if (remoteInfo.type == BundleLoadType.InGame_Dynamic) continue;
                if (!hasTemp)
                {
                    updateABList.Add(remoteInfo);
                }
                else
                {
                    // 计算实际所需下载大小
                    var size = hasTemp ? remoteInfo.size - File.ReadAllBytes(tempABFile).LongLength : remoteInfo.size;
                    // 跳过已经下载完成的
                    if (size < 1) continue;
                    ABHashInfo hash = new ABHashInfo();
                    hash.size = size;
                    hash.hash = remoteInfo.hash;
                    hash.abPath = remoteInfo.abPath;
                    hash.type = remoteInfo.type;
                    updateABList.Add(hash);
                    // tips: 未下载完的tmp文件如果被篡改可能导致无法继续下载, 可以通过检测文件完整性函数[CheckComplete]解决
                }
            }
            else
            {
                var strHashLocal = "";
                if (File.Exists(localHashPath))
                    strHashLocal = File.ReadAllText(localHashPath);
                else
                    continue;
                Dictionary<string, ABHashInfo> dicLocalHash = new Dictionary<string, ABHashInfo>();
                if (!string.IsNullOrEmpty(strHashLocal))
                {
                    var localLines = strHashLocal.Split(HASH_FILE_SPLITER);
                    foreach (var localLine in localLines)
                    {
                        if (string.IsNullOrEmpty(localLine)) continue;

                        ABHashInfo info = ABHashInfo.Parse(localLine);
                        if (info == null) { onFinished?.Invoke(CheckVersionResult.error, "Parse Local ABHashInfo error"); yield break; };
                        dicLocalHash[info.abPath] = info;
                    }
                }
                dicLocalHash.TryGetValue(remoteInfo.abPath, out var localInfo);
                // 若本地缺少该项则直接重新下载，重新校验hash值可能消耗较多时间
                if (localInfo == null) { updateABList.Add(remoteInfo); continue; }
                // 检测是否需要更新
                if (localInfo.hash == remoteInfo.hash) { continue; }
                if (!hasTemp)
                {
                    updateABList.Add(remoteInfo);
                }
                else
                {
                    var size = hasTemp ? remoteInfo.size - File.ReadAllBytes(tempABFile).LongLength : remoteInfo.size;
                    if (size < 1) continue;
                    ABHashInfo hash = new ABHashInfo();
                    hash.size = size;
                    hash.hash = remoteInfo.hash;
                    hash.abPath = remoteInfo.abPath;
                    updateABList.Add(hash);
                }
            }
        }

        ulong totalSize = 0;
        string strUpdateList = MARK_SUCCESS+ HASH_FILE_SPLITER;
        foreach (var item in updateABList)
        {
            strUpdateList += ABHashInfo.ToString(item) + HASH_FILE_SPLITER;
            totalSize += (ulong)item.size;
        }
        strUpdateList += totalSize;
        onFinished?.Invoke(CheckVersionResult.update, strUpdateList);
    }

    public void StartGameDownload(string abInfoList, System.Action<string> onFinished = null, ABDownloadTask.OnABDownloadProgress onProgress = null)
    {
        StartCoroutine(UpdateVersion(abInfoList, onFinished, onProgress));
    }
    IEnumerator UpdateVersion(string abInfoList, System.Action<string> onFinished = null, ABDownloadTask.OnABDownloadProgress onProgress = null)
    {
        List<string> urls = new List<string>();
        var arr = abInfoList.Split(HASH_FILE_SPLITER);
        foreach (var item in arr)
        {
            var info = ABHashInfo.Parse(item);
            if (info != null)
            {
                var url = Path.Combine(remoteABDirectory, info.abPath).Replace(@"\", "/") + VARIANT_AB;
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
            StartCoroutine(dt.Start());
            while (!dt.isDownloadFinished)
                yield return null;
            if (!string.IsNullOrEmpty(downloadError)) { onFinished?.Invoke("DownloadError:" + downloadError); yield break; }
            Debug.Log($"Update AssetBundles finished");
        }

        var localVersionFilePath = Path.Combine(localABDirectory, VERSION_FILE_NAME).Replace(@"\", "/");
        var remoteVersionPath = Path.Combine(remoteABDirectory, VERSION_FILE_NAME).Replace(@"\", "/");
        var reqVer = UnityWebRequest.Get(remoteVersionPath);
        yield return reqVer.SendWebRequest();
        if (!string.IsNullOrEmpty(reqVer.error)) { onFinished?.Invoke("version file not found"); yield break; }
        
        var localHashFilePath = Path.Combine(localABDirectory, HASH_FILE_NAME).Replace(@"\", "/");
        var remoteHashFilePath = Path.Combine(remoteABDirectory, HASH_FILE_NAME).Replace(@"\", "/");
        var reqHash = UnityWebRequest.Get(remoteHashFilePath);
        yield return reqHash.SendWebRequest();
        if (!string.IsNullOrEmpty(reqHash.error)) { onFinished?.Invoke("hash file not found"); yield break; }
        
        using (var fsHash = new FileStream(localHashFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            fsHash.Write(reqHash.downloadHandler.data, 0, reqHash.downloadHandler.text.Length);
        }
        using (var fsVer = new FileStream(localVersionFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            fsVer.Write(reqVer.downloadHandler.data, 0, reqVer.downloadHandler.text.Length);
        }
        Debug.Log($"Update Version & Hash finished");

        var remoteABMainPath = Path.Combine(remoteABDirectory, ABDirectoryName).Replace(@"\","/");
        var localABMainPath = Path.Combine(localABDirectory, ABDirectoryName).Replace(@"\","/");
        var reqMain = UnityWebRequestAssetBundle.GetAssetBundle(remoteABMainPath);
        var handler = new DownloadHandlerFile(localABMainPath);
        reqMain.downloadHandler = handler;
        reqMain.disposeDownloadHandlerOnDispose = true;
        yield return reqMain.SendWebRequest();
        if (!string.IsNullOrEmpty(reqMain.error)) { onFinished?.Invoke("Get MainAssetBundle Error:" + reqMain.error); yield break; }
        var localMain = AssetBundle.LoadFromFile(localABMainPath);
        if (localMain == null) { onFinished?.Invoke("Get MainAssetBundle NULL"); yield break; }
        var manifest = localMain.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        if (manifest == null) { onFinished?.Invoke("Get manifest NULL"); yield break; }
        localMain.Unload(true);
        Debug.Log($"Update MainAssetBundle finished");

        onFinished?.Invoke(MARK_SUCCESS);
    }

    public void CheckCompleteAsync(System.Action<string> onFinished)
    {
        StartCoroutine(CheckComplete(onFinished));
    }
    IEnumerator CheckComplete(System.Action<string> onFinished)
    {
        var remoteHashPath = Path.Combine(remoteABDirectory, HASH_FILE_NAME).Replace(@"\", "/");
        UnityWebRequest reqHash = UnityWebRequest.Get(remoteHashPath);
        yield return reqHash.SendWebRequest();
        if (!string.IsNullOrEmpty(reqHash.error)) { onFinished?.Invoke(reqHash.error); yield break; }
        if (!Directory.Exists(localABDirectory))
            Directory.CreateDirectory(localABDirectory);

        var list = reqHash.downloadHandler.text.Split(HASH_FILE_SPLITER);
        string broken = "";
        foreach (var item in list)
        {
            if (string.IsNullOrEmpty(item)) continue;
            ABHashInfo info = ABHashInfo.Parse(item);
            var localABPath = Path.Combine(localABDirectory, info.ABFileName + VARIANT_AB).Replace(@"\", "/");
            if (!File.Exists(localABPath))
            {
                if (info != null && info.type == BundleLoadType.InGame_Static) continue;
                broken += item + HASH_FILE_SPLITER;
                Debug.Log($"AB[{info.ABName}] no local file, remoteHash = {info.hash}");
            }
            else
            {
                var hash = Helpers.GetFileMD5(localABPath);
                if (info == null) continue;
                if (hash != info.hash) broken+=item+ HASH_FILE_SPLITER;
                Debug.Log($"AB[{info.ABName}] localHash = {hash}, remoteHash = {info.hash}");
            }
        }

        if (broken.Length > 0)
        {
            onFinished?.Invoke(MARK_SUCCESS + HASH_FILE_SPLITER + broken.Substring(0, broken.Length - 1));
        }
        else
        {
            onFinished?.Invoke(MARK_SUCCESS);
        }
    }

    long GetLocalABSize(string abName)
    {
        long size = 0;
        var abfile = abName + VARIANT_AB;

        var localABFile = Path.Combine(localABDirectory, abfile).Replace(@"\", "/");

        var tempABFile = (localABFile + ABDownloadTask.TEMP_VARIANT).Replace(@"\", "/");

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
            var abfile = abList[i] + VARIANT_AB;
            existedSize += GetLocalABSize(abList[i]);
            remoteABPaths.Add(Path.Combine(remoteABDirectory, abfile));
        }
        if (onGetSize == null) { onGetSize?.Invoke(0); return; }
        StartCoroutine(CheckRemoteABSize(remoteABPaths, onGetSize, existedSize));
    }
    IEnumerator CheckRemoteABSize(List<string> abList, System.Action<long> onGetSize, long existedSize = 0)
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
        onGetSize?.Invoke(System.Math.Max(0,totalSize - existedSize));
    }
    
    public bool CheckFreeSpace(long size, string saveDir = null)
    {
        if (string.IsNullOrEmpty(saveDir)) { saveDir = localABDirectory; }
        long freeSpace = 0;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        var dis = DriveInfo.GetDrives();
        var diskName = saveDir.Split(':')[0];
        foreach (var di in dis)
            if (di.Name.Split(':')[0] == diskName) { freeSpace = di.TotalFreeSpace; break; }
        return freeSpace > size;
#elif UNITY_ANROID
        // unchecked
        return true;
#elif UNITY_IOS
        // unchecked
        return true;
        // etc platforms
#else
        return true
#endif
    }

    /// <summary>
    /// Download AssetBundles to temp directory, then copy all files to LocalABPath
    /// </summary>
    /// <param name="onProgress">FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed(kb/s)</param>
    /// <param name="abList">Hash AssetBundle Name List</param>
    /// <param name="onFinished">copy files finished</param>
    /// <param name="onDownloadFinished">download finished</param>
    public void DownloadABInGame(List<string> abList, ABDownloadTask.OnABDownloadProgress onProgress, System.Action<string> onFinished = null)
    {
        List<string> remoteABPaths = new List<string>();
        for (int i = 0; i < abList.Count; i++)
            remoteABPaths.Add(Path.Combine(remoteABDirectory, abList[i] + VARIANT_AB).Replace(@"\","/"));

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

    #endregion

    public void InitializeOnStart(System.Action<string> onFinished)
    {
        if (isInitABRef) return;

        var localMainABPath = Path.Combine(localABDirectory + "/" + ABDirectoryName);
        if (!File.Exists(localMainABPath))
        {
            onFinished?.Invoke($"InitABRefrence FAILED: MainAB NOT found at path[{localMainABPath}]");
            return;
        }

        main = AssetBundle.LoadFromFile(localMainABPath);
        if (main == null) { onFinished?.Invoke("InitABRefrence FAILED: MainAB Null"); return; }
        manifest = main.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        if (manifest == null) { onFinished?.Invoke("InitABRefrence FAILED: AssetBundleManifest Null"); return; }

        var localVersionPath = Path.Combine(localABDirectory + "/" + VERSION_FILE_NAME);
        if (!File.Exists(localVersionPath))
        {
            onFinished?.Invoke($"InitABRefrence FAILED: version file NOT found at path[{localVersionPath}]");
            return;
        }
        var localHashPath = localHashFilePath;
        if (!File.Exists(localHashPath))
        {
            onFinished?.Invoke($"InitABRefrence FAILED: hash file NOT found at path[{localHashPath}]");
            return;
        }
        Catalog = AssetBundleCatalog.Parse(File.ReadAllText(localVersionPath), File.ReadAllText(localHashPath));
        if (Catalog == null || Catalog.mainVersion == 0)
        {
            onFinished?.Invoke($"InitABRefrence FAILED: mainVersion NOT valid]");
            return;
        }

        foreach (var abFile in manifest.GetAllAssetBundles())
        {
            var abPath = Path.Combine(localABDirectory + "/" + abFile);
            var abName = abFile.Substring(0, abFile.Length - VARIANT_AB.Length);

            var depedencies = new List<string>();
            foreach (var dp in manifest.GetAllDependencies(abFile))
                depedencies.Add(dp.Substring(0, dp.Length - VARIANT_AB.Length));
            dicABDependencies[abName] = depedencies;

            Catalog.bundles.TryGetValue(abName, out var aBCatalogInfo);
            if (aBCatalogInfo == null) continue;
            dicABLoadType[abName] = aBCatalogInfo.type;
            
            if (!File.Exists(abPath)) continue;
            var ab = AssetBundle.LoadFromFile(abPath);
            if (ab == null) { onFinished?.Invoke($"InitABRefrence FAILED: AssetBundle LoadFromFile [{abPath}] Null"); return; }

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

        isInitABRef = true;
        onFinished?.Invoke("success");
    }
    void InitializeInGame(List<string> abNames)
    {
        foreach (var abName in abNames)
        {
            var abFile = abName + VARIANT_AB;
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

    public List<string> GetAllAssetNames()
    {
        return System.Linq.Enumerable.ToList(dicAssetABName.Keys);
    }

    #region AsyncLoad

    public void LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, System.Action<string> onLoaded = null, System.Action<float> progress = null)
    {
        dicSceneAssetBundle.TryGetValue(sceneName, out var abName);
        if (string.IsNullOrEmpty(abName)) { progress?.Invoke(1); onLoaded?.Invoke("fail"); return; }
        LoadAssetBundle(abName, (abr) =>
        {
            StartCoroutine(LoadScene(sceneName, mode, onLoaded, progress));
        });
    }
    IEnumerator LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, System.Action<string> onLoaded = null, System.Action<float> progress = null)
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
    public void GetAssetAsync<T>(string assetName, System.Action<T> onGet) where T : Object
    {
        dicAssetABName.TryGetValue(assetName.ToLower(), out var bundleName);
        if (string.IsNullOrEmpty(bundleName)) { onGet?.Invoke(null); return; }
        LoadAssetBundle(bundleName, (abr) =>
        {
            if (abr == null) { onGet?.Invoke(null); return; }
            StartCoroutine(LoadAssetAsync(abr, assetName, onGet));
        });
    }
    IEnumerator LoadAssetAsync<T>(AssetBundleRef abr, string assetName, System.Action<T> onGet) where T : Object
    {
        var abReq = abr.bundle.LoadAssetAsync<T>(assetName);
        yield return abReq;
        onGet?.Invoke((T)abReq.asset);
    }
    public void LoadAssetBundle(string bundleName, System.Action<AssetBundleRef> callback)
    {
        StartCoroutine(LoadAssetBundleAsync(bundleName, callback));
    }
    private IEnumerator LoadAssetBundleAsync(string bundleName, System.Action<AssetBundleRef> callback)
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
                StartCoroutine(LoadAssetBundleAsync(dependency, (ab) => { dependenciesList.Add(ab); }));
            }
        }

        while (dependenciesList.Count < dependencyCount)
            yield return null;

        if (loadingAssetBundles.ContainsKey(bundleName))
        {
            loadingAssetBundles[bundleName] += callback;
            yield break;
        }

        string path = Path.Combine(localABDirectory, bundleName+VARIANT_AB);
        AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path);
        loadingAssetBundles.Add(bundleName, null);

        yield return request;

        if (request.assetBundle != null)
        {
            AssetBundle bundle = request.assetBundle;
            var loaded = new AssetBundleRef(bundle)
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

    #endregion

    #region DirectLoad

    public T GetAsset<T>(string assetName) where T : Object
    {
        T res = null;

        dicAssetABName.TryGetValue(assetName.ToLower(), out var bundleName);
        if (string.IsNullOrEmpty(bundleName)) return res;

        var abref = LoadAssetBundle(bundleName);
        if (abref == null)
            return res;
        if (abref.bundle == null)
            return res;
        res = abref.bundle.LoadAsset<T>(assetName);

        return res;
    }
    public AssetBundleRef LoadAssetBundle(string bundleName)
    {
        loadedAssetBundles.TryGetValue(bundleName, out var abr);
        if (abr != null)
            return abr;
        dicABDependencies.TryGetValue(bundleName, out var dependencies);
        List<AssetBundleRef> dependenciesList = new List<AssetBundleRef>();
        if (dependencies != null)
        {
            foreach (var dependency in dependencies)
            {
                dependenciesList.Add(LoadAssetBundle(dependency));
            }
        }
        string path = Path.Combine(localABDirectory, bundleName + VARIANT_AB);
        var ab = AssetBundle.LoadFromFile(path);
        if (ab == null)
        {
            Debug.Log("LoadAssetBundle FAILED at pat: " + path);
            return null;
        }
        dicABLoadType.TryGetValue(bundleName, out var loadType);
        var loaded = new AssetBundleRef(ab)
        {
            dependencies = dependenciesList,
            type = loadType,
        };
        loadedAssetBundles[bundleName] = loaded;
        return loaded;
    }
    
    #endregion

    public void UnloadAllAssetBundles()
    {
        foreach (var bundle in loadedAssetBundles)
        {
            bundle.Value.bundle.Unload(true);
        }
        loadedAssetBundles.Clear();
    }

    public List<AssetBundleRef> GetAllLoadedAB()
    {
        List<AssetBundleRef> res = new List<AssetBundleRef>();

        foreach (var assetBundle in loadedAssetBundles.Values)
        {
            res.Add(assetBundle);
        }

        return res;
    }
    public bool IsBundleLoaded(string abName)
    {
        loadedAssetBundles.TryGetValue(abName, out var bundle);
        return bundle != null;
    }
}

[System.Serializable]
public class AssetBundleCatalog
{
    public Dictionary<string, ABHashInfo> bundles = new Dictionary<string, ABHashInfo>();
    public int mainVersion, subVersion, resVersion;
    public static AssetBundleCatalog Parse(string ver, string hash)
    {
        AssetBundleCatalog res = new AssetBundleCatalog();
        try
        {
            var versionInfoLine = ver;
            var parts1 = versionInfoLine.Split(AssetBundleManager.VERSION_FILE_SPLITER);
            res.mainVersion = int.Parse(parts1[0]);
            res.subVersion = int.Parse(parts1[1]);
            res.resVersion = int.Parse(parts1[2]);
            var lines = hash.Split(AssetBundleManager.HASH_FILE_SPLITER);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                ABHashInfo info = ABHashInfo.Parse(line);
                if (info == null) { Debug.Log($"Parse hash line[{i}] FAILED"); continue; }
                res.bundles[info.abPath] = info;
            }
        }
        catch
        {
            return null;
        }

        return res;
    }
}
[System.Serializable]
public class ABCatalogInfo
{
    public string abName;
    public string abNameHash;
    public long bundleSize;
    public int bundleLoadType;

    public List<string> dependencies = new List<string>();
    public List<string> resPath = new List<string>();
    public List<string> resPathHas = new List<string>();
}

