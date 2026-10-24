// ThinkPatch.cs —— 思考强度补丁集成
//
// 补丁器本体（think-patch.mjs + efforts.json）来自已验证成功的
// mimo-think-effort 项目，作为嵌入资源随桌宠分发，运行时释放到
// exe 同目录的 think-patch/ 下，用 node 执行。
//
// node 解析顺序：系统 node → MiMo 自带 node（需 MIMO_ELECTRON_NODE_HOST）。
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace MiMoSkin
{
    public static class ThinkPatch
    {
        public static string WorkDir
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "think-patch");
            }
        }

        static string PatchScript { get { return Path.Combine(WorkDir, "think-patch.mjs"); } }
        static string TablePath { get { return Path.Combine(WorkDir, "efforts.json"); } }

        // 释放/更新补丁器文件（幂等：内容变了才重写）
        public static string EnsureFiles()
        {
            try
            {
                if (!Directory.Exists(WorkDir)) Directory.CreateDirectory(WorkDir);
                ExtractIfChanged("MiMoSkin.think-patch.mjs", PatchScript);
                ExtractIfChanged("MiMoSkin.efforts.json", TablePath);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static void ExtractIfChanged(string resName, string dst)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resName))
            {
                if (s == null) throw new FileNotFoundException("资源缺失: " + resName);
                byte[] buf = new byte[s.Length];
                int got = 0;
                while (got < buf.Length)
                {
                    int n = s.Read(buf, got, buf.Length - got);
                    if (n <= 0) break;
                    got += n;
                }

                if (File.Exists(dst))
                {
                    byte[] old = File.ReadAllBytes(dst);
                    if (old.Length == got)
                    {
                        bool same = true;
                        for (int i = 0; i < got; i++) if (old[i] != buf[i]) { same = false; break; }
                        if (same) return;
                    }
                }
                File.WriteAllBytes(dst, buf);
            }
        }

        /* ------------------------------ node 解析 ------------------------------ */

        public static string FindNode()
        {
            // 1) 系统 node
            foreach (string c in new string[] {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "nodejs", "node.exe") })
            {
                if (File.Exists(c)) return c;
            }

            // 2) PATH 里找
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "where.exe";
                p.StartInfo.Arguments = "node";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                foreach (string line in outp.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.Length > 0 && File.Exists(t)) return t;
                }
            }
            catch { }

            // 3) MiMo 自带 node（安装目录 resources/runtimes 或 AppData）
            string mimoDir = MiMoApp.FindExeDir();
            if (!string.IsNullOrEmpty(mimoDir))
            {
                string[] cands = new string[] {
                    Path.Combine(mimoDir, "resources", "runtimes", "win32-x64", "node", "node.exe"),
                    Path.Combine(mimoDir, "resources", "runtimes", "win32-x64", "node", "mimo-node.exe"),
                };
                foreach (string c in cands) if (File.Exists(c)) return c;

                // 递归找（限深度）
                string found = FindFileBeneath(Path.Combine(mimoDir, "resources"), "node.exe", 4);
                if (found != null) return found;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string f2 = FindFileBeneath(Path.Combine(appData, "Xiaomi MiMo", "runtimes"), "node.exe", 5);
            if (f2 != null) return f2;

            return null;
        }

        static string FindFileBeneath(string dir, string fileName, int maxDepth)
        {
            if (!Directory.Exists(dir)) return null;
            try
            {
                foreach (string f in Directory.GetFiles(dir, fileName)) return f;
                foreach (string d in Directory.GetDirectories(dir))
                {
                    if (maxDepth <= 0) continue;
                    string r = FindFileBeneath(d, fileName, maxDepth - 1);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        // MiMo 自带 node 是启动器，需要宿主环境变量
        static void PrepareEnv(ProcessStartInfo psi)
        {
            string exeDir = MiMoApp.FindExeDir();
            if (!string.IsNullOrEmpty(exeDir))
            {
                string host = Path.Combine(exeDir, MiMoApp.ExeName);
                if (File.Exists(host))
                    psi.EnvironmentVariables["MIMO_ELECTRON_NODE_HOST"] = host;
            }
        }

        /* ------------------------------ 运行 ------------------------------ */

        public sealed class RunResult
        {
            public int ExitCode;
            public string Output;
            public bool Ok { get { return ExitCode == 0; } }
        }

        // action: apply | verify | undo | gen
        public static RunResult Run(string action, int timeoutMs)
        {
            RunResult r = new RunResult();
            try
            {
                string err = EnsureFiles();
                if (err != null) { r.ExitCode = 1; r.Output = "释放补丁器失败: " + err; return r; }

                string node = FindNode();
                if (node == null) { r.ExitCode = 1; r.Output = "找不到 Node.js（需要 18+，用于运行补丁器）"; return r; }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = node;
                psi.Arguments = "\"" + PatchScript + "\" " + action;
                psi.WorkingDirectory = WorkDir;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                PrepareEnv(psi);

                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                string so = p.StandardOutput.ReadToEnd();
                string se = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    r.ExitCode = 1;
                    r.Output = "补丁器超时（" + (timeoutMs / 1000) + "s）";
                    return r;
                }

                r.ExitCode = p.ExitCode;
                r.Output = (so + (se.Length > 0 ? "\n" + se : "")).Trim();
            }
            catch (Exception ex)
            {
                r.ExitCode = 1;
                r.Output = ex.Message;
            }
            return r;
        }

        // 快速状态：补丁是否在位（verify 的 exit code）
        public static bool IsApplied
        {
            get
            {
                RunResult r = Run("verify", 30000);
                return r.Ok;
            }
        }
    }
}
