# Windows 11 Search Icon State Fix

**Experimental preview · 0.1.0-preview.1 · MIT**

[English](#english) | [中文](#中文)

## English

An event-driven workaround for the Windows 11 taskbar Search icon occasionally staying colored even though the Search panel is closed.

The intended behavior is unchanged: a monochrome icon when Search is closed, and the normal colored icon when Search is open. The project does not force the icon to stay monochrome all the time.

### Status: please read first

**This is an experimental workaround, not a confirmed root-cause fix or a Microsoft patch.**

The original development build was compiled and run on Windows 11 Pro 25H2 ARM64, build 26200.8037, on a Xiaomi Pad 5. Normal open/close appearance and helper startup were checked. The intermittent wrong-color state did **not** recur during that session, so successful automatic recovery from the actual bug has **not yet been demonstrated**. The generic installer in this repository is an initial packaging of that prototype; broader installation and hardware coverage are not yet validated.

A [first-hand report on Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/5532857/windows-11-search-icon-turns-colored-after-waking) describes a similar symptom after waking from standby, including recovery when hovering over the icon. This is a user report, not Microsoft confirmation that every occurrence has the same cause.

### How it works

1. React to foreground-window changes, relevant shell visibility events, display-on/resume, session unlock, and taskbar recreation. There is no continuous periodic polling.
2. Locate the taskbar's `SearchButton` through UI Automation. Read the small area around the glyph in memory to detect the blue/green colored state.
3. Skip correction while the Search process has focus or a visible, uncloaked Search window exists. Also skip unavailable/off-screen taskbar controls, and allow time for a slow Search launch.
4. If the icon stays colored while Search is closed, send a local mouse-move/mouse-leave message to the taskbar control and request a redraw. The physical cursor is not moved; no clicks or typed keys are sent.
5. If that does not clear the color, briefly change the icon-only Search display mode from `1` to `0` and back to `1`, notifying the taskbar of the change. This fallback can cause a short taskbar layout shift. Other Search display modes are left alone by the fallback.

The helper does not patch system DLLs, inject code into Explorer, restart Explorer/Search, download anything, or change Windows Update, Defender, activation, or global PowerShell execution policies. It uses undocumented shell details such as the `SearchButton` automation ID and may need adjustment after Windows changes.

### Requirements and scope

- Windows 11 with the standard taskbar and **Search icon only** selected.
- Windows PowerShell **5.1** (`powershell.exe`), system .NET Framework and the ScheduledTasks module. PowerShell 7 (`pwsh.exe`) is not the build host.
- Run as the desktop user who will use the helper; it creates a limited-privilege logon task for that user. Organization policy may prevent task registration.
- ARM64 is the original development environment. The AnyCPU source is intended to be portable, but x64, other builds, custom themes, replacement taskbars, high-contrast modes and multi-monitor behavior are not yet validated.

### Download the compiled preview (no compilation)

Open [Releases](https://github.com/Mark8924/windows11-search-icon-fix/releases) and download **SearchIconStateFix.exe**, then double-click it. The optional portable ZIP includes the same EXE, a `Stop.cmd` helper, bilingual instructions and the license. `SHA256SUMS.txt` lists the download hashes.

The unsigned preview runs without a console or tray icon; check Task Manager for `SearchIconStateFix.exe`. Stop it with `SearchIconStateFix.exe --stop`, or double-click `Stop.cmd` from the ZIP. Portable use does not register a startup task. The system requirements and experimental status above still apply.

### Install from source

Download the repository ZIP using **Code → Download ZIP**, extract it, and open Windows PowerShell in that folder. Review the source and scripts first. If Windows marks the downloaded ZIP as blocked, use its **Properties → Unblock** before extracting, where permitted by your organization's policy.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Install.ps1
```

This compiles the C# source locally, installs it in `%LOCALAPPDATA%\SearchIconStateFix`, registers `SearchIconStateFix-<your Windows SID>` in Task Scheduler, and starts it immediately. No administrator privilege is requested by the task. The `RemoteSigned` option applies only to that PowerShell process, not to the machine's global policy.

Do not install alongside an earlier custom development build of this same workaround; remove that build's own startup task first to avoid running two helpers. This installer intentionally does not remove unrelated tasks.

### Build and run without installation

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Build.ps1
& .\build\SearchIconStateFix.exe
```

Running it directly does not add a startup task. To request a graceful stop:

```powershell
& .\build\SearchIconStateFix.exe --stop
```

The application runs without a console or tray icon. The in-session named mutex prevents duplicate instances.

### Uninstall

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "$env:LOCALAPPDATA\SearchIconStateFix\Uninstall.ps1"
```

This stops the helper and removes its current-user logon task. Files and logs are retained; the installation folder can then be deleted manually. There are no permanent theme or search-policy overrides to undo.

### Privacy, resource use and troubleshooting

- Only a small crop centered on the Search glyph is inspected in memory; screenshots are not saved.
- No application content, search queries, keystrokes, credentials or network traffic are recorded or sent anywhere.
- `%LOCALAPPDATA%\SearchIconStateFix\events.log` contains timestamps, helper state, repair attempts and exception descriptions. Review it before sharing; exception text can include local paths.
- Event-driven does not mean zero memory use: the prototype's working set was approximately **70–80 MiB** after loading .NET and UI Automation. CPU use depends on desktop activity; there is no fixed-rate screen capture loop.
- If the issue returns, record the Windows build, Search display mode, theme, trigger, whether the panel was actually closed, and the relevant log lines. Reports showing `stale colored icon`, the attempted refresh and whether `verified monochrome` follows are especially useful.

### License

[MIT](LICENSE). No affiliation with Microsoft. Initial implementation and documentation were developed with AI assistance and remain open to review and testing.

---

## 中文

这是一个由事件触发的自动纠正程序，用于缓解 Windows 11 任务栏搜索面板已经关闭、放大镜图标却偶尔仍保持彩色的问题。

目标是保留原本行为：关闭搜索时为单色图标，打开搜索时显示系统原本的彩色图标。它不会把图标强制永久改成黑白。

### 当前状态：实验性预览

**这是绕过方案，不是已经确认根因的修复，也不是微软补丁。**

原始开发版本已在小米平板 5 的 Windows 11 Pro 25H2 ARM64（26200.8037）中编译并运行，检查了正常打开、收起时的图标外观及辅助程序启动。测试期间没有再次遇到错误变色，因此**尚无实际故障触发后成功自动恢复的验证记录**。本仓库通用安装脚本是原型的初次整理，更广泛的安装和硬件兼容性仍待验证。

[微软社区的一手报告](https://learn.microsoft.com/en-us/answers/questions/5532857/windows-11-search-icon-turns-colored-after-waking)描述了相似现象：待机唤醒后图标错误变彩色，鼠标移上去后恢复。这是用户报告，不能据此断言所有同类现象的根因一致。

### 工作原理

1. 监听前台窗口切换、相关 Shell 窗口显示隐藏、亮屏/恢复、解锁及任务栏重建事件，不持续定时轮询。
2. 通过 UI Automation 定位任务栏 `SearchButton`，只读取图标中央的一小块像素，在内存里判断蓝绿色状态。
3. 搜索进程处于前台，或有可见且未被隐藏合成的搜索窗口时，跳过纠正。控件不可访问、离屏时也跳过，并为慢速打开搜索留出时间。
4. 关闭状态仍持续显示彩色时，向任务栏控件发送局部鼠标经过、离开消息并重绘；不移动真实光标，不点击，也不发送按键。
5. 如果仍未恢复，短暂把“仅搜索图标”模式从 `1` 改为 `0`，然后立即恢复 `1` 并通知任务栏。此回退方式可能导致任务栏短暂重新布局；其他搜索显示模式不会被它覆盖。

程序不修改系统 DLL，不向 Explorer 注入代码，不重启 Explorer 或搜索进程，不下载文件，也不更改更新、Defender、激活或全局 PowerShell 执行策略。它依赖 `SearchButton` 控件标识等未经微软正式保证的 Shell 细节，Windows 更新后可能需要调整。

### 环境要求与适用范围

- Windows 11 原生任务栏，搜索选择**仅显示搜索图标**。
- Windows PowerShell **5.1**（`powershell.exe`）、系统 .NET Framework 和 ScheduledTasks 模块；构建时不要使用 PowerShell 7 的 `pwsh.exe`。
- 用实际使用桌面的用户安装，任务以该用户的普通权限登录启动。组织策略可能限制任务创建。
- 最初在 ARM64 环境开发。源码编译为 AnyCPU，但 x64、其他系统版本、自定义主题、替代任务栏、高对比度及多显示器场景仍未验证。

### 下载已编译预览版（无需编译）

打开 [Releases](https://github.com/Mark8924/windows11-search-icon-fix/releases)，下载 **SearchIconStateFix.exe**，双击即可运行。也可下载便携 ZIP，里面包含同一个 EXE、停止程序用的 `Stop.cmd`、双语说明和许可证。`SHA256SUMS.txt` 提供下载文件的校验值。

预览程序未签名，没有控制台或托盘图标，可在任务管理器查看 `SearchIconStateFix.exe`。执行 `SearchIconStateFix.exe --stop` 或双击 ZIP 内的 `Stop.cmd` 停止。便携运行不会创建自启任务，前述系统要求和实验性状态仍适用。

### 从源码安装

在 GitHub 点击 **Code → Download ZIP**，解压后在目录中打开 Windows PowerShell。先检查源码和脚本。如果下载的 ZIP 被标记为阻止，可在组织策略允许的前提下，先通过 **属性 → 解除锁定** 再解压。

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Install.ps1
```

脚本会在本机编译源码，安装到 `%LOCALAPPDATA%\SearchIconStateFix`，创建名为 `SearchIconStateFix-<当前用户的 Windows SID>` 的登录任务，并立即启动。任务不要求管理员权限。命令中的 `RemoteSigned` 只作用于这次 PowerShell 进程，不修改全局策略。

如果以前装过本方案的自定义开发版，应先移除开发版自己的启动任务，避免两个辅助程序同时运行。本安装脚本不会删除无关任务。

### 只编译运行，不设自启

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Build.ps1
& .\build\SearchIconStateFix.exe
```

直接运行不会创建启动任务。停止程序：

```powershell
& .\build\SearchIconStateFix.exe --stop
```

程序没有控制台或托盘图标，通过会话内互斥锁避免重复运行。

### 卸载

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "$env:LOCALAPPDATA\SearchIconStateFix\Uninstall.ps1"
```

会停止辅助程序并删除当前用户的登录任务，保留文件和日志；之后可以自行删除安装目录。没有需要还原的永久主题或搜索策略修改。

### 隐私、开销与问题反馈

- 只在内存中检查搜索图标中央的小块区域，不保存截图。
- 不记录或上传应用内容、搜索词、按键、凭据或网络流量。
- `%LOCALAPPDATA%\SearchIconStateFix\events.log` 保存时间、程序状态、纠正尝试和异常描述。分享前请检查，异常文本可能包含本地路径。
- 事件触发不代表不占内存：原型加载 .NET 和 UI Automation 后的工作集约为 **70–80 MiB**。CPU 使用随桌面活动变化，没有固定频率的截屏循环。
- 如果再次出现问题，请提供系统构建号、搜索显示模式、主题、触发场景、面板是否确实关闭以及相关日志。尤其关注 `stale colored icon`、后续刷新，以及是否出现 `verified monochrome`。

### 许可证

[MIT](LICENSE)，与微软无隶属关系。初始实现和文档在 AI 辅助下完成，欢迎审查与测试。
