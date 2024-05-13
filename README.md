一个依据路径打包AssetBundle的Unity编辑器工具

以及包含ab包下载、更新、加载、检查完整性等功能的脚本

基本使用方法：
Editor->ABBuilder/CreateConfig->
Set config at path:[Assets/ABBuilder/BuilderConfig]->
ABBuilder/OneKeyBuild->
Copy output files to server path->
Set [remoteAddress]&[remotePort] fields value at script [AssetBundleManager] ->
Build your app and try (or test in sample scene)# ABBuilder
