using System.Collections.Generic;
using UnityEngine;

public class AssetBundleRef
{
    public AssetBundle bundle;
    public ABType type;
    public List<AssetBundleRef> dependencies;
    public AssetBundleRef(AssetBundle bundle)
    {
        this.bundle = bundle;
    }
}