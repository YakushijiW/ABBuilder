using System.Security.Cryptography;
using System.IO;
using UnityEngine;
using System.Text;
using System;
using System.Collections.Generic;
namespace ABBuilder
{
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
        public static void HandleFilesInDirectory(DirectoryInfo dir, bool isTop, System.Action<FileInfo> handle,
            string pattern = null, List<string> ignorePostfix = null, List<string> ignoreDirectory = null)
        {
            foreach (var file in dir.GetFiles(string.IsNullOrEmpty(pattern) ? "*" : pattern, SearchOption.TopDirectoryOnly))
            {
                if (file.Extension == ".meta") continue;
                if (ignorePostfix != null && ignorePostfix.Contains(file.Extension)) continue;

                handle?.Invoke(file);
            }
            if (!isTop)
            {
                foreach (var d in dir.GetDirectories())
                {
                    if (ignoreDirectory != null && ignoreDirectory.Contains(d.FullName)) continue;
                    HandleFilesInDirectory(d, false, handle, pattern, ignorePostfix, ignoreDirectory);
                }
            }
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
        public static byte[] EncryptAES(byte[] data, string Key, string IV)
        {
            byte[] ivBytes = Encoding.UTF8.GetBytes(IV);
            byte[] keyBytes = Encoding.UTF8.GetBytes(Key);

            // 校验密钥和IV的字节长度
            if (keyBytes.Length != 16 && keyBytes.Length != 24 && keyBytes.Length != 32)
            {
                Debug.LogError("密钥长度必须为 16、24 或 32 字节（对应 AES-128/192/256）");
                return null;
            }
            if (ivBytes.Length != 16)
            {
                Debug.LogError("IV 必须为 16 字节");
                return null;
            }

            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Padding = PaddingMode.PKCS7;
                aes.Mode = CipherMode.CBC;

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    // 写入 IV 到加密数据头部（可选，若需要动态IV）
                    // msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        csEncrypt.Write(data, 0, data.Length);
                        csEncrypt.FlushFinalBlock(); // 关键：确保数据完整写入
                    }
                    return msEncrypt.ToArray();
                }
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
        //public static byte[] DecryptAES(byte[] data, string Key, string IV)
        //{
        //    if (Key.Length != IV.Length && IV.Length != 16) return null;
        //    // 提取 IV（AES 的 IV 长度通常为 16 字节）
        //    byte[] iv = System.Text.Encoding.UTF8.GetBytes(IV);
        //    byte[] key = System.Text.Encoding.UTF8.GetBytes(Key);
        //    System.Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        //    byte[] actualData = new byte[data.Length - iv.Length];
        //    System.Buffer.BlockCopy(data, iv.Length, actualData, 0, actualData.Length);

        //    // 解密数据
        //    using (Aes aes = Aes.Create())
        //    {
        //        aes.Key = key;
        //        aes.IV = iv;
        //        aes.Padding = PaddingMode.PKCS7;
        //        aes.Mode = CipherMode.CBC;
        //        byte[] decryptedData = null;
        //        //using (ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV)) // 创建解密器
        //        //{
        //        //    decryptedData = decryptor.TransformFinalBlock(actualData, 0, actualData.Length);
        //        //}
        //        ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        //        using (MemoryStream msDecrypt = new MemoryStream(data))
        //        {
        //            using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
        //            {
        //                //using (StreamReader srDecrypt = new StreamReader(csDecrypt))
        //                //{
        //                //    return srDecrypt.ReadToEnd();
        //                //}
        //                using (MemoryStream outputStream = new MemoryStream())
        //                {
        //                    csDecrypt.CopyTo(outputStream);
        //                    decryptedData = outputStream.ToArray();
        //                }
        //            }
        //        }
        //        return decryptedData;
        //    }
        //}
        public static byte[] DecryptAES(byte[] encryptedData, string Key, string IV)
        {
            byte[] ivBytes = Encoding.UTF8.GetBytes(IV);
            byte[] keyBytes = Encoding.UTF8.GetBytes(Key);
            // 校验密钥和IV的字节长度
            if (keyBytes.Length != 16 && keyBytes.Length != 24 && keyBytes.Length != 32)
            {
                Debug.LogError("密钥长度必须为 16、24 或 32 字节（对应 AES-128/192/256）");
                return null;
            }
            if (ivBytes.Length != 16)
            {
                Debug.LogError("IV 必须为 16 字节");
                return null;
            }

            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Padding = PaddingMode.PKCS7;
                aes.Mode = CipherMode.CBC;

                using (MemoryStream msDecrypt = new MemoryStream(encryptedData))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        using (MemoryStream outputStream = new MemoryStream())
                        {
                            csDecrypt.CopyTo(outputStream);
                            return outputStream.ToArray();
                        }
                    }
                }
            }
        }

        public static class ByteFormatter
        {
            private static readonly string[] Units = { "B", "KB", "MB", "GB" };
            private static readonly ulong[] Factors = { 1, 1L << 10, 1L << 20, 1L << 30 };
            public static string FormatBytes(ulong bytes, int decimalPlaces = 2)
            {
                if (bytes == 0) return "0 B";

                for (int i = Units.Length - 1; i >= 0; i--)
                {
                    if (bytes < Factors[i]) continue;

                    double value = (double)bytes / Factors[i];
                    return FormatNumber(value, decimalPlaces) + " " + Units[i];
                }
                return bytes.ToString("0") + " B";
            }
            private static string FormatNumber(double value, int decimalPlaces)
            {
                string format = decimalPlaces > 0
                    ? $"0.{new string('0', decimalPlaces)}"
                    : "0";

                return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
