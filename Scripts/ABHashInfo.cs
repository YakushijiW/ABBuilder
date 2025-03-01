using System;
namespace ABBuilder
{
    [Serializable]
    public class ABHashInfo
    {
        public const char SPLITER = ',';
        public string abName;
        public string hash;
        public long size;
        public ABType type;
        public uint Encrypt;
        public bool IsEncrypted { get { return Encrypt > 0; } }

        public string ABFileName
        {
            get
            {
                if (string.IsNullOrEmpty(abName)) return null;
                if (IsEncrypted)
                    return abName + ABBuildConfig.VARIANT_AB_ENCRYPT;
                return abName + ABBuildConfig.VARIANT_AB;
            }
        }
        public static ABHashInfo Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            ABHashInfo info = new ABHashInfo();
            var parts = line.Split(SPLITER);
            if (parts.Length >= 5)
            {
                try
                {
                    info.abName = parts[0];
                    info.size = long.Parse(parts[1]);
                    info.type = (ABType)int.Parse(parts[2]);
                    info.hash = parts[3];
                    info.Encrypt = uint.Parse(parts[4]);
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
            return info.abName + SPLITER +
                info.size + SPLITER +
                (int)info.type + SPLITER +
                info.hash + SPLITER +
                info.Encrypt.ToString();
        }
    }
}