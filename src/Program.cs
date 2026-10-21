using System;
using System.IO;
using System.Windows.Forms;

namespace DeskTidy
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                AppPaths.EnsureDirs();
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建数据目录：" + AppPaths.Root + "\n" + ex.Message,
                    "桌面整理助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("发生未处理的错误：\n" + e.Exception.Message, "桌面整理助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
            return 0;
        }
    }
}
