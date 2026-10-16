<div align="center">
  <img src="assets/app-icon.png" alt="Codex 用量监控图标" width="96" height="96">
  <h1>Codex 用量监控</h1>
  <p><strong>C# WPF 原生版</strong> · Windows 桌面版 Codex 的紧凑任务栏额度浮窗</p>
</div>

这是一个面向 Windows 桌面版 Codex 的轻量常驻工具。它固定贴靠在主任务栏托盘图标左侧，以与任务栏相同的高度显示短周期额度和一周额度，不额外占用桌面工作区。

本项目不是 Codex CLI 的通用封装器或替代入口。程序会调用 Windows 桌面版 Codex 随附的本机 `codex.exe app-server`，用户侧用途仍是观察桌面版 Codex 的额度状态。

本项目由 OpenAI Codex 协助开发，但不是 OpenAI 官方项目。项目主要为个人自用，除严重或破坏性 bug 外，不承诺后续维护、兼容性支持或功能请求响应。仓库未提供开源许可证；公开可见不等于主动授予复用权利。

## 界面与功能

- 固定宽度 116px，高度直接采用 Windows 主任务栏的实际高度；三行等分。
- 固定在托盘图标左侧并保持置顶；任务栏位置、尺寸或托盘区变化后会自动重新贴靠。面板里的 **显示位置** 可以在「托盘左侧」和「任务栏最左端」之间切换（Windows 11 任务栏居中时用后者）；读不到托盘区域时退回任务栏最左侧。
- 没有卡片底：文字和横条直接画在任务栏上，像长在状态栏里；配色跟随系统「浅色/深色任务栏」设置自动切换（改主题后重启程序生效）。窗口不出现在任务栏应用列表。
- 右下角 6×6、3px 圆角的小方块是读取状态灯：绿色正常、黄色正在读取或数据变旧、灰蓝色等待首次读数、红色读取失败；单实例运行，不会创建重复窗口。
- 文字用等宽字体 `Consolas`（缺字体时回退 `Segoe UI`），数字宽度一致，三行尺寸统一。
- 刷新失败时保留最后一次有效读数，读不到的窗口对应横条为空、时间显示 `--:--`。
- codex CLI 自更新会把自己装进新的 `OpenAI\Codex\bin\<新哈希>\` 目录并删掉旧的；程序一旦发现记着的 `codex.exe` 路径消失就自动重新定位，不用重启（日志里记一条 `codex.exe re-resolved`）。

![原生版紧凑任务栏浮窗](assets/gui-overview.png)

三行的含义：

- **5H**：约 5 小时短周期额度的剩余百分比，用横条表示（颜色随阈值变绿/黄/红）。
- **WK**：一周额度窗口的剩余百分比，同样用横条表示。
- 第三行：`5H 的重置时刻 / WK 的重置倒计时`，例如 `15:33 / 5d 7h`。服务端未提供某个窗口时，对应位置显示 `--:--` 或 `reset --`，不会拿另一个窗口的数据代替。

**左键单击浮窗**：立即刷新一次（等价于面板里的"立即刷新"），正在读取时右下角状态灯变黄。**右键**打开面板：深色圆角卡片，自绘（不是系统菜单），每个账号一小段图表 —— 圆点 + 账号名、`5H` / `WK` 两条小进度条、各自剩余百分比和重置时间：

```
● easerlee          ← 绿点：当前登录的账号，永远排最上面
5H  ▓▓▓░░░░░░  19%   01:08        ← 横条按剩余百分比填充，颜色随阈值变绿/黄/红
WK  ▓▓▓▓▓▓░░░  84%   6d 22h
● esky7028          ← 灰点：已退出的账号，停在最后一次读数
```

**点面板外任意位置**（别的程序、任务栏、桌面）就关掉它。这一条是程序自己每 80ms 看一次鼠标实现的：浮窗是 `WS_EX_NOACTIVATE` 不激活窗口，WPF 自带的「点外面关闭」靠鼠标捕获，在这种窗口上根本不触发。

**账号记录**（多账号场景）：每次取数成功后，按 `tokens.account_id` 记下当前账号的 5H 百分比、周百分比和各自的刷新时间；换账号后旧账号的记录不再变化，保留的就是它被换走前最后一次读到的值。

- 账号名只取邮箱 `@` 前面的部分；**当前登录的账号排最上面，前面的指示灯是绿色**（和浮窗右下角状态灯同一套语义：绿 = 正常），已退出的账号是灰点。
- 已过刷新时间的窗口、以及**满额（100%）还没开始计时的窗口**都显示 `已重置`，横条留空，不再显示没有意义的旧百分比或那个不可信的重置时间。codex 的 5 小时窗口是**从第一次使用开始算**的，所以窗口没被动过时服务端给的重置时间也不可信。
- 排序：当前登录的账号恒排第一，其余按 `5H` 那一列的重置时刻 —— **谁先恢复可用谁靠上**。窗口已经重置、满额还没开始计时、以及没有重置时间的记录，都不拿旧时间参与比较（前两种当作"现在就能用"，排在有窗口在跑的账号前面；没有时间的排到最后）。真正并列时按最近读数。
- 记录文件：`%LOCALAPPDATA%\CodexQuotaMonitor\accounts.json`（明文邮箱，只有账号 ID／邮箱／百分比／时间，**不含任何 token**）。
- 账号身份只读自 `%USERPROFILE%\.codex\auth.json`，程序从不写入或刷新它。
- 精度是一个刷新间隔：记录的是"最近一次读到"的值，不是账号切换那一瞬间的值。

`设置` 里是：**刷新间隔**（1、5、15 分钟）、**立即刷新**、**显示位置**（左、右）、**开机自动启动**、**退出**。前三类里的选项和开关都是滑动式的：底槽用进度条的同一种底槽色，选中的那一块／滑钮用进度条的绿色，点另一项时滑块滑过去（所以改这几项不会关面板，能看见它动）。

- **显示位置**：`右`（默认）贴在通知区域左侧，`左` 贴到任务栏最左端。Windows 11 任务栏居中时最左端通常是空的，用 `左` 把浮窗放到那里；任务栏左对齐时保持 `右`，免得压住开始按钮。马上生效，写进 `display_side`。
- **开机自动启动**：滑动开关。在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 里登记／移除当前 exe，**开关位置取的是注册表实际状态**（写入失败时会弹回原位），不是这一下点击的意图。
- **退出**：退出窗口和后台进程。浮窗由 1 秒一次的贴靠检查自己跟着任务栏走，不需要手动重贴；移动 exe 后要重设开机启动项。

![原生版右键菜单](assets/right-click-menu.png)

## 安装与运行

推荐从 GitHub Releases 下载 `CodexQuotaMonitor-Setup-1.1.1.msi`，双击即可安装。安装向导支持修改安装目录，并提供以下选项：

- **Create a desktop shortcut**：创建桌面快捷方式，默认选中。
- **Launch Codex Quota Monitor**：安装完成后立即启动，在完成页显示，默认不选中。

安装包包含 .NET 运行时，目标电脑不需要另行安装 .NET Desktop Runtime。`1.1.1` 及之后的安装包支持检测并升级同一产品的旧版本。

也可以下载自包含的 `CodexQuotaMonitor.Wpf.exe` 直接运行。该 EXE 同样不依赖目标电脑预装 .NET。

当前发布产物未使用商业代码签名证书。Windows SmartScreen 可能对首次下载的 MSI 或 EXE 显示来源未知提示；请从本仓库的 Releases 页面获取文件，并自行核对发布来源。

运行前需要：

- Windows 10/11 x64。
- Windows 桌面版 Codex 已安装并已登录。
- 程序能够从桌面版安装位置或 `PATH` 找到 `codex.exe`；诊断时也可用 `--codex-exe` 显式指定。

## 查询原理与风险

额度查询会启动本机 `codex.exe app-server --listen stdio://`，再通过 JSON-RPC 调用 `account/rateLimits/read`。程序依据服务端返回的 `windowDurationMins` 识别额度窗口：约 `300` 分钟归入 **5H**，约 `10080` 分钟归入 **WK**，而不是假定 `primary` 一定代表 5H。

