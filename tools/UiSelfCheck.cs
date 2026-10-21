// 界面自检（仅测试用，不随 exe 发布）：真实实例化 MainForm，逐页切换并检查布局。
// 用法：UiSelfCheck.exe <outFile>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace DeskTidy
{
    internal static class UiSelfCheck
    {
        private static int _problems;
        private static readonly StringBuilder Sb = new StringBuilder();

        private static void Fail(string msg)
        {
            _problems++;
            Sb.AppendLine("    [问题] " + msg);
        }

        private static object Field(object obj, string name)
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) throw new Exception("字段不存在: " + name);
            return f.GetValue(obj);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string outFile = args.Length > 0 ? args[0] : "ui-check.txt";
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 用临时数据目录，避免污染真实配置
            string sandbox = Path.Combine(Path.GetTempPath(), "DeskTidyUiCheck_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            AppPaths.RootOverride = sandbox;
            Directory.CreateDirectory(sandbox);

            MainForm form = null;
            try
            {
                form = new MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(0, 0);
                form.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(500);
                Application.DoEvents();

                Sb.AppendLine("窗体标题: " + form.Text);
                Sb.AppendLine("ClientSize: " + form.ClientSize.Width + "x" + form.ClientSize.Height);
                Sb.AppendLine("MinimumSize: " + form.MinimumSize.Width + "x" + form.MinimumSize.Height);

                TabControl tabs = (TabControl)Field(form, "_tabs");
                Sb.AppendLine("标签页数: " + tabs.TabPages.Count);

                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(250);
                    Application.DoEvents();

                    TabPage page = tabs.TabPages[i];
                    Sb.AppendLine();
                    Sb.AppendLine("---------- [" + i + "] " + page.Text + " ----------");
                    DumpAndCheck(page, form);
                }

                // 关键控件检查
                Sb.AppendLine();
                Sb.AppendLine("关键控件检查：");
                CheckExists(form, "一键整理");
                CheckExists(form, "先试运行（不移动文件）");
                CheckExists(form, "全部勾选");
                CheckExists(form, "全部取消");
                CheckExists(form, "恢复自动判断");
                CheckExists(form, "建立 / 更新快照");
                CheckExists(form, "删除快照");
                CheckExists(form, "把当前桌面全部文件设为固定");
                CheckExists(form, "浏览...");
                CheckExists(form, "刷新");

                // 网格列宽检查
                tabs.SelectedIndex = 0;
                Application.DoEvents();
                DataGridView grid = (DataGridView)Field(form, "_gridPlan");
                Sb.AppendLine();
                Sb.AppendLine("整理预览表格：列 " + grid.Columns.Count + " 个，可视宽度 " + grid.ClientSize.Width
                    + "，列宽合计 " + SumWidths(grid));
                if (SumWidths(grid) > grid.ClientSize.Width + 4)
                    Sb.AppendLine("    [提示] 列宽合计超出可视宽度，会出现横向滚动条");
                foreach (DataGridViewColumn c in grid.Columns)
                {
                    if (c.Width < 30) Fail("列过窄: " + c.HeaderText + " = " + c.Width);
                    Sb.AppendLine("    " + c.HeaderText + "  " + c.Width + "px");
                }
                if (grid.Rows.Count == 0) Sb.AppendLine("    [提示] 预览表当前无行（测试环境桌面为空或全部保留）");
                else Sb.AppendLine("    行数: " + grid.Rows.Count);

                // 缩放测试：最小尺寸
                form.Size = form.MinimumSize;
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                Sb.AppendLine();
                Sb.AppendLine("最小尺寸下的 ClientSize: " + form.ClientSize.Width + "x" + form.ClientSize.Height);
                foreach (TabPage page in tabs.TabPages)
                {
                    tabs.SelectedIndex = tabs.TabPages.IndexOf(page);
                    Application.DoEvents();
                    DumpAndCheck(page, form, true);
                }

                form.Close();
            }
            catch (Exception ex)
            {
                _problems++;
                Sb.AppendLine("[异常] " + ex);
            }
            finally
            {
                try { if (form != null && !form.IsDisposed) form.Dispose(); } catch { }
                try { Directory.Delete(sandbox, true); } catch { }
                Sb.AppendLine();
                Sb.AppendLine("问题计数: " + _problems);
                File.WriteAllText(outFile, Sb.ToString(), new UTF8Encoding(true));
                Console.WriteLine(Sb.ToString());
            }
            return _problems;
        }

        private static int SumWidths(DataGridView g)
        {
            int sum = 0;
            foreach (DataGridViewColumn c in g.Columns) sum += c.Width;
            return sum;
        }

        private static void CheckExists(Control root, string text)
        {
            List<Control> hits = new List<Control>();
            FindAll(root, text, hits);
            if (hits.Count == 0) { Fail("缺少控件: " + text); Sb.AppendLine("  [MISS] " + text); }
            else
            {
                Control c = hits[0];
                Sb.AppendLine(string.Format("  [OK]   {0}  ({1}, {2}px)", text, c.GetType().Name, c.Height));
                if (c.Width < 20 || c.Height < 16) Fail("控件尺寸异常: " + text);
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

        private static void DumpAndCheck(Control root, Form form, bool quiet = false)
        {
            foreach (Control c in Descendants(root))
            {
                if (!c.Visible) continue;
                Rectangle r = c.RectangleToScreen(c.ClientRectangle);
                Rectangle fr = form.RectangleToScreen(form.ClientRectangle);
                string text = (c.Text ?? "").Replace("\r", " ").Replace("\n", " ");
                if (text.Length > 50) text = text.Substring(0, 50) + "...";
                if (text.Length == 0) text = "(无文本)";

                if (c.Width <= 0 || c.Height <= 0) Fail("零尺寸: " + c.GetType().Name + " " + text);
                if (r.Right > fr.Right + 2 || r.Bottom > fr.Bottom + 2)
                    Fail("超出窗体: " + c.GetType().Name + " " + text);

                if (!quiet)
                    Sb.AppendLine(string.Format("  {0,-22} {1,4}x{2,-4} @({3,4},{4,4})  {5}",
                        c.GetType().Name, c.Width, c.Height,
                        r.Left - fr.Left, r.Top - fr.Top, text));
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control d in Descendants(c)) yield return d;
            }
        }
    }
}
