# Windows 11 Search Icon State Fix

**Experimental preview 0.3.0 — adds visual rebuilding for real faults that survive an idle-frame reset.**

## Download and run

### [Download SearchIconStateFix.exe](https://github.com/Mark8924/windows11-search-icon-fix/releases/download/v0.3.0-preview.1/SearchIconStateFix.exe)

[Release details and checksums](https://github.com/Mark8924/windows11-search-icon-fix/releases/tag/v0.3.0-preview.1) · Windows 11 ARM64 / x64 · No commands or administrator rights required.

1. Run the EXE. It installs a current-user copy and opens the settings window.
2. Check **Start automatically when I sign in** if desired. Startup runs invisibly in the background, with no taskbar or notification-area icon.
3. **Hide** keeps monitoring invisibly, with no taskbar or notification-area icon. **Exit**, or closing the window, stops monitoring. Run the EXE or desktop shortcut again to reopen settings without starting another instance.

The UI follows the Windows display language (English / Chinese). Files and logs are stored in `%LOCALAPPDATA%\SearchIconStateFix`. Startup uses your current user's Run entry; the EXE can be moved out of Downloads after its first launch. It does not change execution policy or need a PowerShell startup script.

## What it does

Sometimes the Search panel is closed but its taskbar icon remains colored. This tool waits for a persistent mismatch, then restores the icon animation's idle frame. It reads the idle marker from the running Search visual rather than hardcoding a frame number.

**It never opens or closes Search to reset the icon, sends keyboard/mouse input, changes focus, changes Search settings, or replaces Windows files.**

Implementation: an event-driven C# monitor reads UI Automation state and examines a tiny in-memory image of the Search glyph. If needed, a matching ARM64/x64 native helper uses Windows XAML diagnostics to load a small bridge into Explorer. The bridge verifies Search is unchecked and first restores AnimatedVisualPlayer progress. If a follow-up glyph check still sees color, it clears and restores that player's unchanged animation source, rebuilding its rendering content, then sets the idle frame again. This fallback is limited to once per minute and never changes the Search button's checked state. This is an Explorer-process extension, not a standalone repaint trick. Exit unhooks the monitor and detaches the visual-tree observer; Windows may retain the DLL mapping until Explorer exits. There is no periodic polling loop, telemetry, or saved screenshot.

## Status and limitations

- **Controlled stale-frame repair and a real recurrence tested on Windows 11 25H2 ARM64.** In the real fault, restoring progress alone left the icon colored. Rebuilding the unchanged animation source restored monochrome at both 0.5 and 1.4 seconds; Search stayed closed and cursor/focus stayed unchanged.
- The GUI and startup setting are tested on that ARM64 device. x64 payloads are compiled, **not yet runtime tested**.
- The intermittent real-world issue's long-term recurrence rate is **not yet validated**. This preview does not claim a universal fix.
- Depends on Windows' Search XAML implementation. Other Windows builds, themes, or Search modes may be unsupported. Unsupported/unknown states are left alone; there is no disruptive fallback.
- Unsigned community executable. Review source and checksums before use.

## Build from source

On Windows, use Windows PowerShell 5.1:

```powershell
.\Build.ps1
```

Output: `build\SearchIconStateFix.exe`. The four `Native.*.gz` resources contain both architectures, so the EXE is self-contained and selects the native architecture at runtime. To rebuild these resources from `AnimationBridge.cpp`, use Python 3 and [llvm-mingw](https://github.com/mstorsjo/llvm-mingw):

```sh
python3 BuildNative.py --toolchain /path/to/llvm-mingw
```

No third-party Python packages or additional runtime redistributables are required. `Install.ps1` is optional; downloaded EXE users do not need scripts. `Uninstall.ps1` stops the tool and removes its startup entry, retaining logs/cache. After signing out, the cache folder can be deleted.

MIT license. Bug reports should include Windows build, CPU architecture, Search mode, and a description of the icon state; avoid posting personal screen contents.

---

# Windows 11 搜索图标状态修复

**实验性预览版 0.3.0：新增图标绘制内容重建，处理仅恢复动画进度仍无效的实际故障。**

## 下载和使用

### [直接下载 SearchIconStateFix.exe](https://github.com/Mark8924/windows11-search-icon-fix/releases/download/v0.3.0-preview.1/SearchIconStateFix.exe)

[版本说明与校验值](https://github.com/Mark8924/windows11-search-icon-fix/releases/tag/v0.3.0-preview.1) · 支持 Windows 11 ARM64 / x64 · 无需输入命令或管理员权限。

1. 双击 EXE，会安装到当前用户目录并打开设置界面。
2. 按需勾选“登录 Windows 时自动启动”；自启时纯后台运行，不显示窗口、任务栏或托盘图标。
3. “隐藏”会纯后台运行，不显示任务栏或托盘图标；“退出”或关闭窗口会停止运行。再次双击 EXE 或桌面入口可重新打开设置，不会重复启动。

界面随 Windows 显示语言选择中英文。程序和日志保存在 `%LOCALAPPDATA%\SearchIconStateFix`；自启使用当前用户的 Run 项，不更改 PowerShell 执行策略，也不需要启动脚本。

## 原理和边界

搜索窗口已经关闭，但图标偶尔仍停留在彩色画面。程序确认这种状态持续存在后，只把图标动画恢复到正常的未选中画面，**不会打开/关闭搜索来重置，也不会操作鼠标、键盘或焦点，不更改搜索设置和系统文件**。

实现使用 Windows XAML 诊断接口，按需把对应架构的小型桥接 DLL 加载到资源管理器中，检查实际搜索按钮未选中后先恢复动画进度；若复查仍为彩色，再清空并恢复同一个动画源对象，重建图标绘制内容，然后恢复关闭画面。重建最多每分钟一次，不改搜索按钮的选中状态。监测依靠事件，不使用持续轮询；只在内存检查搜索图标的一小块像素，不保存截图、不上传数据。退出后停止监测并取消观察；DLL 映射可能保留至资源管理器退出。

已在 Windows 11 25H2 ARM64 完成受控恢复及一次真实复现的修复验证：单改进度无效，重建图标后在 0.5 秒、1.4 秒复查均恢复黑色，搜索保持关闭，鼠标位置与焦点未变。界面和自启也已验证；**实际偶发问题是否长期不再出现，仍待验证**。x64 已编译，尚无实机验证。依赖 Windows 的搜索界面实现，不保证所有版本、主题和搜索模式有效；无法确认时跳过，不进行打断用户的后备操作。

这是未签名的开源程序。可先检查源码与校验值。编译方式见上方英文部分；`Uninstall.ps1` 用于退出并删除自启项，保留日志与缓存，注销后可删除缓存目录。MIT 许可证。

Icon generated using the built-in imagegen tool; source PNG and multi-size ICO are included. / 图标由内置 imagegen 生成，附 PNG 原图和多尺寸 ICO。

Icon sizing: `BuildIcon.py` (Python + Pillow) crops only transparent outer padding from the approved `AppIcon-source.png` and packs standard ICO sizes. The artwork is not regenerated. / 图标尺寸：`BuildIcon.py`（Python + Pillow）仅裁去已选原图外围透明边距，再封装为标准 ICO，不重新绘制图案。
