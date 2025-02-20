using System.Security.Cryptography;
using System.IO;
using UnityEngine;
using System.Text;
using System;
using UnityEngine.InputSystem;

public class Helpers
{
    public static string ParseToMD5(string path)
    {
        var str = "";
        using (var md5 = MD5.Create())
        {
            using (var fs = File.OpenRead(path))
            {
                byte[] buffer = md5.ComputeHash(fs);
                for (int i = 0; i < buffer.Length; i++)
                {
                    str += buffer[i].ToString("X2");
                }
            }
        }
        return str;
    }
    public static string ParseFromMD5(string content)
    {
        var str = "";
        MD5 md5 = MD5.Create();
        byte[] s = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        for (int i = 0; i < s.Length; i++)
        {
            str += s[i].ToString("X2");
        }
        return str;
    }

    public static string GetBasicBundlePath(string platform)
    {
        return Application.streamingAssetsPath + $"/{platform}/Basic/";
    }

    // 加密函数
    public static string Encrypt(string plainText)
    {
        using (Aes aesAlg = Aes.Create()) // 创建AES加密实例
        {
            aesAlg.Key = Encoding.UTF8.GetBytes(ABBuildConfig.BundleEncryptKey);  // 设置密钥
            aesAlg.IV = Encoding.UTF8.GetBytes(ABBuildConfig.BundleEncryptIV);    // 设置初始化向量

            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV); // 创建加密器

            using (MemoryStream msEncrypt = new MemoryStream())  // 用于存储加密后的数据
            {
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))  // 创建加密数据流
                {
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))  // 写入加密流
                    {
                        swEncrypt.Write(plainText);  // 写入明文数据
                    }
                }
                // 返回加密后的数据，转换为Base64字符串
                return Convert.ToBase64String(msEncrypt.ToArray());
            }
        }
    }
    // 加密函数
    public static byte[] EncryptAES(byte[] data, string Key, string IV)
    {
        if (Key.Length != IV.Length && IV.Length != 16) { Debug.Log($"byte length is NOT 16"); return null; }

        byte[] iv = System.Text.Encoding.UTF8.GetBytes(IV);
        byte[] key = System.Text.Encoding.UTF8.GetBytes(Key);
        // 使用 AES 加密（示例）
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            byte[] result = null;
            using (ICryptoTransform encryptor = aes.CreateEncryptor())
            {
                var encryptedData = encryptor.TransformFinalBlock(data, 0, data.Length);
                // 将 IV 和加密数据合并写入文件
                result = new byte[aes.IV.Length + encryptedData.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(encryptedData, 0, result, aes.IV.Length, encryptedData.Length);
            }
            return result;
        }
    }
    // 解密函数
    public static string Decrypt(string cipherText)
    {
        using (Aes aesAlg = Aes.Create()) // 创建AES实例
        {
            aesAlg.Key = Encoding.UTF8.GetBytes(ABBuildConfig.BundleEncryptKey);  // 设置密钥
            aesAlg.IV = Encoding.UTF8.GetBytes(ABBuildConfig.BundleEncryptIV);    // 设置初始化向量

            ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);  // 创建解密器

            using (MemoryStream msDecrypt = new MemoryStream(Convert.FromBase64String(cipherText)))  // 将Base64字符串转换为字节数组并读取
            {
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))  // 创建解密数据流
                {
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))  // 从解密流中读取数据
                    {
                        return srDecrypt.ReadToEnd();  // 返回解密后的明文
                    }
                }
            }
        }
    }
    // 解密函数
    public static byte[] DecryptAES(byte[] data, string Key, string IV)
    {
        if (Key.Length != IV.Length && IV.Length != 16) return null;
        // 提取 IV（AES 的 IV 长度通常为 16 字节）
        byte[] iv = System.Text.Encoding.UTF8.GetBytes(IV);
        byte[] key = System.Text.Encoding.UTF8.GetBytes(Key);
        System.Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        byte[] actualData = new byte[data.Length - iv.Length];
        System.Buffer.BlockCopy(data, iv.Length, actualData, 0, actualData.Length);

        // 解密数据
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            byte[] decryptedData = null;
            using (ICryptoTransform decryptor = aes.CreateDecryptor()) // 创建解密器
            {
                decryptedData = decryptor.TransformFinalBlock(actualData, 0, actualData.Length);
            }
            return decryptedData;
        }
    }
}
