using System;

[Serializable]
public class ABHashInfo
{
    public const char SPLITER = ',';
    public string abPath;
    public string hash;
    public long size;
    public BundleLoadType type;

    public string ABFileName { 
        get {
            if (string.IsNullOrEmpty(abPath)) return null;
            var dirs = abPath.Split('/');
            return dirs[dirs.Length - 1];
        }  
    }

    public string ABName
    {
        get
        {
            if (string.IsNullOrEmpty(abPath)) return null;
            var dirs = abPath.Split('/');
            var abfilename = dirs[dirs.Length - 1];
            return abfilename.Split('.')[0];
        }
    }
    public static ABHashInfo Parse(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        ABHashInfo info = new ABHashInfo();
        var parts = line.Split(SPLITER);
        if (parts.Length >= 4)
        {
            try
            {
                info.abPath = parts[0];
                info.size = long.Parse(parts[1]);
                info.type = (BundleLoadType)int.Parse(parts[2]);
                info.hash = parts[3];
            }
            catch
            {
                return null;
            }
        }
        else
            return null;
        return info;
    }
    public static string ToString(ABHashInfo info)
    {
        if (info == null) return null;
        return info.abPath + SPLITER + 
            info.size + SPLITER + 
            (int)info.type + SPLITER + 
            info.hash;
    }
}
