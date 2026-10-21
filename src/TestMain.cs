using System;

namespace DeskTidy
{
    /// <summary>仅供构建脚本编译出的控制台版测试程序使用（不参与正式 exe）。</summary>
    internal static class TestMain
    {
        [STAThread]
        private static int Main()
        {
            return SelfTest.Run();
        }
    }
}
