// Program.cs —— 入口
//
// 默认：显示桌宠。双击桌宠打开设置面板，面板关闭后回到桌宠。
//
// 带参数：命令行模式，便于脚本调用与自检。
//   verify                          校验当前安装
//   restore                         还原原始外观
//   apply --wall <图> [--restart]   直接应用
//   panel                           直接打开设置面板（不显示桌宠）
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MiMoSkin
{
    static class Program
    {
        static PetForm _pet;
        static ToolboxForm _tool;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                try { return RunCli(args); }
                catch (Exception e)
                {
                    Console.Error.WriteLine("✗ " + e.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            PetState st = PetState.Load(SkinConfig.DefaultPath(exeDir));

            _tool = new ToolboxForm();

            _pet = new PetForm();
            _pet.OpenPanelRequested += delegate { ShowToolbox(-1); };
            _pet.OpenToolRequested += delegate(object s, int toolIndex) { ShowToolbox(toolIndex); };
            _pet.ExitRequested += delegate { Application.Exit(); };

            if (st.PetX == int.MinValue) _pet.MoveToDefaultCorner();
            else _pet.RestorePosition(st.PetX, st.PetY);

            _pet.Show();

            Application.Run(_pet);
            return 0;
        }

        static void ShowToolbox(int toolIndex)
        {
            if (_tool == null || _tool.IsDisposed) _tool = new ToolboxForm();

            // 面板出现在桌宠旁边，保持在屏幕内
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            PetState st = PetState.Load(SkinConfig.DefaultPath(exeDir));

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int px = st.PanelX, py = st.PanelY;

            if (px == int.MinValue)
            {
                // 默认放在桌宠左侧，放不下就放右侧
                px = _pet.Left - _tool.Width - 12;
                if (px < wa.Left + 8) px = _pet.Right + 12;
                py = _pet.Top - 40;
            }

            px = Math.Max(wa.Left + 8, Math.Min(wa.Right - _tool.Width - 8, px));
            py = Math.Max(wa.Top + 8, Math.Min(wa.Bottom - _tool.Height - 8, py));

            _tool.Location = new Point(px, py);
            _tool.Show();
            _tool.BringToFront();
            _tool.Activate();
            // 气泡入口带页索引：直接切到对应工具
            if (toolIndex >= 0) _tool.SelectTool(toolIndex);
        }

        static int RunCli(string[] args)
        {
            string cmd = args[0].ToLowerInvariant();
            Action<string> log = delegate(string s) { Console.WriteLine(s); };

            string exeDir = MiMoApp.FindExeDir();
            if (string.IsNullOrEmpty(exeDir))
            {
                Console.Error.WriteLine("✗ 找不到 MiMo Desktop 安装位置");
                return 2;
            }
            string asar = MiMoApp.AsarPath(exeDir);

            if (cmd == "verify")
            {
                Console.WriteLine("asar: " + asar);
                PatchResult r = SkinPatcher.Verify(asar, log);
                Console.WriteLine("皮肤已注入: " + (SkinPatcher.IsPatched(asar) ? "是" : "否"));
                return (r.HashFailures == 0 && r.SizeMatches) ? 0 : 1;
            }

            if (cmd == "panel")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                ToolboxForm p = new ToolboxForm();
                p.Show();
                Application.Run(p);
                return 0;
            }

            if (cmd == "restore")
            {
                if (MiMoApp.RunningCount() > 0 && MiMoApp.IsInsideMiMo() && !HasFlag(args, "--force"))
                {
                    Console.Error.WriteLine("✗ 本程序正运行在 MiMo 内部，还原需要先关闭 MiMo，");
                    Console.Error.WriteLine("  这会同时关掉正在调用本程序的会话。");
                    Console.Error.WriteLine("  请改为在资源管理器里双击 MimoPet.exe，或显式加上 --force。");
                    return 3;
                }

                string err;
                bool was = MiMoApp.RunningCount() > 0;
                if (was && !MiMoApp.Stop(log, out err)) { Console.Error.WriteLine("✗ " + err); return 1; }
                SkinPatcher.Restore(asar, log);
                if (was) MiMoApp.Start(exeDir, log);
                return 0;
            }

            if (cmd == "apply")
            {
                string wall = GetOpt(args, "--wall");
                if (string.IsNullOrEmpty(wall) || !File.Exists(wall))
                {
                    Console.Error.WriteLine("✗ 请用 --wall <图片路径> 指定壁纸");
                    return 2;
                }

                if (MiMoApp.RunningCount() > 0 && MiMoApp.IsInsideMiMo() && !HasFlag(args, "--force"))
                {
                    Console.Error.WriteLine("✗ 本程序正运行在 MiMo 内部，写入壁纸需要先关闭 MiMo，");
                    Console.Error.WriteLine("  这会同时关掉正在调用本程序的会话。");
                    Console.Error.WriteLine("  请改为在资源管理器里双击 MimoPet.exe，或显式加上 --force。");
                    return 3;
                }

                string cfgPath = SkinConfig.DefaultPath(exeDir);
                SkinConfig cfg = SkinConfig.Load(cfgPath);
                cfg.WallpaperPath = wall;

                Wallpaper.Result wr = Wallpaper.Process(wall, cfg.MaxWidth);
                if (wr.Error != null) { Console.Error.WriteLine("✗ " + wr.Error); return 1; }
                log("✓ 图片 " + wr.Width + "×" + wr.Height + "，" + (wr.Jpeg.Length / 1024) + " KB");

                byte[] css = new System.Text.UTF8Encoding(false).GetBytes(CssBuilder.Build(cfg));

                string err;
                bool was = MiMoApp.RunningCount() > 0;
                if (was && !MiMoApp.Stop(log, out err)) { Console.Error.WriteLine("✗ " + err); return 1; }

                SkinPatcher.PatchInput inp = new SkinPatcher.PatchInput();
                inp.AsarPath = asar;
                inp.Css = css;
                inp.Wallpaper = wr.Jpeg;
                inp.Log = log;
                SkinPatcher.Patch(inp);

                cfg.Save(cfgPath);
                if (was || HasFlag(args, "--restart")) MiMoApp.Start(exeDir, log);
                return 0;
            }

            Console.Error.WriteLine("用法：");
            Console.Error.WriteLine("  MimoPet.exe                       显示桌宠");
            Console.Error.WriteLine("  MimoPet.exe panel                 直接打开设置面板");
            Console.Error.WriteLine("  MimoPet.exe verify                校验当前安装");
            Console.Error.WriteLine("  MimoPet.exe restore [--force]     还原原始外观");
            Console.Error.WriteLine("  MimoPet.exe apply --wall <图> [--restart] [--force]");
            return 2;
        }

        static string GetOpt(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return "";
        }

        static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
