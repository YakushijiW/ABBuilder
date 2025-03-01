using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using UnityEngine.Networking;
namespace ABBuilder
{
    public class ABDownloadTask
    {
        /// <summary>
        /// FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed(kb/s)
        /// </summary>
        /// <param name="FileName">Current downloading file</param>
        /// <param name="FileIndex">Current Index of all files</param>
        /// <param name="CurDownloaded">Current file downloaded bytes</param>
        /// <param name="Progress">Progress of all download task, value between (0 - 1)</param>
        /// <param name="TotalDownloaded">Current downloaded bytes of all files</param>
        /// <param name="Speed">Current Speed of downloading (kb/s)</param>
        /// <param name="failedCount">download FAILED AssetBundle count</param>
        public delegate void OnABDownloadProgress(string FileName, int FileIndex,
        ulong CurDownloaded, float Progress, ulong TotalDownloaded, float Speed, int failedCount);

        public bool isDownloadFinished { get; private set; }
        /// <summary>
        /// FileName, FileIndex, CurDownloaded, Progress, TotalDownloaded, Speed(kb/s)
        /// </summary>
        //public Action<string, int, ulong, float, ulong, float> onProgress;

        public OnABDownloadProgress onProgress;

        public Action<string> onDownloadFinished;

        List<string> urls;
        public string SavePath;
        public const string TEMP_VARIANT = ".tmp";
        public List<string> FailedUrls { get; private set; }
        public ABDownloadTask(List<string> infos, string savePath)
        {
            urls = infos;
            SavePath = savePath;
            FailedUrls = new List<string>();
        }
        string GetFileName(string downloadUrl)
        {
            var arr = downloadUrl.Split('/');
            return arr[arr.Length - 1];
        }
        public IEnumerator Start()
        {
            ulong downloaded = 0;
            float lastTime = Time.time;
            ulong lastSize = 0;
            ulong curDownloaded = 0;
            float kbps = 0f;
            for (int i = 0; i < urls.Count; i++)
            {
                var url = urls[i];
                var fileName = GetFileName(url);

                UnityWebRequest req = UnityWebRequest.Get(url);
                string fileSavePath = Path.Combine(SavePath + $"/{fileName}");
                var dhf = new DownloadHandlerFileRange(fileSavePath, req, TEMP_VARIANT);
                req.downloadHandler = dhf;
                dhf.OnProgress += (a, b, c) =>
                {
                    curDownloaded = downloaded + req.downloadedBytes;
                    if (Time.time >= lastTime + 1f)
                    {
                        kbps = (curDownloaded - lastSize) / 1024f;
                        lastSize = curDownloaded;
                        lastTime = Time.time;
                    }

                    onProgress?.Invoke(fileName, i, dhf.CurDownloadedSize, dhf.DownloadProgress, curDownloaded, kbps, FailedUrls.Count); // dhf.Speed
            };
                dhf.onFailed += (e) =>
                {
                    FailedUrls.Add(e);
                };

                req.SendWebRequest();

                if (!string.IsNullOrEmpty(req.error))
                {
                    isDownloadFinished = true;
                    onDownloadFinished?.Invoke($"FAIL:{req.error}");
                    yield break;
                }
                while (!req.isDone)
                {
                    //curDownloaded = downloaded + req.downloadedBytes;
                    //if (Time.time > lastTime + 1f)
                    //    lastSize = curDownloaded;
                    yield return null;
                }
                downloaded += req.downloadedBytes;
                dhf.ManualDispose();

                fileSavePath += TEMP_VARIANT;
                if (File.Exists(fileSavePath))
                {
                    var abSavePath = fileSavePath.Substring(0, fileSavePath.Length - TEMP_VARIANT.Length);
                    if (File.Exists(abSavePath))
                        File.Delete(abSavePath);
                    File.Move(fileSavePath, fileSavePath.Substring(0, fileSavePath.Length - TEMP_VARIANT.Length));
                }
            }



            isDownloadFinished = true;
            onDownloadFinished?.Invoke("");
            yield break;
        }
    }
}
