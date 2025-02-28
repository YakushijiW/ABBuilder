using System.Collections.Generic;
using System.IO;
using UnityEngine;
/// <summary>
/// 分包策略：Resources, StreamingAssetsPath(AppPath), PersistentDataPath(LocalPath)并用，按照功能分类模块化管理AB包
/// 热更新流程：打开应用即检测更新，所有更新内容存储在LocalPath，确保与服务器版本一致后可进入游戏
/// </summary>
public enum ABType
{
    /// <summary>
    /// 打包在AppPath目录，无法被热更新。
    /// </summary>
    Basic,
    /// <summary>
    /// 同时存在于AppPath，LocalPath目录，优先LocalPath
    /// </summary>
    BasicAndHotfix,
    /// <summary>
    /// 从服务器下载，保存在LocalPath。
    /// </summary>
    Hotfix,
    // 以下为扩展功能
    /// <summary>
    /// 与普通热更新流程基本一致，在更新完成后需要重启游戏，一般用于ILRuntime层代码更新
    /// </summary>
    HotfixRestart,
    /// <summary>
    /// 在热更新流程中不被下载。在完成热更新流程后，若部分模块并非玩家能够第一时间接触到，被归为该类可以减少进入游戏前的下载所需时间
    /// </summary>
    Extra,
}
public class ABBuildConfig
{
    public const string VERSION_FILE_NAME = "ver.txt";
    public const string HASH_FILE_NAME = "hash.txt";
    public const string VARIANT_AB = ".ab";
    public const string VARIANT_AB_ENCRYPT = "";

    public const char HASH_FILE_SPLITER = '\n';
    public const char VERSION_FILE_SPLITER = '.';
    public const char AB_INFO_SPLITER = '=';

    public const string BundleEncryptKey = "1234567812345678"; // 16字节密钥，AES-128
    public const string BundleEncryptIV = "1234567812345678"; // 16字节IV（初始化向量）

    public static string ABDirectoryName
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
    public static string LocalPath { get { return Application.persistentDataPath; } }
    public static string AppPath { get { return Application.streamingAssetsPath; } }
    public static string LocalABDirectory { get { return Path.Combine(LocalPath + $"/{ABDirectoryName}"); } }
    public static string AppABDirectory { get { return Path.Combine(AppPath + $"/{ABDirectoryName}"); } }
    public static string LocalVerFilePath { get { return Path.Combine(LocalABDirectory, VERSION_FILE_NAME).Replace(@"\", "/"); } }
    public static string AppVerFilePath { get { return Path.Combine(AppABDirectory, VERSION_FILE_NAME).Replace(@"\", "/"); } }
    public static string LocalHashFilePath { get { return Path.Combine(LocalABDirectory, HASH_FILE_NAME).Replace(@"\", "/"); } }
    public static string AppHashFilePath { get { return Path.Combine(AppABDirectory, HASH_FILE_NAME).Replace(@"\", "/"); } }
}

[System.Serializable]
public class BundleData
{
    public string bundleName = "";
    public List<string> directories = new List<string>();
    public ABType bundleType;
    public bool packTogether = true;
    public uint encrypt = 0;
}