程序不读取 `auth.json` 中的 token，也不自行把账号数据发送到第三方服务。窗口标题里记录的是最近一次成功读取的本地时间，不是 OpenAI 服务端时间。

需要注意：

- `codex.exe app-server` 不是面向本项目承诺稳定性的公开接口；桌面版 Codex 更新后，方法名、字段或窗口类型可能变化。
- 读数依赖本机 Codex 的登录状态和网络；账号切换、登录失效或网络异常都可能使刷新失败。
- 服务端可能临时停用某类额度窗口，此时对应区域会显示不可用。
- 剩余百分比和重置时间来自 Codex 返回值，只适合作为日常参考。
- 本地日志可能包含文件路径和错误信息，不应直接提交到公开仓库。

## 本地设置

安装版默认把状态文件写入：

~~~text
%LOCALAPPDATA%\CodexQuotaMonitor\settings.json
%LOCALAPPDATA%\CodexQuotaMonitor\logs\codex_quota_monitor.log
~~~

从源码目录运行时，`Start-CodexQuotaMonitorNative.cmd` 会把状态保存在项目根目录。环境变量 `CODEX_QUOTA_MONITOR_NATIVE_HOME` 可显式覆盖状态目录。

`settings.example.json` 中的默认值：

~~~json
{
  "quota_interval": 180,
  "red_threshold": 15.0,
  "amber_threshold": 30.0,
  "display_side": "right"
}
~~~

