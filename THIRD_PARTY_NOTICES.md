# 来源与第三方说明

## 灵感来源

界面与交互设计参考了 Alejandro Buján 的 [Tendedero](https://github.com/alejandrobujan/tendedero)，
一个 macOS 上的 Swift / AppKit 应用：截图挂在屏幕顶部的一条绳子上，鼠标靠近才出现。

Tendedero 的**代码**以 MIT 许可发布，但它的**名称、图标和 `docs/` 下的图片另有使用限制**，
不属于 MIT 授权范围。

## 本项目的边界

- 本仓库是**独立的 C# / WPF 重写**，没有包含或翻译原项目的 Swift 源码。
- 没有使用原项目的名称（本项目叫「晾绳 Hangline」）。
- 没有使用原项目的图标和美术资源：托盘图标、界面里的绳子和卡片全部由代码绘制
  （见 `src/Hangline/IconFactory.cs`、`PegCard.cs`、`ScreenCapture.cs`）。
- 交互概念本身（截图挂到顶部、单击复制、拖拽分享）不受版权保护，这里做了独立实现；
  其中一部分还按 Windows 的习惯做了调整，差异见 README。

如果你的使用场景需要署名，请同时保留上面原项目链接和本说明。

## 运行依赖

- .NET 8 桌面运行时（Microsoft.WindowsDesktop.App）——系统级组件，不随仓库分发。
- **OpenCvSharp4**（NuGet）——长截图的帧间位移靠 ORB 特征点算，用了它。
  - OpenCvSharp 本体：Apache License 2.0，<https://github.com/shimat/opencvsharp>
  - 随包分发的 OpenCV 原生库（`OpenCvSharpExtern.dll`）：Apache License 2.0，
    见 <https://opencv.org/license/>
  - 两者都是宽松许可，保留署名即可，不要求本项目开源同等代码。原生库约 93 MB，
    由 NuGet 还原时带下来，不进仓库。
- 其余功能（读写剪贴板、注册表、已知文件夹、拖放、截图）都通过系统 API 完成，没有额外的库。
- 用「画图」（`mspaint.exe`）做长按编辑，那是 Windows 自带程序，不随仓库分发。
