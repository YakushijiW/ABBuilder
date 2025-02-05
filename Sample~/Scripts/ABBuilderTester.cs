using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

public class ABBuilderTester : MonoBehaviour
{
    [SerializeField]
    Toggle togManual;
    [SerializeField]
    GameObject goManual, goSpawnRoot, goConfirm, goProgress;
    [SerializeField]
    Text txtLog, txtConfirm, txtOKBtn, txtCancelBtn, txtProgress;
    [SerializeField]
    Button btnOK, btnCancel, btnStart, btnSpawn, btnSetMat, btnSpawnRemote, btnShowVid, btnCheckComplete , btnClearLog;
    [SerializeField]
    Dropdown ddSpawn, ddSetMat, ddSpawnRemote, ddShowVid;
    [SerializeField]
    RawImage rImgVid1;
    [SerializeField]
    UnityEngine.Video.VideoPlayer vidPlayer;
    [SerializeField]
    Slider sliProgress;

    
    RectTransform rtLogContent;

    private void Awake()
    {
        DontDestroyOnLoad (gameObject);

        rtLogContent = txtLog.transform.parent.GetComponent<RectTransform>();
        btnClearLog.onClick.RemoveAllListeners();
        btnClearLog.onClick.AddListener(OnClickClearLog);
        btnStart.onClick.RemoveAllListeners();
        btnStart.onClick.AddListener(OnClickStart);
        btnSpawn.onClick.RemoveAllListeners();
        btnSpawn.onClick.AddListener(OnClickSpawn);
        btnSetMat.onClick.RemoveAllListeners();
        btnSetMat.onClick.AddListener(OnClickSetMat);
        btnSpawnRemote.onClick.RemoveAllListeners();
        btnSpawnRemote.onClick.AddListener(OnClickSpawnRemote);
        btnShowVid.onClick.RemoveAllListeners();
        btnShowVid.onClick.AddListener(OnClickShowVid);
        btnCheckComplete.onClick.RemoveAllListeners();
        btnCheckComplete.onClick.AddListener(OnClickCheckComplete);
        togManual.onValueChanged.AddListener((b) => { goManual.SetActive(b); });
    }
    private void Start()
    {
        StartCoroutine(InitGame());
    }
    IEnumerator InitGame()
    {
        yield return AssetBundleManager.Instance.InitAsync();
        AssetBundleManager.Instance.CheckUpdate((result, str1) =>
        {
            switch (result)
            {
                case AssetBundleManager.CheckVersionResult.error:
                    Log(str1);
                    break;
                case AssetBundleManager.CheckVersionResult.success:
                    RefreshStartGame();
                    break;
                case AssetBundleManager.CheckVersionResult.updateApp:
                    ShowConfirm($"Need to download new app", () => { Log("Go link [exapmle.com] to download"); }, () => { Application.Quit(); });
                    break;
                case AssetBundleManager.CheckVersionResult.updateCatalog:
                    {
                        var list = System.Linq.Enumerable.ToList(str1.Split(AssetBundleManager.HASH_FILE_SPLITER));
                        long.TryParse(list[list.Count - 1], out var size);
                        if (size < 1)
                        {
                            // Update version & hash files only
                            AssetBundleManager.Instance.StartGameDownload("", (s) =>
                            {
                                // Enter game
                                Log($"All Update Finished");
                                goProgress.SetActive(false);
                                RefreshStartGame();
                            });
                        }
                    }
                    break;
                case AssetBundleManager.CheckVersionResult.updateEnter:
                case AssetBundleManager.CheckVersionResult.updateRestart:
                    {
                        var list = System.Linq.Enumerable.ToList(str1.Split(AssetBundleManager.HASH_FILE_SPLITER));
                        long.TryParse(list[list.Count - 1], out var size);
                        Debug.Log("UpdateList: \n" + str1);
                        bool success = list.Count > 0 && list[0] == AssetBundleManager.MARK_SUCCESS;
                        if (success)
                        {
                            ShowConfirm($"Need to download {(size / 1024f).ToString("0.##")} kb game resource", () =>
                            {
                                if (!AssetBundleManager.Instance.CheckFreeSpace(size))
                                {
                                    ShowConfirm("No enough space", () => { Application.Quit(); }, () => { Application.Quit(); });
                                    return;
                                }
                                string updateContent = "";
                                for (int i = 1; i < list.Count - 1; i++)
                                    updateContent += list[i] + AssetBundleManager.HASH_FILE_SPLITER;
                                //float debugTime = 0;
                                AssetBundleManager.Instance.StartGameDownload(updateContent, (str2) =>
                                {
                                    Log($"All Update Finished");
                                    goProgress.SetActive(false);
                                    if (result == AssetBundleManager.CheckVersionResult.updateEnter)
                                        ShowConfirm("All update finished, enter game ", RefreshStartGame, Application.Quit);
                                    else
                                        ShowConfirm("All update finished, restart game ", Application.Quit, Application.Quit);

                                },
                                    (FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed, FailedCount) =>
                                    {
                                            //if (debugTime < Time.time)
                                            //{
                                            RefreshProgress(size, list.Count - 2, FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed, FailedCount);
                                            //}
                                        });
                            }, () => { Application.Quit(); });
                        }
                        else
                        {
                            Log(str1);
                        }
                    }
                    break;
            }
        });
        yield break;
    }
    void RefreshStartGame()
    {
        btnStart.gameObject.SetActive(true);
        btnCheckComplete.gameObject.SetActive(true);
    }
    void RefreshProgress(long totalSize, int count, string FileName, int FileIndex,
    ulong CurDownloaded, float Progress, ulong TotalDownloaded, float Speed, int failedCount)
    {
        if (!goProgress.activeSelf)
            goProgress.SetActive(true);

        txtProgress.text = $"正在下载更新:进度{GetKB((long)TotalDownloaded)}kb/{GetKB(totalSize)}kb," +
            $"总共{count}个, 已完成{FileIndex - failedCount}个, 失败{failedCount}个, 速度{Speed}kb/s";
        sliProgress.value = (TotalDownloaded / (totalSize *1f));

        if (sliProgress.value >= 1)
        {
            goProgress.SetActive(false);
        }
    }
    public string GetKB(long BYTE)
    {
        return (BYTE/1024).ToString("0.##");
    }
    private void RefreshStarted()
    {
        ddSpawn.ClearOptions();
        ddSetMat.ClearOptions();
        ddShowVid.ClearOptions();
        var all = AssetBundleManager.Instance.GetAllAssetNames();
        var optionsPrefab = new List<Dropdown.OptionData>();
        var optionsMat = new List<Dropdown.OptionData>();
        var optionsVid = new List<Dropdown.OptionData>();
        foreach (var name in all)
        {
            if (name.StartsWith("mat"))
                optionsMat.Add(new Dropdown.OptionData(name));
            else if (name.StartsWith("go"))
                optionsPrefab.Add(new Dropdown.OptionData(name));
            else if (name.StartsWith("vid"))
                optionsVid.Add(new Dropdown.OptionData(name));
        }
        ddSpawn.AddOptions(optionsPrefab);
        ddSetMat.AddOptions(optionsMat);
        ddShowVid.AddOptions(optionsVid);

        btnSpawn.transform.parent.gameObject.SetActive(true);
        btnSetMat.transform.parent.gameObject.SetActive(true);
        btnSpawnRemote.transform.parent.gameObject.SetActive(true);
        btnShowVid.transform.parent.gameObject.SetActive(true);

        ddSpawnRemote.gameObject.SetActive(false);
    }
    public void ShowConfirm(string content, Action onOK = null, Action onCancel = null, string ok = "OK", string cancel = "Cancel")
    {
        if (!goConfirm.activeSelf)
            goConfirm.SetActive(true);

        btnOK.onClick.RemoveAllListeners();
        btnCancel.onClick.RemoveAllListeners();

        btnOK.onClick.AddListener(() => { goConfirm.SetActive(false); onOK?.Invoke(); });
        btnCancel.onClick.AddListener(() => { goConfirm.SetActive(false); onCancel?.Invoke(); });

        txtOKBtn.text = ok;
        txtCancelBtn.text = cancel;

        txtConfirm.text = content;
    }

