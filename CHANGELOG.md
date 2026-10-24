# Changelog

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-10-24

首个公开版本。

### 新增

- 批量任务队列：一次粘贴多条链接，解析后逐行选择清晰度，按顺序下载。
- 每行独立的清晰度下拉框，支持「统一清晰度 + 应用到全部」。
- 未解析时点「开始下载」会先解析全部，等待用户确认清晰度。
- 支持平台：YouTube、TikTok、抖音、X（Twitter）、Instagram，以及 yt-dlp 覆盖的其它站点。
- 抖音：公开解析接口 + 官方无水印直链，无需登录。
- X：公开接口取多档 mp4 直链，敏感/纯视频推文可下；失败回退 yt-dlp。
- Instagram：DASH 多码率解析（1080×1920 / 720×1280）。
- 内置 yt-dlp 与 ffmpeg，单文件 exe，首次启动自动释放。
- 音视频自动合并为 MP4（可选）。
- 文件名带格式标识，避免同一视频不同清晰度互相覆盖。
- Cookie：Netscape 格式合并导入、按站点管理、在线检测登录态。
- 支持命令行参数直接解析：`VidGrab.exe "url1" "url2"`。

### 已知限制

- Windows 构建未做代码签名，SmartScreen 会提示未知发布者。
- 抖音、X 依赖公开接口，接口变动可能导致解析失败。
- Instagram 私密内容需要导入站点 Cookie。
- 未提供 H.264 转码；Instagram 输出为 VP9（现代播放器可正常播放）。

[1.0.0]: https://github.com/crinsgod/VidGrab/releases/tag/v1.0.0
