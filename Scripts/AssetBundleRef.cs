using System.Collections.Generic;
using UnityEngine;
namespace ABBuilder
{
    public class AssetBundleRef
    {
        public AssetBundle bundle;
        public ABType type;
        public List<AssetBundleRef> dependencies;
        public int touchCount = 0;
        public bool IsSingleAsset
        {
            get
            {
                return bundle.name.Contains('/');
            }
        }
        public string GetFirstAssetName()
        {
            return bundle.GetAllAssetNames()[0];
        }
        public AssetBundleRef(AssetBundle bundle)
        {
            this.bundle = bundle;
        }
    }
}