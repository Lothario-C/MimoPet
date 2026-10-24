// MiMoApp.cs —— MiMo Desktop 的定位、关停、启动
//
// 关键约束（踩过的坑，别改）：
// 1) 启动 MiMo 时必须从子进程环境里剔除 ELECTRON_NO_ASAR / ELECTRON_RUN_AS_NODE /
//    MIMO_ELECTRON_NODE_HOST。MiMo 自己必须开启 asar 虚拟文件系统，否则 asar 里的
//    out/main/*-worker.mjs 全部加载失败，表现为启动后后台任务疯狂重试。
// 2) 关停用 PID 精确定位而不是 /IM 按映像名，避免误伤同名进程。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MiMoSkin
{
    public static class MiMoApp
    {
        public const string ExeName = "Xiaomi MiMo.exe";
        public const string ProcessName = "Xiaomi MiMo";

        /* ---------------------------- 定位安装 ---------------------------- */

        public static string FindExeDir()
        {
            // 1) 本 exe 就在 MiMo 目录里（或同级）
            string self = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string c = Path.Combine(self, ExeName);
            if (File.Exists(c)) return self;
            string parent = Path.GetDirectoryName(self);
            if (!string.IsNullOrEmpty(parent))
            {
                c = Path.Combine(parent, ExeName);
                if (File.Exists(c)) return parent;
            }

            // 2) 注册表卸载项（最可靠：跟安装位置走）
            string fromReg = FindFromRegistry();
            if (!string.IsNullOrEmpty(fromReg)) return fromReg;

            // 3) 正在运行的进程路径
            string fromProc = FindFromRunning();
            if (!string.IsNullOrEmpty(fromProc)) return fromProc;

            // 4) 常见安装位置
            string[] guesses = new string[]
            {
                @"C:\Program Files\Xiaomi MiMo",
                @"C:\Program Files (x86)\Xiaomi MiMo",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Xiaomi MiMo"),
            };
            for (int i = 0; i < guesses.Length; i++)
            {
                if (File.Exists(Path.Combine(guesses[i], ExeName))) return guesses[i];
            }

            return "";
        }

        static string FindFromRegistry()
        {
            string[] roots = new string[]
            {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };

            for (int h = 0; h < hives.Length; h++)
            {
                for (int r = 0; r < roots.Length; r++)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hives[h], RegistryView.Registry64))
                        using (RegistryKey uninst = baseKey.OpenSubKey(roots[r]))
                        {
                            if (uninst == null) continue;
                            string[] subs = uninst.GetSubKeyNames();
                            for (int i = 0; i < subs.Length; i++)
                            {
                                using (RegistryKey sub = uninst.OpenSubKey(subs[i]))
                                {
                                    if (sub == null) continue;
                                    string name = sub.GetValue("DisplayName") as string;
                                    if (name == null || name.IndexOf("MiMo", StringComparison.OrdinalIgnoreCase) < 0) continue;

                                    string loc = sub.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrEmpty(loc) && File.Exists(Path.Combine(loc, ExeName))) return loc;

                                    // DisplayIcon 常是 "C:\path\app.exe,0"
                                    string icon = sub.GetValue("DisplayIcon") as string;
                                    if (!string.IsNullOrEmpty(icon))
                                    {
                                        string p = icon;
                                        int comma = p.LastIndexOf(',');
                                        if (comma > 2) p = p.Substring(0, comma);
                                        p = p.Trim('"');
                                        if (File.Exists(p) && Path.GetFileName(p).Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                                            return Path.GetDirectoryName(p);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            return "";
        }

        static string FindFromRunning()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(ProcessName);
                for (int i = 0; i < ps.Length; i++)
                {
                    try
                    {
                        string p = ps[i].MainModule.FileName;
                        if (!string.IsNullOrEmpty(p) && File.Exists(p)) return Path.GetDirectoryName(p);
                    }
                    catch { }
                    finally { ps[i].Dispose(); }
                }
            }
            catch { }
            return "";
        }

        /* -------------------------- 托盘设置 --------------------------
         * MiMo 的 preferences.json 里 trayEnabled=true 时，点关闭窗口只会最小化到托盘，
         * 进程继续在后台跑 —— 此时发 CloseMainWindow 永远等不到进程退出。
         * 必须临时把它关掉，让关闭窗口真的结束进程；改完再恢复用户原来的设置。
         */
        static string PrefsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Xiaomi MiMo", "preferences.json");
        }

        public static bool? ReadTrayEnabled()
        {
            try
            {
                string p = PrefsPath();
                if (!File.Exists(p)) return null;
                JObject o = Json.Parse(File.ReadAllText(p, Encoding.UTF8)) as JObject;
                if (o == null) return null;
                JBool b = o.Get("trayEnabled") as JBool;
                return b == null ? (bool?)null : b.Value;
            }
            catch { return null; }
        }

        public static bool SetTrayEnabled(bool enabled)
        {
            try
            {
                string p = PrefsPath();
                if (!File.Exists(p)) return false;
                string raw = File.ReadAllText(p, Encoding.UTF8);
                JObject o = Json.Parse(raw) as JObject;
                if (o == null) return false;
                o.Set("trayEnabled", new JBool(enabled));
                // 保持原有缩进风格（tab），否则 MiMo 自己写回时会看不出差异
                File.WriteAllText(p, Json.Stringify(o), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        public static string AsarPath(string exeDir)
        {
            return Path.Combine(exeDir, "resources", "app.asar");
        }

        /* ---------------------------- 进程状态 ---------------------------- */

        public static List<int> RunningPids()
        {
            List<int> pids = new List<int>();
            try
            {
                Process[] ps = Process.GetProcessesByName(ProcessName);
                for (int i = 0; i < ps.Length; i++)
                {
                    try { pids.Add(ps[i].Id); } catch { }
                    finally { ps[i].Dispose(); }
                }
            }
            catch { }
            return pids;
        }

        public static int RunningCount() { return RunningPids().Count; }

        public static bool IsRunning() { return RunningCount() > 0; }

        /* ---------------------------- 关停 ---------------------------- */

        // 判断本进程是不是跑在 MiMo 进程树里（例如被 MiMo 的终端/技能调用）。
        // 这种情况下关停 MiMo 会把调用者自己一起杀掉，必须先提醒用户。
        public static bool IsInsideMiMo()
        {
            try
            {
                int pid = Process.GetCurrentProcess().Id;
                HashSet<int> mimo = new HashSet<int>(RunningPids());
                if (mimo.Contains(pid)) return true;

                // 沿父进程链向上找
                for (int depth = 0; depth < 12; depth++)
                {
                    int ppid = ParentPid(pid);
                    if (ppid <= 0 || ppid == pid) break;
                    if (mimo.Contains(ppid)) return true;
                    pid = ppid;
                }
            }
            catch { }
            return false;
        }

        static int ParentPid(int pid)
        {
            try
            {
                // .NET Framework 没有内置父进程 API，用 WMI 查询
                using (System.Management.ManagementObjectSearcher s =
                    new System.Management.ManagementObjectSearcher(
                        "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId=" + pid))
                {
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        object v = o["ParentProcessId"];
                        if (v != null) return Convert.ToInt32(v);
                    }
                }
            }
            catch { }
            return 0;
        }

        public static bool Stop(Action<string> log, out string error)
        {
            error = null;
            List<int> pids = RunningPids();
            if (pids.Count == 0) return false;

            log("· 检测到 MiMo Desktop 正在运行（" + pids.Count + " 个进程），正在关闭…");

            // 托盘模式开着时，关闭窗口只会缩到托盘、进程不退。
            // 临时关掉它，关闭窗口才会真正结束进程；记下原值，启动后恢复。
            bool trayWasOn = false;
            bool? tray = ReadTrayEnabled();
            if (tray == true)
            {
                trayWasOn = true;
                if (SetTrayEnabled(false)) log("· 已临时关闭「显示在系统托盘」（改完会自动恢复）");
            }

            // 只走「优雅关闭」：对所有带窗口的进程发关闭请求，然后耐心等待。
            //
            // 绝不能用 Kill 强杀。Electron 的单实例锁在进程被强杀时来不及释放，
            // 紧接着重启的新实例会以为已有实例在跑，于是请求那个已死的实例显示窗口
            // → 静默退出，最终只剩一个没有窗口的残留进程，点图标毫无反应，
            // 必须把所有进程都杀干净才能再打开。这是必须避免的故障。
            for (int i = 0; i < pids.Count; i++)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pids[i]))
                    {
                        if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                    }
                }
                catch { }
            }

            // 给足时间（最长 20 秒）让它自己退干净
            for (int i = 0; i < 200; i++)
            {
                if (RunningCount() == 0)
                {
                    if (trayWasOn) SetTrayEnabled(true);
                    log("✓ 已关闭");
                    return true;
                }
                System.Threading.Thread.Sleep(100);
            }

            // 仍不退 → 再发一次关闭请求（有些进程可能刚创建窗口）
            List<int> left = RunningPids();
            for (int i = 0; i < left.Count; i++)
            {
                try
                {
                    using (Process p = Process.GetProcessById(left[i]))
                    {
                        if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                    }
                }
                catch { }
            }
            for (int i = 0; i < 100; i++)
            {
                if (RunningCount() == 0)
                {
                    if (trayWasOn) SetTrayEnabled(true);
                    log("✓ 已关闭");
                    return true;
                }
                System.Threading.Thread.Sleep(100);
            }

            // 到这一步说明它不肯正常退出。不再强杀 —— 强杀会留下坏掉单实例锁，
            // 让 MiMo 之后无法正常启动，比「这次没改成」严重得多。
            if (trayWasOn) SetTrayEnabled(true);
            error = "MiMo Desktop 没有响应关闭请求。\n\n" +
                    "请手动关闭 MiMo（右下角托盘图标右键 → 退出），然后重新点击「应用」。\n" +
                    "（为避免破坏 MiMo 的启动状态，本程序不会强制结束它。）";
            return false;
        }

        /* ---------------------------- 启动 ---------------------------- */

        public static void Start(string exeDir, Action<string> log)
        {
            string exe = Path.Combine(exeDir, ExeName);
            log("· 正在启动 MiMo Desktop…");

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = exeDir;

            // 必须剔除：ELECTRON_NO_ASAR 会让 MiMo 里的 worker 全部加载失败
            psi.EnvironmentVariables.Remove("ELECTRON_NO_ASAR");
            psi.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
            psi.EnvironmentVariables.Remove("MIMO_ELECTRON_NODE_HOST");

            Process p = Process.Start(psi);
            if (p != null) p.Dispose();

            // 确认窗口真的出现了再算成功。
            // 只看「进程已启动」不够：Electron 有单实例锁，若上一个实例残留了锁，
            // 新实例会立刻静默退出，看起来像启动了其实没有窗口。
            for (int i = 0; i < 300; i++)
            {
                if (HasVisibleWindow()) { log("✓ 已启动"); return; }
                System.Threading.Thread.Sleep(100);
            }
            log("⚠ 进程已启动，但 30 秒内没等到窗口。若界面没出现，请手动打开 MiMo。");
        }

        public static bool HasVisibleWindow()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(ProcessName);
                bool found = false;
                for (int i = 0; i < ps.Length; i++)
                {
                    try { if (ps[i].MainWindowHandle != IntPtr.Zero) { found = true; break; } }
                    catch { }
                    finally { ps[i].Dispose(); }
                }
                return found;
            }
            catch { return false; }
        }

        // 有进程但没有窗口 = 上次被异常终止后留下的僵尸状态。
        // 这种状态点图标没反应，必须先清掉才能正常启动。
        public static bool IsZombieState()
        {
            return RunningCount() > 0 && !HasVisibleWindow();
        }

        // 清理僵尸进程。这是唯一允许强杀的场合：
        // 已经没有窗口，没有任何正常途径能让它退出了。
        public static void CleanupZombies(Action<string> log)
        {
            List<int> pids = RunningPids();
            if (pids.Count == 0) return;

            log("· 检测到 " + pids.Count + " 个没有窗口的残留进程，正在清理…");
            for (int i = 0; i < pids.Count; i++)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pids[i]))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }
            for (int i = 0; i < 50; i++)
            {
                if (RunningCount() == 0) break;
                System.Threading.Thread.Sleep(100);
            }
            log("✓ 已清理残留进程");
        }

        /* --------------------------- 重启 --------------------------- */

        public static void Restart(string exeDir, Action<string> log, out string error)
        {
            error = null;
            bool wasRunning = RunningCount() > 0;
            if (wasRunning)
            {
                if (!Stop(log, out error)) return;
            }
            else
            {
                log("· MiMo Desktop 未在运行");
            }
            Start(exeDir, log);
        }
    }
}