    public void OnClickStart()
    {
        StartCoroutine(StartGame());
    }
    IEnumerator StartGame()
    {
        yield return AssetBundleManager.Instance.InitializeOnStartAsync();
        if (AssetBundleManager.Instance.IsInitABRef) yield break;
        AssetBundleManager.Instance.LoadSceneAsync("ABBuilderSampleScene2",
            UnityEngine.SceneManagement.LoadSceneMode.Single,
            (s) => { Log("LoadSceneAsyncFinished: " + s); if (string.IsNullOrEmpty(s)) RefreshStarted(); },
            (progress) => { Log(progress.ToString()); });
    }
    public void OnClickSpawn()
    {
        AssetBundleManager.Instance.GetAssetAsync<GameObject>(ddSpawn.options[ddSpawn.value].text, (obj) =>
        {
            Instantiate(obj, goSpawnRoot.transform);
        });
    }
    public void OnClickSetMat()
    {
        AssetBundleManager.Instance.GetAssetAsync<Material>(ddSetMat.options[ddSetMat.value].text, (mat) =>
        {
            GameObject go = GameObject.Find("Cube");
            if (go == null)
                go = GameObject.Find("Sphere");

            go.GetComponent<MeshRenderer>().material = mat;
        });
    }

    public void OnClickSpawnRemote()
    {
        var lv1ABList = new List<string>() { "testab1", "testab2", "testab3" };
        AssetBundleManager.Instance.CheckRemoteABSizeAsync(lv1ABList, (size) =>
        {
            ShowConfirm($"Need download {(size / 1024f).ToString("0.##")} kb game resource to spawn this object", () =>
            {
                if (size < 1)
                {
                    var go = AssetBundleManager.Instance.GetAsset<GameObject>("gotest1");
                    Instantiate(go, goSpawnRoot.transform);
                    return;
                }
                if (AssetBundleManager.Instance.CheckFreeSpace(size))
                {
                    AssetBundleManager.Instance.DownloadABInGame(lv1ABList, (FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed, FailedCount) =>
                    {
                        RefreshProgress(size, lv1ABList.Count, FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed, FailedCount);
                    }, (s) => {
                        if (!string.IsNullOrEmpty(s)) { Log(s); return; }
                        var go = AssetBundleManager.Instance.GetAsset<GameObject>("gotest1");
                        Instantiate(go, goSpawnRoot.transform);
                    });
                }
                else
                {
                    ShowConfirm("No enough free space to download game resource");
                }
            });
        });
    }
    public void OnClickShowVid()
    {
        if (vidPlayer.isPlaying)
            vidPlayer.Pause();

        RenderTexture rt = AssetBundleManager.Instance.GetAsset<RenderTexture>("renderTex");
        var clip = AssetBundleManager.Instance.GetAsset<UnityEngine.Video.VideoClip>(ddShowVid.options[ddShowVid.value].text);

        rImgVid1.texture = rt;
        vidPlayer.targetTexture = rt;
        vidPlayer.clip = clip;
        vidPlayer.SetDirectAudioVolume(0, .05f);
        vidPlayer.Play();
    }

