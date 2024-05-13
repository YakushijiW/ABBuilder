using System.Security.Cryptography;
using System.IO;

public class Helpers
{
    public static string GetFileMD5(string path)
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
    public static string GetMD5(string content)
    {
        var str = "";
        MD5 md5 = MD5.Create();
        byte[] s = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        for (int i = 0; i < s.Length; i++)
        {
            str += s[i].ToString("X2");
        }
        return  str;
    }
}
