using System.Collections.Generic;
using System.IO;
using UnityEngine;
/// <summary>
/// �ְ����ԣ�Resources, StreamingAssetsPath(AppPath), PersistentDataPath(LocalPath)���ã����չ��ܷ���ģ�黯����AB��
/// �ȸ������̣���Ӧ�ü������£����и������ݴ洢��LocalPath��ȷ����������汾һ�º�ɽ�����Ϸ
/// </summary>
public enum ABType
{
    /// <summary>
    /// �����AppPathĿ¼���޷����ȸ��¡�
    /// </summary>
    Basic,
    /// <summary>
    /// ͬʱ������AppPath��LocalPathĿ¼������LocalPath
    /// </summary>
    BasicAndHotfix,
    /// <summary>
    /// �ӷ��������أ�������LocalPath��
    /// </summary>
    Hotfix,
    // ����Ϊ��չ����
    /// <summary>
    /// ����ͨ�ȸ������̻���һ�£��ڸ�����ɺ���Ҫ������Ϸ��һ������ILRuntime��������
    /// </summary>
    HotfixRestart,
    /// <summary>
    /// ���ȸ��������в������ء�������ȸ������̺�������ģ�鲢������ܹ���һʱ��Ӵ���������Ϊ������Լ��ٽ�����Ϸǰ����������ʱ��
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

    public const string BundleEncryptKey = "1234567812345678"; // 16�ֽ���Կ��AES-128
    public const string BundleEncryptIV = "1234567812345678"; // 16�ֽ�IV����ʼ��������

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