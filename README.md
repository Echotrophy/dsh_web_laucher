# dsh-web-launcher

Windows 系统托盘小工具，用于一键启动 **DeepSeek Harness Web**（`dsh web`）服务。

双击运行即可完成：**后台启动 dsh web → 检测 `127.0.0.1:3080` 就绪 → 自动打开默认浏览器**。全程无命令行窗口，托盘图标常驻显示服务状态。

## 功能特性

- **一键启动**：双击 exe 即以隐藏窗口方式拉起 `dsh web`，无控制台窗口残留；程序完全后台运行，不会出现于 Alt+Tab 切换与任务管理器
- **就绪探测**：轮询端口等待服务就绪，就绪后气泡提示并自动打开默认浏览器
- **托盘状态图标**：🟡 启动中 / 🟢 运行中 / 🔴 异常 / ⚪ 已停止
- **右键菜单**：打开界面、重新启动服务、设置、打开日志文件、退出（停止服务）
- **单实例保护**：重复双击不会重复启动服务，只打开界面
- **智能接管**：若端口已被 node/dsh 进程占用则直接接管（退出时一并停止）；若被其他程序占用则不接管，避免误杀
- **进程清理**：退出/重启时先结束自己拉起的进程树，再按端口清扫残留监听进程，不留孤儿进程
- **健康巡检**：后台定时检查服务状态，服务挂掉时托盘自动变红
- **内置设置窗口**：托盘右键「设置…」即可修改端口、超时、路径等参数，设置自动保存到 `%LOCALAPPDATA%\dsh-web-launcher\`，exe 目录保持整洁
- **高度可配置**：端口、超时、node/dsh 路径、DSH_HOME、附加参数等均可通过设置窗口或 `config.json` 调整

## 环境要求

| 依赖 | 说明 |
|---|---|
| 操作系统 | Windows 7 及以上（需 .NET Framework 4.5+，Windows 10/11 已内置） |
| Node.js | 已安装并可通过命令行调用（含 npm） |
| dsh | 已通过 npm 全局安装：`npm install -g @deepseek-ai/dsh` |

无其他依赖；编译工具链同样只需 Windows 自带的 .NET Framework `csc.exe`。

## 快速开始

1. 双击 `dsh-web-launcher.exe`（建议右键 → 发送到 → 桌面快捷方式）
2. 托盘出现 🟡 黄色圆点表示正在启动；变 🟢 绿色表示服务就绪，此时会气泡提示并自动打开浏览器
3. 用完想关闭服务：右键托盘图标 → **退出（停止服务）**，dsh 进程会被一并结束

> 如果 dsh 已经在运行（例如正通过其他方式启动着），双击工具不会重复启动，而是直接打开浏览器页面。

## 托盘图标与右键菜单

| 图标 | 含义 |
|---|---|
| 🟡 黄色 | 启动中（等待服务就绪） |
| 🟢 绿色 | 运行中（可正常访问） |
| 🔴 红色 | 异常（启动失败 / 端口无响应 / 进程闪退） |
| ⚪ 灰色 | 已停止 |

右键菜单项：

- **打开界面** — 在默认浏览器打开 `http://127.0.0.1:3080`
- **重新启动服务** — 杀掉当前 dsh 进程并重新拉起（用于卡死/异常恢复）
- **设置…** — 打开设置窗口，修改端口、超时、路径等参数，保存后可选立即重启生效（设置存放在 `%LOCALAPPDATA%\dsh-web-launcher\`，不占用 exe 目录）
- **打开日志文件** — 查看 `dsh-web.log`（默认在 `%LOCALAPPDATA%\dsh-web-launcher\`，dsh 的 stdout/stderr 也会写入该文件，便于排障）
- **退出（停止服务）** — 结束 dsh 进程树并退出工具

双击托盘图标 = 打开界面。

## 配置文件

配置读取优先级：**① exe 同目录 `config.json`（手动放置，便携优先）→ ② `%LOCALAPPDATA%\dsh-web-launcher\config.json`（设置窗口保存）→ ③ 默认值**。**绝大多数用户无需手动编辑配置文件**：托盘右键「设置…」即可修改端口、超时、路径等参数，设置自动保存到 `%LOCALAPPDATA%\dsh-web-launcher\`，不会在 exe 目录产生任何文件；高级用户仍可把 `config.json` 放在 exe 同目录手动编辑（优先级更高），只需写入想修改的字段，未填字段自动用默认值。仓库不提交个人 `config.json`。

| 字段 | 默认值 | 说明 |
|---|---|---|
| `host` | `127.0.0.1` | 监听地址 |
| `port` | `3080` | 端口（同时影响就绪轮询目标与 `--port` 启动参数） |
| `autoOpenBrowser` | `true` | 就绪后是否自动打开默认浏览器 |
| `startTimeoutSeconds` | `120` | 等待就绪的超时秒数 |
| `pollIntervalMs` | `500` | 就绪探测间隔（毫秒） |
| `nodePath` | 自动探测 | 启动 dsh 使用的 node.exe 路径（留空自动查找：PATH → 常见安装位置） |
| `dshBinPath` | 自动探测 | dsh 入口 bin.js 路径（留空自动查找常见 npm 全局安装位置） |
| `dshHome` | 环境变量或 `~/.dsh` | DSH_HOME，会传给 dsh 进程 |
| `logFile` | `dsh-web.log` | 日志文件名（默认存放于 `%LOCALAPPDATA%\dsh-web-launcher\`；填含路径的值则按相对/绝对路径解析） |
| `extraArgs` | `[]` | 附加给 `dsh web` 的命令行参数，如 `["--patch", "extra.yml"]` |

修改配置后，先退出托盘中的旧实例，再重新双击 exe 生效。

## 从源码构建

无需安装任何构建工具，使用 Windows 自带的 .NET Framework `csc.exe`：

```bat
build.cmd
```

双击 `build.cmd` 或在命令行执行即可，成功后在当前目录生成 `dsh-web-launcher.exe`。

发布时可使用 `package.cmd` 一键生成 Release 附件：

```bat
package.cmd
```

产物位于 `dist\` 目录（`dsh-web-launcher.exe`，单文件）。

## 文件结构

| 文件 | 说明 |
|---|---|
| `dsh-web-launcher.cs` | 全部源码（C# 5 语法，注释完整，目标框架 .NET Framework 4.x） |
| `build.cmd` | 一键编译脚本（零依赖） |
| `package.cmd` | 发布打包脚本：一键生成发布用 exe |
| `config.example.json` | 配置文件模板 |
| `DeepSeekHarness-WhaleGirl.ico` | 应用图标（编译时通过 `build.cmd` 嵌入 exe） |

以下文件为运行产物或本机个人文件，不纳入版本控制（见 `.gitignore`）：

| 文件 | 说明 |
|---|---|
| `dsh-web-launcher.exe` | 编译产物，由 `build.cmd` 生成 |
| `dsh-web.log` | 运行日志，自动生成（位于 `%LOCALAPPDATA%\dsh-web-launcher\`） |
| `config.json` | 本机个人配置（含本机绝对路径） |

## 常见问题

- **托盘图标不见了？** 点击任务栏右下角「^」展开隐藏图标，将 DSH Web 图标拖到可见区即可。
- **启动失败或超时？** 右键托盘图标 → 打开日志文件，查看 `[dsh]` / `[dsh!]` 开头的行；常见原因是端口被占用、路径配置错误或首次启动较慢（首次加载较耗时，可调大 `startTimeoutSeconds`）。
- **找不到 node / dsh？** 优先在 `config.json` 中显式配置 `nodePath` 与 `dshBinPath`；未配置时自动探测：node 从 PATH 与常见安装位置查找，dsh 从 node 所在目录的 `node_global\node_modules`、`npm root -g` 结果及常见默认位置查找。
- **修改端口后旧端口仍被占用？** 先右键退出工具，确认旧的 dsh 进程已结束（可在任务管理器中检查 node 进程）后再启动。

## 更新记录

- **v1.0.5** — 修复双击启动时浏览器连续打开两个标签页的问题：① OpenBrowser 跨进程 10 秒去重；② 启动 dsh 时加 `--no-open`，禁用 dsh 自身自动打开浏览器（由启动器统一控制）
- **v1.0.4** — 新增托盘右键「设置…」内置设置窗口，可修改端口、超时、路径等参数；设置保存到 `%LOCALAPPDATA%\dsh-web-launcher\config.json`，exe 目录保持整洁；发布包仅提供单 exe（设置功能已覆盖原配置版用途）
- **v1.0.3** — 修复 Alt+Tab / 任务管理器出现「无标题幽灵窗口」的问题（工具窗口样式 + 窗体永不显示）；新增 `package.cmd` 一键打包脚本
- **v1.0.2** — 日志默认写入 `%LOCALAPPDATA%\dsh-web-launcher\`，不再落在 exe 同目录（如桌面）
- **v1.0.1** — 增强 dsh 入口自动探测（node 目录推导 / `npm root -g` / 常见位置），直接下载 exe 也能自动定位 dsh
- **v1.0.0** — 首个版本

## 开源许可

本项目基于 [MIT License](LICENSE) 开源，可以自由使用、修改和分发，请保留版权声明。
