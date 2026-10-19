# 晾绳 Hangline

截图挂到屏幕最上面的一条绳子上。鼠标不碰它就缩回去，屏幕上不留痕迹。

Windows 工具，照着 [Tendedero](https://github.com/alejandrobujan/tendedero)（macOS）的思路重写的独立实现：
**程序自己不抢你的截图键**，只负责把系统存下来的截图接住、挂起来、方便你取用。

<!-- 首图：想放演示图就把截图存成 docs/hero.png，然后去掉下面这行的注释
![晾绳](docs/hero.png)
-->

## 它怎么工作

Windows 把截图存在 `图片\Screenshots`，`PrtScn`、`Win+PrtScn`、`Win+Shift+S` 存的都在那儿。
晾绳盯着这个目录，**图一落盘就挂到绳子上**——你的快捷键、你习惯的截图工具，都不用改。

## 功能

- **接住系统截图**：出现即挂，不用手动导入
- **鼠标贴上屏幕沿**绳子滑下来，移开自动收回
- **一张图一个手势**：单击复制 · 双击打开 · 长按用画图编辑 · 拖出去发送 · 右键出菜单
- **自己也能截**：`Ctrl+Alt+A` 框选一块区域，截完挂上绳子并放进剪贴板
- **剪贴板里的图片**也挂（纯图片才接，带文字或文件的忽略）
- **全屏应用时自动隐藏**：看视频、玩游戏不挡路
- **额外监听目录**：把下载目录之类的也盯上
- **可接管系统截图目录**：让系统截图直接落到晾绳目录，桌面不乱（可随时还原）
- **长截图**：框选一块区域，自己滚页面，自动拼成一张长图
- **四种界面语言**：简体中文 / 繁體中文 / English / 日本語
- **不联网**：没有账号、没有上报，截图不出本机

## 用法

| 操作 | 效果 |
| --- | --- |
| 鼠标贴到屏幕上沿 | 绳子滑下来；移开就收回去 |
| 单击图片 | 复制到剪贴板（成功时卡片闪一下蓝边） |
| 双击图片 | 用默认看图程序打开 |
| 长按图片 | 丢进「画图」编辑，关掉画图后缩略图自动刷新 |
| 拖出图片 | 拖到文件夹就是拷一份，拖到聊天窗口就是发出去 |
| 右键图片 | 复制 / 打开 / 用画图编辑 / 在文件夹中显示 / 另存为 / 从绳子上取下 / 丢弃 |
| 图片右上角的 ✕ | 丢弃：从绳子上取下，文件**送回收站** |
| `Ctrl+Alt+A` | 框选截图，截完挂上绳子并复制到剪贴板 |
| `Ctrl+Alt+L` | 长截图：框选区域后自己滚页面，拼好挂上绳子 |
| `Ctrl+Alt+T` | 把绳子拉下来 |
| 托盘图标双击 | 临时拉下来 4 秒 |

右键菜单开着、或者正在拖图的时候，绳子不会收回去。

快捷键可以在托盘里换：托盘 →「截图快捷键」/「长截图快捷键」/「展开 / 收起快捷键」。

## 托盘菜单

| 菜单项 | 作用 |
| --- | --- |
| 展开 / 收起、固定显示 | 控制绳子 |
| 截图快捷键、长截图快捷键、展开 / 收起快捷键 | 换键，改完立刻生效 |
| 重新挂上最近的截图 | 把截图目录里最近的几张重新挂回来 |
| 打开截图文件夹 / 打开数据目录 | 打开对应目录 |
| 清空绳子 | 全部取下（自己截的图送回收站） |
| 剪贴板里的图片也挂上 | 开关 |
| 鼠标碰上沿就拉下来 | 开关 |
| 全屏应用时自动隐藏 | 开关 |
| 接管系统截图目录 | 开关（见下） |
| 额外监听目录 | 添加 / 打开 / 移除 |
| 语言 | 跟随系统 / 简体中文 / 繁體中文 / English / 日本語 |
| 开机启动 | 开关 |
| 退出 | 退出（会把接管的截图目录还回去） |

## 长截图

`Ctrl+Alt+L`：框出一块区域，然后自己滚页面。程序每 500 ms 抓一帧，把相邻两帧的纵向
位移算出来、拼成一张长图。点「完成」、按 Esc，或者画面停住 4 帧都会收工。

程序**不替你滚页面**。给别的程序灌滚轮消息，浏览器、Electron、自绘控件反应差别太大，
所以滚动交给用户，程序只负责拼。

帧间位移用 **ORB 特征点**投票算，不是逐行比像素：

- 每帧提特征点，相邻两帧做描述子匹配
- 只保留横向位移在 100 px 以内的匹配（同一处纹理不该横向跑偏）
- 每个匹配点给一个纵向位移，按位移值分桶投票
- **得票最多的那个位移**就是滚动量，不取最大位移——位移最大的点往往是错配

逐行签名在大量重复行（列表、代码、表格）上会锁错位置，接缝处出重影；特征点靠局部纹理，
重复行几乎不影响投票结果。画面完全没动时直接丢弃该帧，不追加。

上限是**高 30000 行、面积 1.5 亿像素**，到顶就停下提示。连续 11 帧对不上会明确报错
（提示回到中断处缓慢滚动），**不会硬拼一张烂图**。

匹配正确性由 `tools/matcher-harness` 用合成页面逐像素验证，改算法参数后跑一遍再提交。

## 数据放在哪

**跟在程序旁边**，不往 C 盘的 `AppData` 里堆：

```
dist\data\
  settings.json                    设置
  hangline.log                     日志
  hangline-32.ico                  托盘图标（代码画的）
  Shots\                           自己截的图
  Clipboard\                       从剪贴板接住的图
  Inbox\                           接管系统截图之后的落点
```

程序目录写不进去时（比如装在 `Program Files`）自动退回 `%APPDATA%\Hangline`。
出问题先看 `hangline.log`。

## 设置

数据目录下的 `settings.json`，托盘菜单改的是同一份：

```json
{
  "Pinned": false,
  "RevealOnEdge": true,
  "HideOnFullScreen": true,
  "WatchClipboard": true,
  "TakeOverScreenshotFolder": false,
  "ExtraFolders": [],
  "Language": "system",
  "HotkeyEnabled": true,
  "Hotkey": "Ctrl+Alt+T",
  "ShotHotkeyEnabled": true,
  "ShotHotkey": "Ctrl+Alt+A",
  "LongShotHotkeyEnabled": true,
  "LongShotHotkey": "Ctrl+Alt+L",
  "MaxItems": 12,
  "CardHeight": 128,
  "EdgeTriggerPx": 3
}
```

| 键 | 说明 |
| --- | --- |
| `MaxItems` | 绳子上最多挂几张，超了从最旧的开始掉 |
| `CardHeight` | 卡片高度（逻辑像素） |
| `EdgeTriggerPx` | 鼠标进屏幕上沿多少像素算"碰绳子" |
| `Language` | `system` 表示跟随系统 |

手动编辑时注意 JSON 里反斜杠要写成 `\\`，写坏了程序会退回默认值并在日志里记一笔。

## 接管系统截图目录

打开之后，Windows 的 `Screenshots` 已知文件夹会指向 `data\Inbox`，系统截图直接落进来，
桌面和图片目录都不会堆东西。默认关闭。

- 原来的位置会记在 `data\screenshots-folder-backup.txt`，**关掉开关或退出程序时原样还回去**
- 上次被强杀没还上，下次启动也会补还
- 想手动还原：注册表
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders`
  里把 `{b7bede81-df94-4682-a7d8-57a52620b86f}` 这一项删掉（回到系统默认），或改成你要的路径

## 构建

需要 [.NET SDK 8](https://dotnet.microsoft.com/download)。仓库里不带构建产物。

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

脚本会自己找装了 SDK 的 dotnet（PATH → `DOTNET_ROOT` → `Program Files` → `D:\dotnet`）。
SDK 装在别处就设一下环境变量 `DOTNET_ROOT`。

产物在 `dist\Hangline.exe`。默认不带运行时（目标机装了 .NET 8 桌面运行时即可）。
整包约 96 MB：其中 93 MB 是 OpenCvSharp 带下来的 OpenCV 原生库（`OpenCvSharpExtern.dll`
65 MB，外加一个 27 MB 的 ffmpeg 组件——长截图用不到，嫌大可以从
`dist\runtimes\win-x64\native\` 删掉）。

要拷到没装 .NET 的机器上：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -SelfContained
```

### 验证长截图的拼接

`tools/matcher-harness` 是匹配器的验证脚本：合成一页内容（含大片重复行），按已知步长
"滚动"它，算出每步位移，拼回去再跟原页逐像素比对；顺带检查发布目录里的原生库能不能真加载。

```powershell
cd tools\matcher-harness
dotnet run -c Release
```

输出里每项都应当是"逐像素一致"，最后打印"全部通过"。

## 代码结构

| 文件 | 作用 |
| --- | --- |
| `Program.cs` | 装配：托盘、监听、快捷键、退出 |
| `LineWindow.xaml(.cs)` | 贴着屏幕上沿的绳子，滑出与收起 |
| `PegCard.cs` | 一张图：缩略图、手势、右键菜单、动画 |
| `ScreenCapture.cs` | 框选截图：全屏遮罩、选区换算、裁图存盘 |
| `LongCapture.cs` | 长截图：抓帧循环、画布拼接、上限与失败判定 |
| `FrameMatcher.cs` | 相邻两帧的纵向位移：ORB 特征点匹配 + 位移投票 |
| `ClipboardImages.cs` | 剪贴板里的纯图片 |
| `ClipboardPayload.cs` | 往剪贴板放 PNG / DIB / TIFF / Bitmap 多份格式 |
| `ScreenshotWatcher.cs` | 盯截图目录（支持多个） |
| `ScreenshotFolder.cs` | 接管 / 还原系统的 `Screenshots` 已知文件夹 |
| `FullScreen.cs` | 判断前台是不是全屏应用 |
| `MessageWindow.cs` | 收全局快捷键和剪贴板通知的隐藏窗口 |
| `TrayIcon.cs` | 托盘图标与菜单 |
| `Localization.cs` | 界面文案，加语言只改这一个文件 |
| `ImageLoader.cs` | 读图、缩略图、内容指纹（识别同一张图的副本） |
| `Native.cs` | 用到的 Win32 调用 |
| `AppSettings.cs` / `Log.cs` | 设置与日志 |

## 已知限制

- 只在 Windows 10 / 11 上测过
- 多显示器可以跟着鼠标走，但**混合 DPI**（比如一块 100%、一块 150%）下的坐标换算没有充分验证
- 全屏隐藏靠"前台窗口是否盖满整块屏"判断，极少数独占全屏的游戏可能判不出来
- 剪贴板只接"纯图片"：和文字、文件一起复制的会被忽略（这是刻意的，避免把表格、文件图标当截图挂上来）
- 路径同名文件被覆盖时不会重新挂（截图工具一般不会覆盖同名文件）

## 来源与许可

界面和交互的设计灵感来自 Alejandro Buján 的 [Tendedero](https://github.com/alejandrobujan/tendedero)。
那是 macOS 上的 Swift / AppKit 实现；本仓库是**独立的 C# / WPF 重写**，不包含原项目的代码。

原项目源码是 MIT，但它的**名称、图标和文档图片不在 MIT 范围内**。所以这里用了自己的名字「晾绳 Hangline」，
托盘图标和界面里的图形全部是代码画出来的（见 `IconFactory.cs`），没有使用原项目的任何美术资源。

本仓库代码使用 [MIT 许可](LICENSE)。