## 从源码运行

源码构建需要 .NET 8 SDK。普通启动：

~~~cmd
Start-CodexQuotaMonitorNative.cmd
~~~

诊断和单次读取：

~~~cmd
Start-CodexQuotaMonitorNative.cmd --check
Start-CodexQuotaMonitorNative.cmd --once
~~~

支持的参数：

- **--check**：输出环境、状态目录和任务栏贴靠位置，不启动 GUI。
- **--once**：读取一次额度并输出 JSON，不启动 GUI。
- **--codex-home &lt;path&gt;**：指定 Codex home，默认使用 `%CODEX_HOME%` 或 `~/.codex`。
- **--codex-exe &lt;path&gt;**：指定 `codex.exe`。
- **--quota-interval &lt;seconds&gt;**：覆盖额度刷新间隔。

## 构建发布包

运行：

~~~powershell
pwsh -NoProfile -File .\Build-Release.ps1
~~~

脚本会生成：

~~~text
publish\win-x64-self-contained\CodexQuotaMonitor.Wpf.exe
publish\installer\CodexQuotaMonitor-Setup-1.1.1.msi
~~~

构建机需要 .NET 8 SDK，并会通过 NuGet 获取 WiX；WiX 只用于生成 MSI，终端用户不需要安装 WiX。

基础验证命令：

~~~cmd
dotnet build .\src\CodexQuotaMonitor.Wpf\CodexQuotaMonitor.Wpf.csproj -c Release
dotnet run --project .\tests\CodexQuotaMonitor.Tests\CodexQuotaMonitor.Tests.csproj -c Release
~~~

## 分支关系

本分支是 C# WPF 原生 Windows 版本，提供自包含 EXE、MSI、原生托盘和紧凑任务栏浮窗。同一仓库的 `python-tk` 分支保留 Python/Tk 实现：它更方便阅读和修改，但需要用户自行准备 Python，并通过 `python codex_quota_float.py` 运行。

## 公开仓库注意事项

`.gitignore` 已排除本地设置、日志和构建产物。公开提交前仍应确认没有包含：

- `settings.json`
- `logs/`
- `publish/`
- `src/**/bin/`、`src/**/obj/`
- `installer/**/bin/`、`installer/**/obj/`
- 个人路径、token、key、账户信息或运行日志
