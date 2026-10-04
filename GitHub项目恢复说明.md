# 从 GitHub 恢复项目

大剑动画 FBX 已使用 Git LFS 管理，原 .meta、控制器、场景及其他资源保留在同一仓库。无需另外复制动画素材。

## 新电脑首次克隆

安装 Git（包含 Git LFS），然后执行：

```powershell
git lfs install
git clone https://github.com/1mcmn/UnityActionGame.git
cd UnityActionGame
git lfs pull
git lfs fsck
git status --short
```

正常克隆会自动下载 LFS 素材；git lfs pull 用于补全下载。fsck 检查 LFS 对象完整性，尚未打开引擎时 status 应为空。

## 已有克隆更新

先保留或提交自己尚未保存的修改，再执行：

```powershell
git lfs install
git pull --ff-only
git lfs pull
git lfs fsck
```

不要只依赖 GitHub 的 Download ZIP：未配置包含 LFS 对象时，压缩包可能只有指针文件。使用 git clone。

使用团结引擎 2022.3.62t10 打开项目，等待重新导入缓存；不要删除或重新生成已有 .meta。Library、Temp、本机编辑器设置和构建产物不属于仓库备份。

GitHub LFS 存储及下载额度按账号计算： https://docs.github.com/en/billing/concepts/product-billing/git-lfs 。动画包原始文件约 1.75 GiB，962 个 FBX 由 LFS 管理。历史提交不做改写。