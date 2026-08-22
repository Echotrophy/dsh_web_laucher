// ============================================================================
//  dsh-web-launcher.cs
//  DeepSeek Harness Web 启动器（托盘小工具）
//
//  功能：
//    1. 双击运行，无命令行窗口、无控制台驻留
//    2. 若 127.0.0.1:3080 已被占用：
//       - 占用者是 node/dsh 进程 → 自动"接管"，退出时可停止它
//       - 占用者是非 node 进程 → 不接管（退出不会误杀）
//    3. 否则以隐藏窗口方式启动 dsh web（node ...\@deepseek-ai\dsh\lib\bin.js web）
//    4. 轮询端口，就绪后托盘气泡提示 + 自动打开默认浏览器
//    5. 托盘图标常驻：黄色=启动中 / 绿色=运行中 / 红色=异常 / 灰色=已停止
//       右键菜单：打开界面 / 重新启动服务 / 设置… / 打开日志文件 / 退出（停止服务）
//    6. 单实例保护（重复双击只打开浏览器）；后台定时巡检，服务挂掉托盘变红
//    7. 退出/重启时：先杀自己拉起的进程树，再按端口清扫残留的 node 监听进程
//       （即使进程关系失联，也能停掉真正占用端口的 dsh 服务）
//
//  编译（Windows 自带 .NET Framework 的 csc.exe，无需安装任何东西）：
//      build.cmd
//
//  配置文件：优先 exe 同目录 config.json，其次 %LOCALAPPDATA%\dsh-web-launcher\config.json（可在托盘「设置…」中修改）
//  日志文件：默认 %LOCALAPPDATA%\dsh-web-launcher\dsh-web.log（config.json 的 logFile 可自定义）
//
//  目标框架：.NET Framework 4.x（C# 5 语法，兼容旧版 csc）
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace DSHWebLauncher
{
    // ------------------------------------------------------------------ 程序入口
    internal static class Program
    {
        private const string MutexName = "DSHWebLauncher_SingleInstance";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例在运行：按配置决定是否打开浏览器，然后本实例退出
                    Config cfg = Config.Load();
                    Log.Init(cfg.ResolveLogPath());
                    Log.Write("检测到已有实例，本实例退出");
                    if (cfg.AutoOpenBrowser) OpenBrowser(cfg.Url);
                    return;
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new LauncherForm());
                }
                catch (Exception ex)
                {
                    Log.Write("程序异常: " + ex);
                }
            }
        }

        public static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Log.Write("已在默认浏览器打开: " + url);
            }
            catch (Exception ex)
            {
                Log.Write("打开浏览器失败: " + ex.Message);
            }
        }
    }

    // ------------------------------------------------------------------ 配置
    internal sealed class Config
    {
        public string Host = "127.0.0.1";
        public int Port = 3080;
        public bool AutoOpenBrowser = true;
        public int StartTimeoutSeconds = 120;
        public int PollIntervalMs = 500;
        public string NodePath = "";
        public string DshBinPath = "";
        public string DshHome = "";
        public string LogFile = "dsh-web.log";
        public string[] ExtraArgs = new string[0];

        public string Url { get { return "http://" + Host + ":" + Port; } }

        /// <summary>当前配置的来源文件（设置保存时写回同一文件；无则保存到 %LOCALAPPDATA%\dsh-web-launcher\config.json）</summary>
        private string _sourcePath;

        /// <summary>读取配置：优先 exe 同目录 config.json（便携手动配置），其次 %LOCALAPPDATA%\dsh-web-launcher\config.json（设置窗口保存），否则使用默认值</summary>
        public static Config Load()
        {
            Config c = new Config();
            string exeCfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(exeCfg)) { c.ApplyJson(exeCfg); c._sourcePath = exeCfg; return c; }
            string appCfg = Path.Combine(AppDataConfigDir(), "config.json");
            if (File.Exists(appCfg)) { c.ApplyJson(appCfg); c._sourcePath = appCfg; return c; }
            return c;
        }

        private static string AppDataConfigDir()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local)
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.Combine(local, "dsh-web-launcher");
        }

        private void ApplyJson(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var ser = new JavaScriptSerializer();
                var map = ser.Deserialize<Dictionary<string, object>>(json);
                if (map == null) return;

                object v;
                if (map.TryGetValue("host", out v) && v != null && Convert.ToString(v).Length > 0) Host = Convert.ToString(v);
                if (map.TryGetValue("port", out v) && v != null) Port = Convert.ToInt32(v);
                if (map.TryGetValue("autoOpenBrowser", out v) && v != null) AutoOpenBrowser = Convert.ToBoolean(v);
                if (map.TryGetValue("startTimeoutSeconds", out v) && v != null) StartTimeoutSeconds = Convert.ToInt32(v);
                if (map.TryGetValue("pollIntervalMs", out v) && v != null) PollIntervalMs = Convert.ToInt32(v);
                if (map.TryGetValue("nodePath", out v) && v != null && Convert.ToString(v).Length > 0) NodePath = Convert.ToString(v);
                if (map.TryGetValue("dshBinPath", out v) && v != null && Convert.ToString(v).Length > 0) DshBinPath = Convert.ToString(v);
                if (map.TryGetValue("dshHome", out v) && v != null && Convert.ToString(v).Length > 0) DshHome = Convert.ToString(v);
                if (map.TryGetValue("logFile", out v) && v != null && Convert.ToString(v).Length > 0) LogFile = Convert.ToString(v);
                if (map.TryGetValue("extraArgs", out v) && v != null)
                {
                    var list = new List<string>();
                    var al = v as ArrayList;
                    if (al != null)
                    {
                        foreach (object o in al) if (o != null) list.Add(Convert.ToString(o));
                    }
                    else if (v is object[])
                    {
                        foreach (object o in (object[])v) if (o != null) list.Add(Convert.ToString(o));
                    }
                    ExtraArgs = list.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Write("读取 " + path + " 失败，使用默认配置: " + ex.Message);
            }
        }

        /// <summary>解析 node.exe 路径：配置 > PATH > 常见安装位置</summary>
        public string ResolveNodePath()
        {
            if (NodePath.Length > 0 && File.Exists(NodePath)) return NodePath;

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(';'))
            {
                if (dir.Length == 0) continue;
                string cand = Path.Combine(dir.Trim('"'), "node.exe");
                if (File.Exists(cand)) return cand;
            }

            string[] fallbacks = {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                @"%ProgramFiles%\nodejs\node.exe",
                @"%LOCALAPPDATA%\Programs\nodejs\node.exe"
            };
            foreach (string f in fallbacks)
            {
                string expanded = Environment.ExpandEnvironmentVariables(f);
                if (File.Exists(expanded)) return expanded;
            }
            return "node";
        }

        /// <summary>解析 dsh 的 bin.js：配置 > 自动探测（node 目录推导 / npm root -g / 常见位置）</summary>
        public string ResolveDshBin()
        {
            if (DshBinPath.Length > 0 && File.Exists(DshBinPath)) return DshBinPath;

            // 1) 由 node.exe 所在目录推导 npm 全局目录（自定义安装常见形态：<node目录>\node_global\node_modules）
            string nodeDir = Path.GetDirectoryName(ResolveNodePath());
            if (!string.IsNullOrEmpty(nodeDir))
            {
                string cand = Path.Combine(nodeDir, "node_global", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                if (File.Exists(cand)) return cand;
                cand = Path.Combine(nodeDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                if (File.Exists(cand)) return cand;
            }

            // 2) 通过 `npm root -g` 动态查询全局模块根目录（兼容任意 npm 全局路径配置）
            try
            {
                string npmRoot = RunNpmRoot();
                if (!string.IsNullOrEmpty(npmRoot))
                {
                    string cand = Path.Combine(npmRoot, "@deepseek-ai", "dsh", "lib", "bin.js");
                    if (File.Exists(cand)) return cand;
                }
            }
            catch { }

            // 3) 常见默认位置兜底
            string[] candidates = {
                @"%APPDATA%\npm\node_modules\@deepseek-ai\dsh\lib\bin.js",
                @"%ProgramFiles%\nodejs\node_modules\@deepseek-ai\dsh\lib\bin.js",
                @"%ProgramFiles%\nodejs\node_global\node_modules\@deepseek-ai\dsh\lib\bin.js",
                @"%LOCALAPPDATA%\Programs\nodejs\node_modules\@deepseek-ai\dsh\lib\bin.js"
            };
            foreach (string f in candidates)
            {
                string expanded = Environment.ExpandEnvironmentVariables(f);
                if (File.Exists(expanded)) return expanded;
            }
            return DshBinPath; // 找不到时原样返回，让日志报错更明确
        }

        /// <summary>执行 `npm root -g` 获取全局模块根目录（失败返回 null）</summary>
        private static string RunNpmRoot()
        {
            try
            {
                // 经 cmd.exe 调用：CreateProcess 无法直接启动 npm.cmd（.cmd shim），且需按 PATHEXT 解析
                var psi = new ProcessStartInfo("cmd.exe", "/c npm root -g");
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
                    string outp = p.StandardOutput.ReadToEnd();
                    return string.IsNullOrEmpty(outp) ? null : outp.Trim();
                }
            }
            catch { return null; }
        }

        /// <summary>解析 DSH_HOME：配置 > 环境变量 > %USERPROFILE%\.dsh</summary>
        public string ResolveDshHome()
        {
            if (DshHome.Length > 0) return DshHome;
            string env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
        }

        /// <summary>解析日志文件路径：config 中为含路径的值则按相对/绝对路径解析；否则放入 %LOCALAPPDATA%\dsh-web-launcher\，避免污染 exe 所在目录（如桌面）</summary>
        public string ResolveLogPath()
        {
            if (LogFile.IndexOf('\\') >= 0 || LogFile.IndexOf('/') >= 0)
            {
                return Path.IsPathRooted(LogFile)
                    ? LogFile
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFile);
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir;
            if (string.IsNullOrEmpty(local)) dir = AppDomain.CurrentDomain.BaseDirectory;
            else dir = Path.Combine(local, "dsh-web-launcher");
            try { Directory.CreateDirectory(dir); }
            catch { dir = AppDomain.CurrentDomain.BaseDirectory; }
            return Path.Combine(dir, LogFile);
        }

        /// <summary>把当前配置写回来源文件；无来源时保存到 %LOCALAPPDATA%\dsh-web-launcher\config.json（保持 exe 目录整洁）</summary>
        public bool Save()
        {
            try
            {
                string dir = _sourcePath != null ? Path.GetDirectoryName(_sourcePath) : AppDataConfigDir();
                string path = _sourcePath ?? Path.Combine(dir, "config.json");
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.Append("  \"host\": ").Append(JsonStr(Host)).AppendLine(",");
                sb.Append("  \"port\": ").Append(Port).AppendLine(",");
                sb.Append("  \"autoOpenBrowser\": ").Append(AutoOpenBrowser ? "true" : "false").AppendLine(",");
                sb.Append("  \"startTimeoutSeconds\": ").Append(StartTimeoutSeconds).AppendLine(",");
                sb.Append("  \"pollIntervalMs\": ").Append(PollIntervalMs).AppendLine(",");
                sb.Append("  \"nodePath\": ").Append(JsonStr(NodePath)).AppendLine(",");
                sb.Append("  \"dshBinPath\": ").Append(JsonStr(DshBinPath)).AppendLine(",");
                sb.Append("  \"dshHome\": ").Append(JsonStr(DshHome)).AppendLine(",");
                sb.Append("  \"logFile\": ").Append(JsonStr(LogFile)).AppendLine(",");
                sb.Append("  \"extraArgs\": [");
                for (int i = 0; i < ExtraArgs.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(JsonStr(ExtraArgs[i]));
                }
                sb.AppendLine("]");
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                _sourcePath = path;
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("保存 config.json 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>JSON 字符串转义（借助 JavaScriptSerializer 生成带引号的合法 JSON 字符串）</summary>
        private static string JsonStr(string s)
        {
            return new JavaScriptSerializer().Serialize(s ?? "");
        }
    }

    // ------------------------------------------------------------------ 日志
    internal static class Log
    {
        private static readonly object Lock = new object();
        private static string _path;

        public static void Init(string file)
        {
            lock (Lock)
            {
                _path = file;
                try
                {
                    // UTF-8 带 BOM：记事本/PowerShell 都能正确显示中文
                    File.AppendAllText(_path, "===== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 启动器启动 =====\r\n", new UTF8Encoding(true));
                }
                catch { }
            }
        }

        public static void Write(string msg)
        {
            lock (Lock)
            {
                if (_path == null) return;
                try
                {
                    File.AppendAllText(_path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n", new UTF8Encoding(true));
                }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------------ 主窗体（不可见，承载托盘图标与全部逻辑）
    internal sealed class LauncherForm : Form
    {
        private Config _cfg;
        private NotifyIcon _tray;
        private ToolStripMenuItem _miExit;
        private ToolStripMenuItem _miOpenUrl;
        private System.Windows.Forms.Timer _pollTimer;
        private System.Windows.Forms.Timer _healthTimer;
        private Process _proc;
        private bool _ownProcess;
        private bool _ready;
        private bool _starting;
        private bool _exiting;
        private DateTime _startedAt;
        private Icon _iconStart, _iconReady, _iconError, _iconStopped;

        public LauncherForm()
        {
            // 隐藏窗体：无边框、不进任务栏、完全不显示
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Opacity = 0;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(-32000, -32000);
            // 系统注销/关机时先清理，避免遗留孤儿 dsh 进程
            SystemEvents.SessionEnding += OnSessionEnding;

            _cfg = Config.Load();
            Log.Init(_cfg.ResolveLogPath());
            Log.Write("配置: url=" + _cfg.Url + " node=" + _cfg.ResolveNodePath() + " dshBin=" + _cfg.ResolveDshBin() + " dshHome=" + _cfg.ResolveDshHome());

            BuildTray();
            Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // 工具窗口样式（WS_EX_TOOLWINDOW）：不出现于 Alt+Tab 切换、任务栏与任务管理器的应用列表
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }

        // 窗体永不显示：仅作为托盘图标与消息循环的宿主（防止 Alt+Tab / 任务管理器出现幽灵窗口）
        protected override void SetVisibleCore(bool value)
        {
            if (!this.IsHandleCreated) this.CreateControl();
            base.SetVisibleCore(false);
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            Cleanup();
        }

        // ------------------------------------------------------------ 托盘
        private void BuildTray()
        {
            _iconStart = MakeCircleIcon(Color.Orange, Color.FromArgb(120, 80, 0));
            _iconReady = MakeCircleIcon(Color.LimeGreen, Color.FromArgb(0, 100, 0));
            _iconError = MakeCircleIcon(Color.Red, Color.FromArgb(120, 0, 0));
            _iconStopped = MakeCircleIcon(Color.Gray, Color.FromArgb(60, 60, 60));

            var menu = new ContextMenuStrip();
            _miOpenUrl = new ToolStripMenuItem("打开界面 (" + _cfg.Url + ")");
            _miOpenUrl.Click += delegate { Program.OpenBrowser(_cfg.Url); };
            var miRestart = new ToolStripMenuItem("重新启动服务");
            miRestart.Click += delegate { RestartServer(); };
            var miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += delegate { OpenSettings(); };
            var miLog = new ToolStripMenuItem("打开日志文件");
            miLog.Click += delegate { TryOpenLog(); };
            _miExit = new ToolStripMenuItem("退出（停止服务）");
            _miExit.Click += delegate { ExitApplication(); };
            menu.Items.Add(_miOpenUrl);
            menu.Items.Add(miRestart);
            menu.Items.Add(miSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miLog);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_miExit);

            _tray = new NotifyIcon();
            _tray.Icon = _iconStart;
            _tray.Text = "DSH Web：启动中…";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { Program.OpenBrowser(_cfg.Url); };

            UpdateExitMenuText();
        }

        private void SetTrayState(int state) // 0=启动中 1=运行中 2=已停止 3=异常
        {
            try
            {
                switch (state)
                {
                    case 1: _tray.Icon = _iconReady; _tray.Text = "DSH Web：运行中 " + _cfg.Url; break;
                    case 2: _tray.Icon = _iconStopped; _tray.Text = "DSH Web：已停止"; break;
                    case 3: _tray.Icon = _iconError; _tray.Text = "DSH Web：异常，请查看日志"; break;
                    default: _tray.Icon = _iconStart; _tray.Text = "DSH Web：启动中…"; break;
                }
            }
            catch { }
        }

        /// <summary>根据是否持有 dsh 进程，更新退出菜单文案</summary>
        private void UpdateExitMenuText()
        {
            if (_miExit != null)
                _miExit.Text = _ownProcess ? "退出（停止服务）" : "退出（未接管外部服务）";
        }

        // ------------------------------------------------------------ 启动流程
        private void Start()
        {
            if (PortOpen(_cfg.Host, _cfg.Port))
            {
                // 端口已被占用：若占用者是 node/dsh 进程则接管，退出时可停止它
                Process adopted = AdoptPortListener();
                if (adopted != null)
                {
                    Log.Write("端口 " + _cfg.Port + " 已有 node/dsh 进程 PID=" + adopted.Id + "，已接管，退出时可停止");
                    _proc = adopted;
                    _ownProcess = true;
                }
                else
                {
                    Log.Write("端口 " + _cfg.Port + " 已被占用，但不是可识别的 node/dsh 进程，未接管（退出不会停止它）");
                    _ownProcess = false;
                }
                _ready = true;
                _starting = false;
                SetTrayState(1);
                UpdateExitMenuText();
                if (_cfg.AutoOpenBrowser) Program.OpenBrowser(_cfg.Url);
                StartHealthTimer();
                return;
            }

            _starting = true;
            _ready = false;
            _startedAt = DateTime.Now;
            if (!StartDshProcess())
            {
                _starting = false;
                SetTrayState(3);
                _tray.ShowBalloonTip(6000, "DSH Web 启动器", "无法启动 dsh 进程，请查看日志文件", ToolTipIcon.Error);
                return;
            }

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = Math.Max(200, _cfg.PollIntervalMs);
            _pollTimer.Tick += PollTick;
            _pollTimer.Start();
            SetTrayState(0);
            UpdateExitMenuText();
            Log.Write("已启动 dsh 进程 PID=" + _proc.Id + "，等待 " + _cfg.Url + " 就绪…");
        }

        private bool StartDshProcess()
        {
            try
            {
                string node = _cfg.ResolveNodePath();
                string bin = _cfg.ResolveDshBin();
                if (!File.Exists(bin))
                {
                    Log.Write("找不到 dsh 入口文件: " + bin + "（请在 config.json 中配置 dshBinPath）");
                    return false;
                }

                StringBuilder args = new StringBuilder();
                args.Append(Quote(bin)).Append(" web");
                if (_cfg.Port != 3080) args.Append(" --port ").Append(_cfg.Port);
                if (_cfg.Host != "127.0.0.1") args.Append(" --host ").Append(_cfg.Host);
                foreach (string a in _cfg.ExtraArgs) args.Append(" ").Append(Quote(a));

                var psi = new ProcessStartInfo();
                psi.FileName = node;
                psi.Arguments = args.ToString();
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                // 工作目录用 dshHome（始终存在），避免启动器目录被移动/删除后 dsh 启动报 chdir 错误
                psi.WorkingDirectory = _cfg.ResolveDshHome();
                psi.EnvironmentVariables["DSH_HOME"] = _cfg.ResolveDshHome();

                // 每个进程用独立回调，回调内校验引用，避免"旧进程的 Exited 事件误操作新进程"
                Process proc = new Process();
                proc.StartInfo = psi;
                proc.EnableRaisingEvents = true;
                proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Log.Write("[dsh] " + e.Data); };
                proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Log.Write("[dsh!] " + e.Data); };
                proc.Exited += delegate { OnDshExited(proc); };

                if (!proc.Start()) { Log.Write("Process.Start 返回 false"); return false; }
                _proc = proc;
                _ownProcess = true;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("启动 dsh 进程失败: " + ex);
                return false;
            }
        }

        private void PollTick(object sender, EventArgs e)
        {
            if (_exiting) return;
            if (PortOpen(_cfg.Host, _cfg.Port)) { MarkReady(); return; }

            if (_proc != null && !ProcAlive(_proc))
            {
                StopPollTimer();
                _starting = false;
                SetTrayState(3);
                _tray.ShowBalloonTip(6000, "DSH Web 启动器", "dsh 进程启动后立即退出，请查看日志", ToolTipIcon.Error);
                return;
            }

            double elapsed = (DateTime.Now - _startedAt).TotalSeconds;
            if (elapsed > _cfg.StartTimeoutSeconds)
            {
                StopPollTimer();
                _starting = false;
                SetTrayState(3);
                _tray.ShowBalloonTip(8000, "DSH Web 启动器", "等待 " + _cfg.Url + " 就绪超时（" + _cfg.StartTimeoutSeconds + " 秒），请查看日志", ToolTipIcon.Error);
                return;
            }

            _tray.Text = "DSH Web：启动中… " + (int)elapsed + "s";
        }

        private void MarkReady()
        {
            if (_ready || _exiting) return;
            _ready = true;
            _starting = false;
            StopPollTimer();
            SetTrayState(1);
            Log.Write("服务就绪: " + _cfg.Url);
            _tray.ShowBalloonTip(4000, "DSH Web 启动器", "DeepSeek Harness 已就绪：" + _cfg.Url, ToolTipIcon.Info);
            if (_cfg.AutoOpenBrowser) Program.OpenBrowser(_cfg.Url);
            StartHealthTimer();
        }

        // ------------------------------------------------------------ 巡检
        private void StartHealthTimer()
        {
            if (_healthTimer != null) return;
            _healthTimer = new System.Windows.Forms.Timer();
            _healthTimer.Interval = 3000;
            _healthTimer.Tick += HealthTick;
            _healthTimer.Start();
        }

        private void HealthTick(object sender, EventArgs e)
        {
            if (_exiting) return;
            bool open = PortOpen(_cfg.Host, _cfg.Port);
            if (open)
            {
                if (!_ready) MarkReady();
                return;
            }

            if (ProcAlive(_proc))
            {
                SetTrayState(3); // 进程在但端口不通：异常
            }
            else
            {
                SetTrayState(2); // 进程没了（或非本进程托管）：已停止
            }
        }

        // ------------------------------------------------------------ 进程退出回调
        private void OnDshExited(Process proc)
        {
            if (_exiting) return;
            // 过期回调防护：重启/停止后旧进程的 Exited 事件仍会到达，
            // 只处理"当前正在托管"的那个进程，避免误改新进程的所有权状态
            if (!ReferenceEquals(proc, _proc)) return;
            try
            {
                this.BeginInvoke((MethodInvoker)delegate
                {
                    if (_exiting || this.IsDisposed) return;
                    if (!ReferenceEquals(proc, _proc)) return;
                    int code = -1;
                    try { code = proc.ExitCode; } catch { }
                    Log.Write("dsh 进程已退出 PID=" + proc.Id + " code=" + code);
                    _proc = null;
                    _ownProcess = false;
                    _ready = false;
                    StopPollTimer();
                    UpdateExitMenuText();
                    if (_starting)
                    {
                        _starting = false;
                        SetTrayState(3);
                        _tray.ShowBalloonTip(6000, "DSH Web 启动器", "dsh 启动失败或立即退出（code " + code + "），请查看日志", ToolTipIcon.Error);
                    }
                    else
                    {
                        SetTrayState(2);
                        _tray.ShowBalloonTip(5000, "DSH Web 启动器", "dsh 服务已停止", ToolTipIcon.Warning);
                    }
                });
            }
            catch { }
        }

        // ------------------------------------------------------------ 重启 / 退出
        private void OpenSettings()
        {
            using (var dlg = new SettingsDialog(_cfg))
            {
                // 主窗体隐藏，不传 owner 避免焦点/层级问题
                if (dlg.ShowDialog() != DialogResult.OK) return;
            }
            if (!_cfg.Save())
            {
                MessageBox.Show("保存 config.json 失败，请确认 exe 所在目录可写。", "dsh-web-launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (_miOpenUrl != null) _miOpenUrl.Text = "打开界面 (" + _cfg.Url + ")";
            Log.Write("设置已保存: " + _cfg.Url);
            if (MessageBox.Show("设置已保存。\n是否立即重启服务以生效？", "dsh-web-launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                RestartServer();
            }
        }

        private void RestartServer()
        {
            Log.Write("手动重启服务…");
            StopTimers();
            KillOwnProcess();
            _ready = false;
            _starting = false;
            Start();
        }

        private void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            Log.Write("用户选择退出");
            Cleanup();
            this.Close();
            Application.Exit();
        }

        private void Cleanup()
        {
            try
            {
                StopTimers();
                KillOwnProcess();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
            }
            catch { }
        }

        private void StopTimers()
        {
            StopPollTimer();
            if (_healthTimer != null) { try { _healthTimer.Stop(); } catch { } _healthTimer = null; }
        }

        private void StopPollTimer()
        {
            if (_pollTimer != null) { try { _pollTimer.Stop(); } catch { } _pollTimer = null; }
        }

        private void KillOwnProcess()
        {
            Process target = _proc;
            bool owned = _ownProcess;
            // 先摘除引用，防止被杀的进程触发 Exited 回调影响后续状态
            _proc = null;
            _ownProcess = false;

            if (target != null && owned)
            {
                bool alive = true;
                try { alive = !target.HasExited; } catch { alive = true; } // 查询失败时按存活处理，尝试强杀
                if (alive)
                {
                    Log.Write("停止 dsh 进程树 PID=" + target.Id);
                    RunTaskKill(target.Id);
                    try { target.WaitForExit(8000); } catch { }
                }
            }

            // 端口清扫：即使进程关系失联（孤儿进程/接管的外部实例），
            // 也能停掉真正占用该端口的 node/dsh 服务
            KillPortListeners();
        }

        private static void RunTaskKill(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit(8000);
                }
            }
            catch (Exception ex)
            {
                Log.Write("taskkill " + pid + " 失败: " + ex.Message);
            }
        }

        /// <summary>杀掉占用配置端口的 node 进程（非 node 进程跳过，避免误杀）</summary>
        private void KillPortListeners()
        {
            List<int> pids = GetPortListenerPids(_cfg.Host, _cfg.Port);
            foreach (int pid in pids)
            {
                if (_proc != null && pid == _proc.Id) continue;
                if (pid == Process.GetCurrentProcess().Id) continue;
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        if (p == null) continue;
                        if (!IsNodeProcess(p))
                        {
                            Log.Write("端口 " + _cfg.Port + " 由非 node 进程 PID=" + pid + " 占用，跳过（避免误杀）");
                            continue;
                        }
                        Log.Write("停止端口 " + _cfg.Port + " 上的 node 进程 PID=" + pid);
                        RunTaskKill(pid);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 接管端口上已有的 node/dsh 进程，使"退出"能停止它。
        /// 注意：不为被接管进程附加 Exited 事件（EnableRaisingEvents 对已存在进程
        /// 可能因权限失败），存活状态交给端口巡检判断，退出时直接按 PID 停止。
        /// </summary>
        private Process AdoptPortListener()
        {
            List<int> pids = GetPortListenerPids(_cfg.Host, _cfg.Port);
            foreach (int pid in pids)
            {
                if (pid == Process.GetCurrentProcess().Id) continue;
                try
                {
                    Process p = Process.GetProcessById(pid);
                    if (p != null && IsNodeProcess(p)) return p;
                }
                catch { }
            }
            return null;
        }

        private static bool IsNodeProcess(Process p)
        {
            try
            {
                if (string.Equals(p.ProcessName, "node", StringComparison.OrdinalIgnoreCase)) return true;
                string path = p.MainModule != null ? p.MainModule.FileName : "";
                return path.Length > 0 && string.Equals(Path.GetFileName(path), "node.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>安全判断进程是否存活；查询失败时假定存活（端口巡检才是最终依据）</summary>
        private static bool ProcAlive(Process p)
        {
            try { return p != null && !p.HasExited; }
            catch { return true; }
        }

        private void TryOpenLog()
        {
            string logPath = _cfg.ResolveLogPath();
            try
            {
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Write("打开日志失败: " + ex.Message);
            }
        }

        // ------------------------------------------------------------ 工具方法
        private static bool PortOpen(string host, int port)
        {
            var client = new TcpClient();
            try
            {
                IAsyncResult ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(800)) return false;
                client.EndConnect(ar);
                return true;
            }
            catch { return false; }
            finally { try { client.Close(); } catch { } }
        }

        /// <summary>列出监听指定 host:port 的进程 PID（GetExtendedTcpTable，IPv4）</summary>
        private static List<int> GetPortListenerPids(string host, int port)
        {
            var result = new List<int>();
            try
            {
                uint addr = BitConverter.ToUInt32(IPAddress.Parse(host).GetAddressBytes(), 0);
                int size = 0;
                uint rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                if (rc != 0 && rc != ERROR_INSUFFICIENT_BUFFER) return result;

                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    rc = GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                    if (rc != 0) return result;

                    int count = Marshal.ReadInt32(buf);
                    IntPtr row = IntPtr.Add(buf, Marshal.SizeOf(typeof(int)));
                    int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                    for (int i = 0; i < count; i++)
                    {
                        MIB_TCPROW_OWNER_PID r = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(row, typeof(MIB_TCPROW_OWNER_PID));
                        row = IntPtr.Add(row, rowSize);
                        if (r.state == MIB_TCP_STATE_LISTEN && r.localAddr == addr)
                        {
                            ushort localPort = (ushort)IPAddress.NetworkToHostOrder((short)r.localPort);
                            if (localPort == (ushort)port && r.owningPid > 0 && !result.Contains(r.owningPid))
                                result.Add(r.owningPid);
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            return result;
        }

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint MIB_TCP_STATE_LISTEN = 2;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int TableClass, int Reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;        // 0
            public uint localAddr;    // 4
            public ushort localPort;  // 8
            public uint remoteAddr;   // 12
            public ushort remotePort; // 16
            public int owningPid;     // 20
        }

        private static string Quote(string s)
        {
            if (s.IndexOf(' ') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private static Icon MakeCircleIcon(Color main, Color ring)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(main)) g.FillEllipse(b, 2, 2, 12, 12);
                using (Pen p = new Pen(ring, 1f)) g.DrawEllipse(p, 2, 2, 12, 12);
                using (SolidBrush w = new SolidBrush(Color.FromArgb(160, 255, 255, 255))) g.FillEllipse(w, 6, 5, 4, 4);
            }
            IntPtr h = bmp.GetHicon();
            Icon icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            bmp.Dispose();
            return icon;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
            Cleanup();
            base.OnFormClosed(e);
        }
    }

    // ------------------------------------------------------------------ 设置窗口
    internal sealed class SettingsDialog : Form
    {
        private readonly Config _cfg;
        private TextBox _txtHost, _txtPort, _txtTimeout, _txtPoll, _txtNode, _txtBin, _txtHome, _txtExtra;
        private CheckBox _chkAutoOpen;

        public SettingsDialog(Config cfg)
        {
            _cfg = cfg;

            Text = "dsh-web-launcher 设置";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 372);

            const int xLabel = 16, xInput = 180, wInput = 300;
            int y = 14;
            AddLabel("监听地址 host", xLabel, y);
            _txtHost = AddText(cfg.Host, xInput, y, wInput);
            y += 32;
            AddLabel("端口 port", xLabel, y);
            _txtPort = AddText(cfg.Port.ToString(), xInput, y, wInput);
            y += 32;
            AddLabel("自动打开浏览器", xLabel, y);
            _chkAutoOpen = new CheckBox { Checked = cfg.AutoOpenBrowser, Location = new Point(xInput, y) };
            Controls.Add(_chkAutoOpen);
            y += 32;
            AddLabel("等待就绪超时（秒）", xLabel, y);
            _txtTimeout = AddText(cfg.StartTimeoutSeconds.ToString(), xInput, y, wInput);
            y += 32;
            AddLabel("就绪探测间隔（毫秒）", xLabel, y);
            _txtPoll = AddText(cfg.PollIntervalMs.ToString(), xInput, y, wInput);
            y += 32;
            AddLabel("node.exe 路径", xLabel, y);
            _txtNode = AddText(cfg.NodePath, xInput, y, wInput);
            y += 32;
            AddLabel("dsh 入口 bin.js 路径", xLabel, y);
            _txtBin = AddText(cfg.DshBinPath, xInput, y, wInput);
            y += 32;
            AddLabel("DSH_HOME", xLabel, y);
            _txtHome = AddText(cfg.DshHome, xInput, y, wInput);
            y += 32;
            AddLabel("附加参数（空格分隔）", xLabel, y);
            _txtExtra = AddText(string.Join(" ", cfg.ExtraArgs), xInput, y, wInput);
            y += 40;

            Controls.Add(new Label
            {
                Text = "提示：路径留空会自动探测；设置保存在 %LOCALAPPDATA%\\dsh-web-launcher\\config.json，不占用 exe 目录。",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(xLabel, y)
            });

            var btnOk = new Button { Text = "保存", Width = 88, Height = 28, Location = new Point(500 - 16 - 88 - 8 - 88, 372 - 44) };
            var btnCancel = new Button { Text = "取消", Width = 88, Height = 28, Location = new Point(500 - 16 - 88, 372 - 44) };
            btnOk.Click += delegate { if (TryApply()) { DialogResult = DialogResult.OK; Close(); } };
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y + 4) });
        }

        private TextBox AddText(string value, int x, int y, int width)
        {
            var tb = new TextBox { Text = value, Location = new Point(x, y), Width = width };
            Controls.Add(tb);
            return tb;
        }

        /// <summary>校验并写回配置；全部合法返回 true</summary>
        private bool TryApply()
        {
            string host = _txtHost.Text.Trim();
            int port, timeout, poll;
            if (host.Length == 0)
            {
                MessageBox.Show("监听地址不能为空。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!int.TryParse(_txtPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                MessageBox.Show("端口需为 1-65535 的整数。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!int.TryParse(_txtTimeout.Text.Trim(), out timeout) || timeout < 1)
            {
                MessageBox.Show("等待超时需为正整数（秒）。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!int.TryParse(_txtPoll.Text.Trim(), out poll) || poll < 1)
            {
                MessageBox.Show("探测间隔需为正整数（毫秒）。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _cfg.Host = host;
            _cfg.Port = port;
            _cfg.AutoOpenBrowser = _chkAutoOpen.Checked;
            _cfg.StartTimeoutSeconds = timeout;
            _cfg.PollIntervalMs = poll;
            _cfg.NodePath = _txtNode.Text.Trim();
            _cfg.DshBinPath = _txtBin.Text.Trim();
            _cfg.DshHome = _txtHome.Text.Trim();
            _cfg.ExtraArgs = SplitArgs(_txtExtra.Text);
            return true;
        }

        private static string[] SplitArgs(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new string[0];
            return s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
