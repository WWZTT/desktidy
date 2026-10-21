using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DeskTidy
{
    /// <summary>无界面的逻辑自测。运行 DeskTidy.exe --selftest，返回码 0 表示全部通过。</summary>
    internal static class SelfTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly List<string> Failures = new List<string>();

        private static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("  [PASS] " + name);
            }
            else
            {
                _fail++;
                Failures.Add(name + " :: " + detail);
                Console.WriteLine("  [FAIL] " + name + "  -> " + detail);
            }
        }

        private static void Eq(string name, object expected, object actual)
        {
            bool ok = Equals(expected, actual);
            Check(name, ok, string.Format("expected=[{0}] actual=[{1}]", expected, actual));
        }

        public static int Run()
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            string sandbox = Path.Combine(Path.GetTempPath(), "DeskTidySelfTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string desktop = Path.Combine(sandbox, "Desktop");
            string dataRoot = Path.Combine(sandbox, "Data");

            try
            {
                Directory.CreateDirectory(desktop);
                Directory.CreateDirectory(dataRoot);
                AppPaths.RootOverride = dataRoot;

                TestFolderName();
                TestExcludeRules();
                TestPlanBasic(desktop);
                TestSnapshotProtection(desktop);
                TestStrictSnapshot(desktop);
                TestExecuteAndUndo(desktop);
                TestUniqueNames(desktop);
                TestFoldersNeverPacked(desktop);
                TestReuseSameDayFolder(desktop);
            }
            catch (Exception ex)
            {
                _fail++;
                Failures.Add("EXCEPTION :: " + ex);
                Console.WriteLine("[FATAL] " + ex);
            }
            finally
            {
                try { Directory.Delete(sandbox, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项。");
            if (_fail > 0)
            {
                Console.WriteLine("失败明细：");
                foreach (string f in Failures) Console.WriteLine("  - " + f);
                return 1;
            }
            return 0;
        }

        private static void Reset(string desktop)
        {
            foreach (string f in Directory.GetFiles(desktop)) File.Delete(f);
            foreach (string d in Directory.GetDirectories(desktop)) Directory.Delete(d, true);
            AppPaths.EnsureDirs();
            if (File.Exists(AppPaths.SnapshotFile)) File.Delete(AppPaths.SnapshotFile);
            if (File.Exists(AppPaths.ConfigFile)) File.Delete(AppPaths.ConfigFile);
            foreach (string f in Directory.GetFiles(AppPaths.LogDir, "*.json")) File.Delete(f);
        }

        private static void Touch(string path, string content)
        {
            File.WriteAllText(path, content ?? "x");
        }

        // ---------------------------------------------------------- 用例

        private static void TestFolderName()
        {
            Console.WriteLine("日期文件夹名");
            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DateFormat = "yyyy-MM-dd";
            Eq("默认格式", "2025-03-09", Organizer.BuildFolderName(cfg, new DateTime(2025, 3, 9)));

            cfg.DateFormat = "yyyy年MM月dd日";
            Eq("中文格式", "2025年03月09日", Organizer.BuildFolderName(cfg, new DateTime(2025, 3, 9)));

            cfg.DateFormat = "yyyy/MM/dd";
            Eq("非法字符被清洗", "2025-03-09", Organizer.BuildFolderName(cfg, new DateTime(2025, 3, 9)));

            cfg.DateFormat = "";
            Eq("空格式回退默认", "2025-03-09", Organizer.BuildFolderName(cfg, new DateTime(2025, 3, 9)));

            cfg.DateFormat = "???";
            string weird = Organizer.BuildFolderName(cfg, new DateTime(2025, 3, 9));
            Check("格式化异常不崩溃", weird.Length > 0, "got=" + weird);
        }

        private static void TestExcludeRules()
        {
            Console.WriteLine("排除规则");
            AppConfig cfg = AppConfig.CreateDefault();
            cfg.ExcludePatterns = new string[] { "desktop.ini", "*.tmp", "~$*" };
            cfg.ExcludeExtensions = new string[] { ".lnk", "exe" };

            Check("精确名", Organizer.IsExcludedByRule(cfg, "desktop.ini"), "desktop.ini");
            Check("大小写无关", Organizer.IsExcludedByRule(cfg, "Desktop.INI"), "Desktop.INI");
            Check("通配符", Organizer.IsExcludedByRule(cfg, "abc.tmp"), "abc.tmp");
            Check("前缀通配", Organizer.IsExcludedByRule(cfg, "~$报告.docx"), "~$报告.docx");
            Check("扩展名无点也识别", Organizer.IsExcludedByRule(cfg, "setup.exe"), "setup.exe");
            Check("扩展名忽略大小写", Organizer.IsExcludedByRule(cfg, "A.LNK"), "A.LNK");
            Check("普通文件不命中", !Organizer.IsExcludedByRule(cfg, "报表.xlsx"), "报表.xlsx");
            Check("* 全匹配", Wildcards.IsMatch("*", "任意名"), "* rule");

            List<string> split = Wildcards.Split("a.txt\r\n# 注释\r\n\r\n b.tmp ; c.log | d.md ");
            Eq("规则文本拆分", 4, split.Count);
        }

        private static void TestPlanBasic(string desktop)
        {
            Console.WriteLine("基础方案：只打包文件");
            Reset(desktop);
            Touch(Path.Combine(desktop, "a.txt"), "1");
            Touch(Path.Combine(desktop, "b.pdf"), "2");
            Touch(Path.Combine(desktop, "noext"), "3");
            Touch(Path.Combine(desktop, ".hidden"), "4");
            Directory.CreateDirectory(Path.Combine(desktop, "项目文件夹"));

            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DesktopPath = desktop;
            cfg.ExcludePatterns = new string[0];

            TidyPlan plan = Organizer.BuildPlan(cfg, null, new DateTime(2025, 3, 9));
            Eq("文件数（不含目录）", 4, plan.Items.Count);
            Eq("全部待打包", 4, plan.PackCount);

            bool hasDir = false;
            foreach (PlanItem it in plan.Items)
                if (it.Name == "项目文件夹") hasDir = true;
            Check("目录未进入方案", !hasDir, "目录出现在列表里");

            Eq("目标目录", Path.Combine(desktop, "2025-03-09"), plan.TargetDir);
        }

        private static void TestSnapshotProtection(string desktop)
        {
            Console.WriteLine("快照保护");
            Reset(desktop);
            Touch(Path.Combine(desktop, "每天要用.docx"), "fixed");
            Touch(Path.Combine(desktop, "快捷方式.lnk"), "fixed");

            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DesktopPath = desktop;
            cfg.ExcludePatterns = new string[0];
            cfg.ExcludeExtensions = new string[0];

            SnapshotData snap = SnapshotStore.Capture(desktop);
            SnapshotStore.Save(snap);
            Eq("快照记录数", 2, snap.Entries.Count);

            // 再新增两个文件
            Touch(Path.Combine(desktop, "新建 1.txt"), "new");
            Touch(Path.Combine(desktop, "新建 2.png"), "new");

            SnapshotData reloaded = SnapshotStore.Load();
            Eq("快照可持久化", 2, reloaded.Entries.Count);

            TidyPlan plan = Organizer.BuildPlan(cfg, reloaded, new DateTime(2025, 3, 9));
            Eq("只打包新增文件", 2, plan.PackCount);
            Eq("固定文件数量", 2, plan.ProtectedCount);

            foreach (PlanItem it in plan.Items)
            {
                if (it.Name == "每天要用.docx")
                    Check("快照文件判定为保留", it.Decision == PackDecision.ProtectedSnap, it.Decision.ToString());
            }

            // 关闭快照保护后，全部文件都视为可打包
            cfg.ProtectSnapshot = false;
            TidyPlan plan2 = Organizer.BuildPlan(cfg, reloaded, new DateTime(2025, 3, 9));
            Eq("关闭快照保护后全部打包", 4, plan2.PackCount);
            cfg.ProtectSnapshot = true;

            // 规则优先级：排除规则优先于快照
            cfg.ExcludePatterns = new string[] { "新建 1.txt" };
            TidyPlan plan3 = Organizer.BuildPlan(cfg, reloaded, new DateTime(2025, 3, 9));
            Eq("排除规则生效", 1, plan3.PackCount);
        }

        private static void TestStrictSnapshot(string desktop)
        {
            Console.WriteLine("严格快照匹配");
            Reset(desktop);
            string f = Path.Combine(desktop, "报告.docx");
            Touch(f, "v1");

            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DesktopPath = desktop;
            cfg.ExcludePatterns = new string[0];
            cfg.ExcludeExtensions = new string[0];

            SnapshotData snap = SnapshotStore.Capture(desktop);

            // 修改内容（大小变化）
            Touch(f, "版本二，内容更长一些");

            TidyPlan relaxed = Organizer.BuildPlan(cfg, snap, new DateTime(2025, 3, 9));
            Eq("宽松模式仍按名字保护", 0, relaxed.PackCount);

            cfg.StrictSnapshotMatch = true;
            TidyPlan strict = Organizer.BuildPlan(cfg, snap, new DateTime(2025, 3, 9));
            Eq("严格模式检出被修改", 1, strict.PackCount);
        }

        private static void TestExecuteAndUndo(string desktop)
        {
            Console.WriteLine("执行与撤销");
            Reset(desktop);
            Touch(Path.Combine(desktop, "固定.txt"), "keep");
            Touch(Path.Combine(desktop, "新1.txt"), "n1");
            Touch(Path.Combine(desktop, "新2.jpg"), "n2");

            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DesktopPath = desktop;
            cfg.ExcludePatterns = new string[0];
            cfg.ExcludeExtensions = new string[0];
            cfg.ReuseSameDayFolder = true;

            SnapshotData snap = SnapshotStore.Capture(desktop);
            Touch(Path.Combine(desktop, "新3.log"), "n3");

            TidyPlan plan = Organizer.BuildPlan(cfg, snap, new DateTime(2025, 3, 9));
            Eq("待打包数", 1, plan.PackCount);

            TidyResult dry = Organizer.Execute(cfg, plan, true);
            Check("试运行成功", dry.Success, dry.Message);
            Check("试运行不移动文件", File.Exists(Path.Combine(desktop, "新3.log")), "文件被移动了");

            TidyResult res = Organizer.Execute(cfg, plan, false);
            Check("执行成功", res.Success, res.Message);
            Eq("移动数量", 1, res.MovedCount);
            Check("目标文件夹已建立", Directory.Exists(Path.Combine(desktop, "2025-03-09")), "目标文件夹不存在");
            Check("文件已进入日期文件夹", File.Exists(Path.Combine(desktop, "2025-03-09", "新3.log")), "文件不在目标目录");
            Check("桌面已无该文件", !File.Exists(Path.Combine(desktop, "新3.log")), "桌面仍存在该文件");
            Check("固定文件留在桌面", File.Exists(Path.Combine(desktop, "固定.txt")), "固定文件被移动");
            Check("撤销记录已写入", res.UndoEntry != null && File.Exists(res.UndoEntry.Path), "无撤销日志");

            Eq("历史记录条数", 1, UndoLogStore.ListAll().Count);

            TidyResult undo = Organizer.Undo(res.UndoEntry, false);
            Check("撤销成功", undo.Success, undo.Message);
            Check("文件回到桌面", File.Exists(Path.Combine(desktop, "新3.log")), "文件未回来");

            UndoLogEntry again = UndoLogStore.ListAll()[0];
            Check("撤销状态已持久化", again.Data.Undone, "Undone 未落盘");

            TidyResult undo2 = Organizer.Undo(again, false);
            Check("重复撤销被拒绝", !undo2.Success, "重复撤销居然成功");

            // 撤销同名冲突：桌面上已存在同名文件时不覆盖
            Reset(desktop);
            Touch(Path.Combine(desktop, "同名.txt"), "old");
            SnapshotData s2 = SnapshotStore.Capture(desktop);
            Touch(Path.Combine(desktop, "同名.txt"), "old");
            File.Delete(Path.Combine(desktop, "同名.txt"));
            Touch(Path.Combine(desktop, "另一个.txt"), "a");
            TidyPlan p2 = Organizer.BuildPlan(cfg, s2, new DateTime(2025, 3, 10));
            TidyResult r2 = Organizer.Execute(cfg, p2, false);
            Eq("第二次打包数量", 1, r2.MovedCount);
            Touch(Path.Combine(desktop, "另一个.txt"), "占用同名");
            TidyResult u2 = Organizer.Undo(r2.UndoEntry, false);
            Check("撤销遇同名不覆盖", u2.Success && File.Exists(Path.Combine(desktop, "另一个 (1).txt")),
                "期望生成 另一个 (1).txt");
            Eq("原同名文件内容未被覆盖", "占用同名", File.ReadAllText(Path.Combine(desktop, "另一个.txt")));
        }

        private static void TestUniqueNames(string desktop)
        {
            Console.WriteLine("重名处理");
            Reset(desktop);
            string dir = Path.Combine(desktop, "2025-01-01");
            Directory.CreateDirectory(dir);
            Touch(Path.Combine(dir, "a.txt"), "x");
            string u1 = FsUtil.UniqueFile(Path.Combine(dir, "a.txt"));
            Eq("重名文件改名", "a (1).txt", Path.GetFileName(u1));
            Touch(u1, "y");
            string u2 = FsUtil.UniqueFile(Path.Combine(dir, "a.txt"));
            Eq("再次重名递增", "a (2).txt", Path.GetFileName(u2));

            Directory.CreateDirectory(Path.Combine(desktop, "2025-02-02"));
            string d2 = FsUtil.UniqueDir(Path.Combine(desktop, "2025-02-02"));
            Eq("重名文件夹改名", "2025-02-02 (1)", Path.GetFileName(d2));

            Eq("大小格式化 B", "512 B", FsUtil.FormatSize(512));
            Eq("大小格式化 MB", "2.0 MB", FsUtil.FormatSize(2 * 1024 * 1024));
            Eq("非法字符清洗", "a-b", FsUtil.SanitizeName("a/b"));
        }

        private static void TestFoldersNeverPacked(string desktop)
        {
            Console.WriteLine("文件夹永不打包");
            Reset(desktop);
            Directory.CreateDirectory(Path.Combine(desktop, "空文件夹"));
            Directory.CreateDirectory(Path.Combine(desktop, "有文件的文件夹"));
            Touch(Path.Combine(desktop, "有文件的文件夹", "inner.txt"), "i");

            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DesktopPath = desktop;
            cfg.ExcludePatterns = new string[0];

            TidyPlan plan = Organizer.BuildPlan(cfg, null, new DateTime(2025, 3, 9));
            Eq("方案不含任何目录条目", 0, plan.Items.Count);

            TidyResult res = Organizer.Execute(cfg, plan, false);
            Check("空方案不报错", res.Success, res.Message);
            Check("文件夹原样保留", Directory.Exists(Path.Combine(desktop, "有文件的文件夹")), "文件夹消失");
            Check("子文件未被移动", File.Exists(Path.Combine(desktop, "有文件的文件夹", "inner.txt")), "子文件被移动");
        }

        private static void TestReuseSameDayFolder(string desktop)
        {
            Console.WriteLine("同一天多次整理");
            Reset(desktop);
            AppConfig cfg = AppConfig.CreateDefault();
            cfg.DesktopPath = desktop;
            cfg.ExcludePatterns = new string[0];
            cfg.ReuseSameDayFolder = true;

            Touch(Path.Combine(desktop, "批1.txt"), "1");
            TidyResult r1 = Organizer.Execute(cfg, Organizer.BuildPlan(cfg, null, new DateTime(2025, 4, 1)), false);
            Eq("第一批", 1, r1.MovedCount);

            Touch(Path.Combine(desktop, "批2.txt"), "2");
            TidyResult r2 = Organizer.Execute(cfg, Organizer.BuildPlan(cfg, null, new DateTime(2025, 4, 1)), false);
            Eq("第二批", 1, r2.MovedCount);

            Check("合并到同一文件夹", File.Exists(Path.Combine(desktop, "2025-04-01", "批1.txt"))
                && File.Exists(Path.Combine(desktop, "2025-04-01", "批2.txt")), "未合并在同一目录");
            Check("没有产生 (1) 目录", !Directory.Exists(Path.Combine(desktop, "2025-04-01 (1)")), "出现了重复目录");

            cfg.ReuseSameDayFolder = false;
            Touch(Path.Combine(desktop, "批3.txt"), "3");
            Organizer.Execute(cfg, Organizer.BuildPlan(cfg, null, new DateTime(2025, 4, 1)), false);
            Check("关闭复用后另建目录", Directory.Exists(Path.Combine(desktop, "2025-04-01 (1)")), "未另建目录");
        }
    }
}
