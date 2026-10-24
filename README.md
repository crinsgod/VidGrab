# VidGrab

**Windows 单文件批量视频下载器** —— YouTube、TikTok、抖音、X（Twitter）、Instagram，一次粘贴多条链接，逐行选清晰度，按顺序下载。

[![Release](https://img.shields.io/github/v/release/crinsgod/VidGrab?style=flat-square)](https://github.com/crinsgod/VidGrab/releases)
[![License](https://img.shields.io/github/license/crinsgod/VidGrab?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square)](#)

![主界面](docs/images/main.png)

## 简介

VidGrab 把 yt-dlp 的能力包成一个 Windows 图形工具，并针对国内平台做了补充：

- **单文件 exe**：内置 yt-dlp 与 ffmpeg，首次启动自动释放，无需安装 Python / Node。
- **批量队列**：一行一个链接，解析后每个任务独立选清晰度，然后顺序下载。
- **不需要每条确认**：统一清晰度可一键应用到全部，也可以逐行微调。
- **抖音、X 有专用链路**：不依赖登录态；Instagram、YouTube 等走 yt-dlp。
- **Cookie 合并管理**：多站点 Cookie 存在一个文件里，按站点检测有效性、删除、清空。

## 功能

| 功能 | 说明 |
|---|---|
| 批量解析 | 一次粘贴多行链接或整段分享文案，自动提取 URL 并去重 |
| 清晰度选择 | 每行一个下拉框，显示宽×高、容器、编码、预估大小 |
| 统一设置 | 「统一清晰度」+「应用到全部」，按每行实际档位就近匹配 |
| 顺序下载 | 严格串行，一个完成再下一个；支持随时停止 |
| 音视频合并 | YouTube 等分离流自动调用内置 ffmpeg 合并为 MP4 |
| 文件名防冲突 | 文件名带格式标识，同一视频换清晰度不会覆盖或误跳过 |
| Cookie 导入 | Netscape 格式，多站点合并；支持在线检测登录态 |
| 平台适配 | 抖音无水印直链、X 多档 mp4 直链、Instagram DASH 多码率 |

## 支持平台

| 平台 | 解析方式 | 需要 Cookie |
|---|---|---|
| YouTube | yt-dlp | 可选（年龄限制内容需要） |
| TikTok | yt-dlp | 可选 |
| 抖音 | 公开解析接口 + 官方播放直链 | 不需要 |
| X（Twitter） | 公开接口多档 mp4 直链 | 不需要 |
| Instagram | yt-dlp（DASH） | 私密/受限内容需要 |
| 其它 | yt-dlp 支持的 1000+ 站点 | 视站点而定 |

## 下载

到 [Releases](https://github.com/crinsgod/VidGrab/releases) 下载最新 `VidGrab.exe`：

1. 双击运行（未签名，Windows 可能提示「未知发布者」，点「仍要运行」）。
2. 首次启动会在 `%LOCALAPPDATA%\VidGrab\bin` 释放 yt-dlp 与 ffmpeg。
3. 粘贴链接 → 解析全部 → 选清晰度 → 开始下载。

校验下载（可选）：

```powershell
Get-FileHash .\VidGrab.exe -Algorithm SHA256
```

## 使用

### 批量下载

1. 把链接粘到输入框，一行一个（抖音可直接粘整段分享文案）。
2. 点「解析全部」，表格里每行出现清晰度下拉框。
3. 逐行选，或顶部「统一清晰度」+「应用到全部」。
4. 点「开始下载」，按表格顺序执行。

不先解析也可以：直接点「开始下载」会先解析全部，停在待下载状态让你确认。

### Cookie（登录态）

适用：Instagram 私密帖、YouTube 年龄限制、X 敏感内容等。

1. 浏览器安装扩展 **Get cookies.txt LOCALLY**。
2. **登录目标站点后**，在目标页面点 Export / Export As，格式选 Netscape，得到 `cookies.txt`。
3. 程序里点「导入 Cookie…」，导入是**合并**，不会覆盖其它站点。
4. 对其它平台重复 2–3 步。

「管理…」里可以查看每个站点的 Cookie 数量、检测有效性、删除单个站点或清空。

```text
有效         = 登录态确认可用
本地通过     = 该站点不需要/不支持在线检测（如抖音）
未登录/已失效 = 需要重新导出
已过期       = 全部 Cookie 的过期时间已到
无法判断     = 被同意页/风控挡住，换时间再试
```

> Cookie 等同账号凭证，只保存在本机 `%APPDATA%\VidGrab\cookies.txt`，不会上传。

## 常见问题

**提示「yt-dlp 解析失败」**
站点改版导致，更新 `%LOCALAPPDATA%\VidGrab\bin\yt-dlp.exe`（删除后重启程序会重新释放），或到 [yt-dlp releases](https://github.com/yt-dlp/yt-dlp/releases) 下载覆盖。

**为什么文件名里有一串 `[269+251]`**
那是格式标识，用来避免同一视频不同清晰度互相覆盖。介意可以自己改模板（`src/YtDlp.cs`）。

**X 提示「这条推文没有视频」**
推文本身只有图片/文字，或被删除、受保护；引用推文请打开被引用的那条再解析。

**抖音解析失败**
抖音走的是公开解析接口，接口临时不可用时换个时间再试；其它平台不受影响。

**Windows 报毒 / SmartScreen**
exe 未做代码签名，且内置了 yt-dlp/ffmpeg，容易触发启发式告警。可自行从源码构建。

## 从源码构建

需要 Windows 10/11 + .NET Framework 4.8（系统自带 `csc.exe`）。

```powershell
git clone https://github.com/crinsgod/VidGrab.git
cd VidGrab
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

脚本会下载 yt-dlp 与 ffmpeg，压缩后编入 exe，输出到 `dist\VidGrab.exe`。
构建产物约 80 MB（ffmpeg 占大头）。

## 项目结构

```text
VidGrab/
├─ src/                 # C# 源码（WinForms，.NET Framework 4.8）
│  ├─ MainForm.cs       # 主界面与任务队列
│  ├─ YtDlp.cs          # yt-dlp 解析/下载封装
│  ├─ Douyin.cs         # 抖音解析与直链下载
│  ├─ XTwitter.cs       # X 公开接口解析
│  ├─ CookieStore*.cs   # Cookie 合并/管理
│  └─ CookieChecker.cs  # 登录态检测
├─ docs/images/         # 截图
├─ build.ps1            # 一键构建
└─ .github/workflows/   # CI / Release
```

## 第三方组件与许可

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) —— Unlicense
- [FFmpeg](https://ffmpeg.org/)（BtbN GPL build）—— GPLv3，源码见 [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds)
- 图标与界面素材为项目自有

VidGrab 以独立进程方式调用 ffmpeg，发布时随包附带其许可声明，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 免责声明

本项目仅用于个人学习与内容备份。请遵守目标平台的用户协议与当地法律法规，不要用于侵犯版权、绕过付费墙或传播他人隐私内容。使用者自行承担因下载、存储、传播内容产生的一切责任。

## License

[MIT](LICENSE)
