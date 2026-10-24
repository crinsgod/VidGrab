# 第三方组件与许可

VidGrab 的 exe 内置以下两个可执行文件，首次运行时释放到
`%LOCALAPPDATA%\VidGrab\bin`，并以**独立进程**方式调用：

## yt-dlp

- 项目：https://github.com/yt-dlp/yt-dlp
- 许可：Unlicense（公共领域）
- 用途：解析与下载 YouTube、TikTok、Instagram 等站点

## FFmpeg

- 项目：https://ffmpeg.org/
- 构建：BtbN/FFmpeg-Builds（win64 GPL 版）
- 许可：**GPLv3**（该构建启用了 GPL 组件）
- 源码：https://github.com/BtbN/FFmpeg-Builds
- 用途：音视频合并（`-c copy`），不做重编码

### GPL 合规说明

VidGrab 自身代码以 MIT 发布；FFmpeg 作为独立可执行文件随包分发，
两者通过命令行进程边界交互，不构成链接或派生作品。
随包提供 FFmpeg 的许可声明与源码获取地址；如需替换 FFmpeg，
直接替换 `%LOCALAPPDATA%\VidGrab\bin\ffmpeg.exe` 即可，程序不校验签名。

FFmpeg 完整许可文本见 https://ffmpeg.org/legal.html 。

## 界面与图片

项目内的截图、图标由本项目生成，随 MIT 许可发布。

## 免责

第三方组件的使用受其各自许可约束；本项目不对第三方组件的行为作任何担保。