    public void OnClickCheckComplete()
    {
        Log($"StartCheck");
        AssetBundleManager.Instance.CheckCompleteAsync((s) =>
        {
            if (s.StartsWith(AssetBundleManager.MARK_SUCCESS))
            {
                float debugTime = 0;
                int totalCount = 0;
                var list = s.Split(AssetBundleManager.HASH_FILE_SPLITER);
                if (list.Length > 1)
                    totalCount = list.Length - 1;
                else
                {
                    ShowConfirm("All files complete", () => { RefreshStartGame(); }, () => { Application.Quit(); });
                    return;
                }
                Log("Broken file count: " + (list.Length - 1) + ", start redownload");
                ABDownloadTask.OnABDownloadProgress onProgress = 
                (FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed, FailedCount) =>
                {
                    if (debugTime < Time.time)
                    {
                        Log($"DownloadingFile[{FileName}], Index[{FileIndex + 1}/{totalCount}], Progress[{Progress}]," +
                        $" Downloaded[{CurDownloaded}], TotalDownloaded[{TotalDownloaded}], Speed[{Speed}kb/s]");
                        debugTime = Time.time + .5f;
                    }
                };
                Log("Broken files count = " + (list.Length - 1));
                AssetBundleManager.Instance.StartGameDownload(s, (msg) =>
                {
                    Log($"All Update Finished");
                    goProgress.SetActive(false);
                    ShowConfirm("All update finished, enter game ", () => { RefreshStartGame(); }, () => { Application.Quit(); });
                }, onProgress);
            }
            else
            {
                Log(s);
            }
        });
    }

    public void OnClickClearLog()
    {
        txtLog.text = "";
    }

    void Log(string s)
    {
        txtLog.text+=s+"\n";
        if (rtLogContent.sizeDelta.y > 600)
            rtLogContent.localPosition = new Vector3(0, rtLogContent.sizeDelta.y - 600, 0);
    }

}
