# Comic Reader UWP · 个人优化版

> 基于 [aicd0/ComicReaderUWP](https://github.com/aicd0/ComicReaderUWP) **v3.5.0** 的个人修改版，**非官方版本**。
> 原项目是用 WinUI 3 / .NET 写的本地漫画阅读器，支持文件夹、压缩包（zip/rar/7z/cbz/cbr）、PDF、书库管理、标签评分等。

## 这个版本改了什么

### 1. 多帧动图（WebP / GIF）加载大幅提速

原实现有两个瓶颈：打开一张动图时会把**所有帧一次性解码并上传显存**，且逐帧解码用的是随机帧索引——`SKCodec.GetPixels(frameIndex)` 每次都会把前面所有帧重新解一遍，复杂度 O(N²)。

实测（540×788、123 帧、14MB 的 WebP，机械硬盘）：

| | 首帧可见 | 全部 123 帧解码完 |
|---|---|---|
| 修改前 | 9 ms | **71.9 秒** |
| 修改后 | 9 ms | **1.12 秒** |

改动：

- 首帧同步解码、立即显示，其余帧在后台**顺序**解码（复用同一块像素缓冲 + `priorFrame`），一次线性扫描代替 N² 次重复解码
- 解码前不再通过 `LoadImageMeta` **二次打开同一个文件**，改为从已打开的流里读帧数，机械盘上省掉一轮寻道
- 本地文件流改为 1MB 缓冲 + `SequentialScan`，连续翻页更顺
- 未解码的帧先回退到最近的已解码帧，绘制永远不会被解码阻塞

### 2. 书库 / 合集页支持卡片缩放

- 主页与合集二级页右上角新增缩放滑块（0.6x – 2.0x），拖动即时生效，列数自动重排
- 缩放值全局共享并写入设置，下次启动保持；两个页面保持一致
- 卡片内封面改为弹性布局（封面占满剩余空间、文字区按自身高度），**放大时不会把文字或页数挤出卡片**

### 3. 修复「添加图片位置」窗口列表无法滚动

原来文件夹列表放在 `StackPanel` 里，纵向给的是无限高度，`ListView` 不认为自己被裁切——既没有滚动条，滚轮也无响应，路径一多下面就看不到了。改为三行 `Grid`（`Auto / Auto / *`）并把列表放进受限高度后，滚轮、滚动条、拖动全部正常。

## 下载与使用

到 [Releases](https://github.com/gws811-ctrl/ComicReaderUWP-optimization/releases) 下载 `ComicReaderUWP-portable-win64.zip`，解压后双击 `Start-ComicReaderUWP.bat`（或 `ComicReaderUWP.exe`）。

- **免安装、自带运行时**，不需要另行安装 .NET 或 Windows App SDK
- 首次启动是**全新空库**，需要自行「添加图片位置」
- 所有数据（书库、设置、缓存）都写在 exe 旁边的 `user\` 目录，**删掉它即可恢复出厂**
- 因为要往程序目录写数据，**不建议放在 `C:\Program Files`**（无写权限），放 D 盘/S 盘任意文件夹即可
- 仅提供 **x64** 版本，系统要求 Windows 10 1809 及以上

## 从源码构建

环境：

- Visual Studio 2022，勾选「.NET 桌面开发」与「WinUI 应用程序开发」工作负载，安装 Windows 11 SDK
- .NET 10 SDK

用 VS 直接打开 `src/ComicReaderUWP.sln` 生成即可。命令行构建便携版：

```bat
dotnet build src\ComicReaderUWP\ComicReaderUWP.csproj -c Release -p:Platform=x64 -p:IsPackaged=false
```

> `src/Directory.Build.props` 会自动探测 Visual Studio 的 AppxPackage 构建任务路径，解决纯命令行构建时的 MSB4062 报错；没有装 VS 的机器上它会自动跳过，不影响在 IDE 内构建。

## 说明

- 这是个人自用修改版，只针对自己遇到的三个问题做了改动，**不保证跟进上游更新**
- 已尽量不改动原有功能；书库、标签、评分、PDF、压缩包等逻辑保持原样
- 遇到问题欢迎提 Issue，但修复随缘 🙂

## 致谢与许可

- 原始项目：[aicd0/ComicReaderUWP](https://github.com/aicd0/ComicReaderUWP)，作者 **aicd0**
- 本项目遵循原项目的 **MIT License**，原始版权声明保留在 `LICENSE.md` 中
- 若原作者认为此 fork 有不妥，请联系删除

---

## English summary

An **unofficial** fork of [aicd0/ComicReaderUWP](https://github.com/aicd0/ComicReaderUWP) v3.5.0 (WinUI 3 comic reader). Three changes:

1. **Animated WebP/GIF loading**: the original decoded every frame at once with random frame indexing (O(N²) in Skia). Now the first frame is decoded synchronously and the rest progressively in order (reused pixel buffer + `priorFrame`). Measured on a 123-frame 14 MB WebP: **71.9 s → 1.12 s**. Also removed a redundant second file open per page and added 1 MB / `SequentialScan` file streams.
2. **Card zoom for the library and collection pages**: 0.6x–2.0x slider, shared and persisted; the cover area is now flexible, so cards scale without clipping the title block.
3. **Fixed the "add image location" dialog**: the folder list was inside a `StackPanel`, so it never scrolled. Now a star-sized `Grid` row gives it a bounded height — wheel, scrollbar and drag all work.

Portable x64 build, self-contained, data stored in `user\` next to the executable. MIT licensed, original copyright retained.
