// 界面流程端到端测试（仅测试用）：真实创建 MainForm，用 ProgrammaticClick 走完整交互，
// 自动应答弹出的确认对话框，验证「快照 → 预览 → 一键整理 → 撤销」全链路。
// 用法：UiFlowTest.exe <outFile>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DeskTidy
{
    internal static class UiFlowTest
    {
        private static int _pass, _fail;
        private static readonly StringBuilder Sb = new StringBuilder();
        private static readonly List<string> Failures = new List<string>();

        private static void Check(string name, bool ok, string detail = "")
        {
            if (ok) { _pass++; Sb.AppendLine("  [PASS] " + name); }
            else
            {
                _fail++;
                Failures.Add(name + (detail.Length > 0 ? " :: " + detail : ""));
                Sb.AppendLine("  [FAIL] " + name + (detail.Length > 0 ? " :: " + detail : ""));
            }
        }

        private static void Eq(string name, object expected, object actual)
        {
            bool ok = (expected == null && actual == null) ||
                      (expected != null && expected.Equals(actual));
            Check(name, ok, "期望 " + expected + " 实际 " + actual);
        }

        // ---------------------------------------------------- 对话框自动应答
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder buf, int max);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWnd, EnumProc proc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder buf, int max);

        private const int BM_CLICK = 0x00F5;
        private static int _dialogCount;

        private static readonly string[] Affirmative =
            new string[] { "是(&Y)", "是(Y)", "是", "确定", "OK", "Yes", "&Yes" };

        /// <summary>应答最近弹出的消息框：点击肯定按钮（是 / 确定）。</summary>
        private static void DismissDialogs()
        {
            int me = System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                var cls = new StringBuilder(64);
                GetClassName(h, cls, 64);
                if (cls.ToString() != "#32770") return true;
                int pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid != me) return true;

                IntPtr hit = IntPtr.Zero;
                EnumChildWindows(h, delegate (IntPtr c, IntPtr l2)
                {
                    var ccls = new StringBuilder(64);
                    var ctxt = new StringBuilder(142);
                    GetClassName(c, ccls, 64);
                    if (ccls.ToString() != "Button") return true;
                    GetWindowText(c, ctxt, 142);
                    string t = ctxt.ToString();
                    foreach (string a in Affirmative)
                    {
                        if (t.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) { hit = c; return false; }
                    }
                    return true;
                }, IntPtr.Zero);

                if (hit != IntPtr.Zero)
                {
                    SendMessage(hit, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    _dialogCount++;
                }
                return true;
            }, IntPtr.Zero);
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

        private static object Field(object obj, string name)
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) throw new Exception("字段不存在: " + name);
            return f.GetValue(obj);
        }

        private static void Click(Control root, string text)
        {
            List<Control> hits = new List<Control>();
            FindAll(root, text, hits);
            if (hits.Count == 0) throw new Exception("找不到控件: " + text);
            Control target = hits[0];

            // Button.PerformClick 对不可见控件无效，先切到它所在的 TabPage
            SelectOwningTab(target);
            Pump(250);

            Button b = target as Button;
            if (b != null) { b.PerformClick(); Pump(400); return; }

            CheckBox c = target as CheckBox;
            if (c != null) { c.Checked = true; Pump(200); return; }

            throw new Exception("不是可点击控件: " + text);
        }

        /// <summary>把控件所在的 TabPage 切到前台，使控件可见。</summary>
        private static void SelectOwningTab(Control c)
        {
            for (Control p = c; p != null; p = p.Parent)
            {
                TabPage tp = p as TabPage;
                if (tp == null) continue;
                TabControl tc = tp.Parent as TabControl;
                if (tc != null) tc.SelectedTab = tp;
                break;
            }
        }

        private static void FindAll(Control root, string text, List<Control> hits)
        {
            foreach (Control c in root.Controls)
            {
                if (c.Text == text) hits.Add(c);
                FindAll(c, text, hits);
            }
        }

        private static int CountPlanRowsFor(DataGridView grid, string fileName, bool packState)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                PlanItem it = row.Tag as PlanItem;
                if (it == null || !string.Equals(it.Name, fileName, StringComparison.OrdinalIgnoreCase)) continue;
                object v = row.Cells["colPack"].Value;
                bool packed = v is bool && (bool)v;
                return packed == packState ? 1 : 0;
            }
            return -1;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string outFile = args.Length > 0 ? args[0] : "ui-flow.txt";
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string sandbox = Path.Combine(Path.GetTempPath(), "DeskTidyFlow_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string desktop = Path.Combine(sandbox, "Desktop");
            Directory.CreateDirectory(desktop);
            AppPaths.RootOverride = Path.Combine(sandbox, "Data");
            AppPaths.EnsureDirs();

            // 初始桌面：3 个文件 + 1 个文件夹
            File.WriteAllText(Path.Combine(desktop, "固定的快捷方式.lnk"), "lnk");
            File.WriteAllText(Path.Combine(desktop, "常用表格.xlsx"), "xlsx");
            File.WriteAllText(Path.Combine(desktop, "旧文档.txt"), "old");
            Directory.CreateDirectory(Path.Combine(desktop, "工作文件夹"));
            File.WriteAllText(Path.Combine(desktop, "工作文件夹", "内部.txt"), "inner");

            MainForm form = null;
            Timer watchdog = new Timer();
            watchdog.Interval = 150;
            watchdog.Tick += delegate { DismissDialogs(); };
            watchdog.Start();

            try
            {
                // 预置配置：指向沙箱桌面，关闭「整理后打开文件夹」以免弹出资源管理器
                AppConfig cfg = AppConfig.CreateDefault();
                cfg.DesktopPath = desktop;
                cfg.OpenFolderAfterTidy = false;
                cfg.Save();

                form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(0, 0);
                form.Show();
                Pump(700);

                DataGridView gridPlan = (DataGridView)Field(form, "_gridPlan");
                DataGridView gridSnap = (DataGridView)Field(form, "_gridSnap");
                Label lblSnapInfo = (Label)Field(form, "_lblSnapInfo");

                Sb.AppendLine("一、初始状态（未建快照）");
                Eq("预览行数 = 3 个文件（文件夹不计入）", 3, gridPlan.Rows.Count);
                Eq("快照表为空", 0, gridSnap.Rows.Count);
                Check("提示「尚未建立快照」", lblSnapInfo.Text.Contains("尚未建立快照"), lblSnapInfo.Text);
                Eq("未建快照时全部可打包", 3, CountChecked(gridPlan));

                Sb.AppendLine();
                Sb.AppendLine("二、建立快照");
                Click(form, "建立 / 更新快照");
                Eq("快照记录 3 个文件", 3, gridSnap.Rows.Count);
                Check("快照提示已更新", lblSnapInfo.Text.Contains("共 3 个固定文件"), lblSnapInfo.Text);
                DumpRows(gridPlan, "    [诊断] ");
                AppConfig live = (AppConfig)Field(form, "_cfg");
                Sb.AppendLine("    [诊断] ProtectSnapshot=" + live.ProtectSnapshot
                    + " Strict=" + live.StrictSnapshotMatch
                    + " DesktopPath=" + live.DesktopPath);
                Eq("已有文件全部变为保留", 0, CountChecked(gridPlan));

                Sb.AppendLine();
                Sb.AppendLine("三、桌面新增文件后刷新");
                File.WriteAllText(Path.Combine(desktop, "新下载1.zip"), "zip-content");
                File.WriteAllText(Path.Combine(desktop, "新截图.jpg"), "jpg-content");
                File.WriteAllText(Path.Combine(desktop, "临时缓存.tmp"), "tmp-content");
                Directory.CreateDirectory(Path.Combine(desktop, "新文件夹"));
                File.WriteAllText(Path.Combine(desktop, "新文件夹", "里层.txt"), "x");
                Click(form, "刷新");

                Eq("预览行数 = 6 个文件", 6, gridPlan.Rows.Count);
                Eq("只有 2 个新文件可打包（.tmp 被排除规则挡掉）", 2, CountChecked(gridPlan));
                Eq("旧文件仍为保留", 1, CountPlanRowsFor(gridPlan, "旧文档.txt", false));
                Eq("旧文件未被勾选打包", 1, CountPlanRowsFor(gridPlan, "旧文档.txt", false));
                Eq("新文件夹未被列出", 0, CountPlanRowsFor(gridPlan, "新文件夹", false) < 0 ? 0 : 1);

                Sb.AppendLine();
                Sb.AppendLine("四、手动取消勾选");
                SelectRow(gridPlan, "新下载1.zip");
                SetRowCheck(gridPlan, "新下载1.zip", false);
                Pump(200);
                Eq("取消后仅剩 1 个待打包", 1, CountChecked(gridPlan));
                SetRowCheck(gridPlan, "新下载1.zip", true);
                Pump(200);
                Eq("恢复勾选后 2 个待打包", 2, CountChecked(gridPlan));

                Sb.AppendLine();
                Sb.AppendLine("五、一键整理（确认对话框自动应答）");
                _dialogCount = 0;
                Click(form, "一键整理");
                Pump(900);
                Check("弹出了确认对话框", _dialogCount > 0, "dialogCount=" + _dialogCount);
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string targetDir = Path.Combine(desktop, today);
                Check("日期文件夹已建立", Directory.Exists(targetDir), targetDir);
                Check("新文件已移入日期文件夹", File.Exists(Path.Combine(targetDir, "新下载1.zip")));
                Check("新截图已移入日期文件夹", File.Exists(Path.Combine(targetDir, "新截图.jpg")));
                Check("固定文件仍在桌面", File.Exists(Path.Combine(desktop, "旧文档.txt")));
                Check("被规则排除的 .tmp 仍在桌面", File.Exists(Path.Combine(desktop, "临时缓存.tmp")));
                Check("文件夹仍在桌面", Directory.Exists(Path.Combine(desktop, "工作文件夹")));
                Check("新文件夹仍在桌面", Directory.Exists(Path.Combine(desktop, "新文件夹")));
                Check("文件夹内的文件未被移动", File.Exists(Path.Combine(desktop, "工作文件夹", "内部.txt")));

                DataGridView gridHistory = (DataGridView)Field(form, "_gridHistory");
                TabControl tabs = (TabControl)Field(form, "_tabs");
                tabs.SelectedIndex = 3;
                Pump(500);
                Eq("整理记录 1 条", 1, gridHistory.Rows.Count);
                Eq("记录状态为有效", "有效", gridHistory.Rows[0].Cells[3].Value);

                Sb.AppendLine();
                Sb.AppendLine("六、撤销");
                _dialogCount = 0;
                Click(form, "撤销选中的整理");
                Pump(900);
                Check("弹出了撤销确认对话框", _dialogCount > 0, "dialogCount=" + _dialogCount);
                Check("文件已回到桌面", File.Exists(Path.Combine(desktop, "新下载1.zip")));
                Check("文件已回到桌面（2）", File.Exists(Path.Combine(desktop, "新截图.jpg")));
                Pump(300);
                Eq("记录状态变为已撤销", "已撤销", gridHistory.Rows[0].Cells[3].Value);

                Sb.AppendLine();
                Sb.AppendLine("七、先试运行不改动文件");
                File.WriteAllText(Path.Combine(desktop, "试运行.txt"), "dry");
                Click(form, "刷新");
                CheckBox dry = (CheckBox)FindFirst(form, "先试运行（不移动文件）");
                dry.Checked = true;
                Pump(200);
                Click(form, "一键整理");
                Pump(700);
                Check("试运行后文件仍在桌面", File.Exists(Path.Combine(desktop, "试运行.txt")));
                Check("试运行未创建第二个日期文件夹",
                    Directory.GetDirectories(desktop, "20??-??-??*").Length == 1,
                    string.Join(",", Directory.GetDirectories(desktop)));
                dry.Checked = false;
                Pump(100);

                form.Close();
            }
            catch (Exception ex)
            {
                _fail++;
                Failures.Add("EXCEPTION :: " + ex);
                Sb.AppendLine("[异常] " + ex);
            }
            finally
            {
                watchdog.Stop();
                watchdog.Dispose();
                try { if (form != null && !form.IsDisposed) form.Dispose(); } catch { }
                try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); } catch { }
                Sb.AppendLine();
                Sb.AppendLine(string.Format("通过 {0} 项，失败 {1} 项。", _pass, _fail));
                if (Failures.Count > 0)
                {
                    Sb.AppendLine("失败明细：");
                    foreach (string f in Failures) Sb.AppendLine("  - " + f);
                }
                File.WriteAllText(outFile, Sb.ToString(), new UTF8Encoding(true));
                Console.WriteLine(Sb.ToString());
            }

            return _fail == 0 ? 0 : 1;
        }

        private static Control FindFirst(Control root, string text)
        {
            List<Control> hits = new List<Control>();
            FindAll(root, text, hits);
            return hits.Count > 0 ? hits[0] : null;
        }

        private static void DumpRows(DataGridView grid, string prefix)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                PlanItem it = row.Tag as PlanItem;
                if (it == null) continue;
                object v = row.Cells["colPack"].Value;
                Sb.AppendLine(prefix + it.Name + " | pack=" + v + " | decision=" + it.Decision
                    + " | choice=" + (it.UserChoice.HasValue ? it.UserChoice.ToString() : "null")
                    + " | " + it.Reason);
            }
        }

        private static int CountChecked(DataGridView grid)
        {
            int n = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                object v = row.Cells["colPack"].Value;
                if (v is bool && (bool)v) n++;
            }
            return n;
        }

        private static DataGridViewRow FindRow(DataGridView grid, string fileName)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                PlanItem it = row.Tag as PlanItem;
                if (it != null && string.Equals(it.Name, fileName, StringComparison.OrdinalIgnoreCase)) return row;
            }
            return null;
        }

        private static void SelectRow(DataGridView grid, string fileName)
        {
            DataGridViewRow row = FindRow(grid, fileName);
            if (row != null) grid.CurrentCell = row.Cells["colName"];
        }

        private static void SetRowCheck(DataGridView grid, string fileName, bool value)
        {
            DataGridViewRow row = FindRow(grid, fileName);
            if (row == null) throw new Exception("找不到行: " + fileName);
            row.Cells["colPack"].Value = value;
        }
    }
}
