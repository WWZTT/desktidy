// 截图工具（仅测试用）：把 MainForm 的四个标签页渲染成 PNG，便于人工检查界面。
// 用法：UiShot.cs <输出目录>
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DeskTidy
{
    internal static class UiShot
    {
        private static object Field(object obj, string name)
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) throw new Exception("字段不存在: " + name);
            return f.GetValue(obj);
        }

        private static void Pump(int ms)
        {
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(20);
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string sandbox = Path.Combine(Path.GetTempPath(), "DeskTidyShot_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string desktop = Path.Combine(sandbox, "Desktop");
            Directory.CreateDirectory(desktop);
            AppPaths.RootOverride = Path.Combine(sandbox, "Data");
            AppPaths.EnsureDirs();

            string[] fixedFiles = { "工作日报模板.xlsx", "客户资料.pdf", "桌面整理助手.exe", "设计稿.ai" };
            foreach (string f in fixedFiles) File.WriteAllText(Path.Combine(desktop, f), new string('x', 4000));
            string[] newFiles = { "会议记录.docx", "产品图-最终版.jpg", "素材包.zip", "数据备份.cdr", "临时导出.tmp" };
            for (int i = 0; i < newFiles.Length; i++)
                File.WriteAllText(Path.Combine(desktop, newFiles[i]), new string('y', 1200 * (i + 1)));
            Directory.CreateDirectory(Path.Combine(desktop, "进行中的项目"));

            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DesktopPath = desktop;
            cfg.OpenFolderAfterTidy = false;
            cfg.Save();

            // 快照要在 MainForm 构造前落盘，构造函数才会读到它
            SnapshotData snap = SnapshotStore.Capture(desktop);
            SnapshotData trimmed = new SnapshotData();
            trimmed.DesktopPath = desktop;
            trimmed.CreatedLocal = snap.CreatedLocal;
            foreach (SnapshotEntry e in snap.Entries)
                if (Array.IndexOf(fixedFiles, e.Name) >= 0) trimmed.Entries.Add(e);
            SnapshotStore.Save(trimmed);

            MainForm form = new MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(20, 20);
            form.Show();
            Pump(800);

            TabControl tabs = (TabControl)Field(form, "_tabs");
            // 输出名用 ASCII，便于 README 在 GitHub 上稳定引用
            string[] names = { "01-preview", "02-snapshot", "03-settings", "04-history" };
            for (int i = 0; i < names.Length; i++)
            {
                tabs.SelectedIndex = i;
                Pump(700);
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(Path.Combine(outDir, names[i] + ".png"), ImageFormat.Png);
                }
                Console.WriteLine("已保存 " + names[i] + ".png  " + form.Width + "x" + form.Height);
            }

            form.Close();
            form.Dispose();
            try { Directory.Delete(sandbox, true); } catch { }
            return 0;
        }

        private static Control FindFirst(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                if (c.Text == text) return c;
                Control hit = FindFirst(c, text);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
