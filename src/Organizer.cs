using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DeskTidy
{
    public enum PackDecision
    {
        Pack,           // 会被打包
        IsDirectory,    // 文件夹，永不打包
        ProtectedSnap,  // 命中快照，固定文件
        ProtectedRule,  // 命中排除规则
        TargetFolder    // 目标日期文件夹本身（不会出现在文件列表里，占位）
    }

    public class PlanItem
    {
        public string FullPath { get; set; }
        public string Name { get; set; }
        public long Length { get; set; }
        public DateTime LastWrite { get; set; }
        public PackDecision Decision { get; set; }
        public string Reason { get; set; }

        /// <summary>用户在界面上的手动勾选；null 表示沿用自动判断。</summary>
        public bool? UserChoice { get; set; }

        public bool WillPack
        {
            get
            {
                if (UserChoice.HasValue) return UserChoice.Value;
                return Decision == PackDecision.Pack;
            }
        }
    }

    public class TidyPlan
    {
        public string DesktopPath { get; set; }
        public string TargetDir { get; set; }
        public string FolderName { get; set; }
        public List<PlanItem> Items { get; set; }

        public TidyPlan()
        {
            Items = new List<PlanItem>();
        }

        public List<PlanItem> PackItems
        {
            get
            {
                List<PlanItem> list = new List<PlanItem>();
                foreach (PlanItem i in Items) if (i.WillPack) list.Add(i);
                return list;
            }
        }

        public int PackCount { get { return PackItems.Count; } }

        public int ProtectedCount
        {
            get
            {
                int n = 0;
                foreach (PlanItem i in Items)
                    if (i.Decision == PackDecision.ProtectedSnap || i.Decision == PackDecision.ProtectedRule) n++;
                return n;
            }
        }

        public long PackBytes
        {
            get
            {
                long n = 0;
                foreach (PlanItem i in Items) if (i.WillPack) n += i.Length;
                return n;
            }
        }
    }

    public class TidyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string TargetDir { get; set; }
        public int MovedCount { get; set; }
        public List<string> Errors { get; set; }
        public UndoLogEntry UndoEntry { get; set; }

        public TidyResult()
        {
            Errors = new List<string>();
        }
    }

    public static class Organizer
    {
        /// <summary>目标文件夹名，如 2025-06-01。</summary>
        public static string BuildFolderName(AppConfig cfg, DateTime when)
        {
            string fmt = string.IsNullOrEmpty(cfg.DateFormat) ? "yyyy-MM-dd" : cfg.DateFormat;
            string name;
            try
            {
                name = when.ToString(fmt, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                name = when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return FsUtil.SanitizeName(name);
        }

        /// <summary>当日目标目录；同一天复用同名文件夹（避免出现 2025-06-01 (1)）。</summary>
        public static string BuildTargetDir(AppConfig cfg, string desktopPath, DateTime when)
        {
            string folder = BuildFolderName(cfg, when);
            string target = Path.Combine(desktopPath, folder);
            if (cfg.ReuseSameDayFolder && Directory.Exists(target)) return target;
            if (!cfg.ReuseSameDayFolder) return FsUtil.UniqueDir(target);
            return target;
        }

        public static bool IsExcludedByRule(AppConfig cfg, string fileName)
        {
            string ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext))
            {
                foreach (string e in cfg.ExcludeExtensions)
                {
                    string ee = e.Trim();
                    if (ee.Length == 0) continue;
                    if (!ee.StartsWith(".")) ee = "." + ee;
                    if (string.Equals(ee, ext, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            foreach (string p in cfg.ExcludePatterns)
            {
                if (Wildcards.IsMatch(p, fileName)) return true;
            }
            return false;
        }

        /// <summary>生成整理方案：只作用于桌面顶层"文件"，不动任何文件夹。</summary>
        public static TidyPlan BuildPlan(AppConfig cfg, SnapshotData snapshot, DateTime when)
        {
            cfg = AppConfig.Normalize(cfg);
            string desktop = cfg.DesktopPath;

            TidyPlan plan = new TidyPlan();
            plan.DesktopPath = desktop;
            plan.TargetDir = BuildTargetDir(cfg, desktop, when);
            plan.FolderName = Path.GetFileName(plan.TargetDir);

            if (!Directory.Exists(desktop)) return plan;

            Dictionary<string, SnapshotEntry> snap = SnapshotStore.ToIndex(snapshot);
            bool useSnap = cfg.ProtectSnapshot && snap.Count > 0;

            string[] files = Directory.GetFiles(desktop);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string path in files)
            {
                PlanItem item = new PlanItem();
                item.FullPath = path;
                item.Name = Path.GetFileName(path);
                item.Decision = PackDecision.Pack;
                item.Reason = "新增文件，可打包";
                try
                {
                    FileInfo fi = new FileInfo(path);
                    item.Length = fi.Length;
                    item.LastWrite = fi.LastWriteTime;
                }
                catch
                {
                    item.Length = 0;
                    item.LastWrite = DateTime.MinValue;
                }

                if (IsExcludedByRule(cfg, item.Name))
                {
                    item.Decision = PackDecision.ProtectedRule;
                    item.Reason = "命中排除规则，始终保留";
                    plan.Items.Add(item);
                    continue;
                }

                if (useSnap)
                {
                    SnapshotEntry entry;
                    if (snap.TryGetValue(item.Name, out entry))
                    {
                        bool match = !cfg.StrictSnapshotMatch ||
                                     (entry.Length == item.Length &&
                                      entry.LastWriteUtcTicks == item.LastWrite.ToUniversalTime().Ticks);
                        if (match)
                        {
                            item.Decision = PackDecision.ProtectedSnap;
                            item.Reason = cfg.StrictSnapshotMatch
                                ? "快照文件（名字/大小/时间一致），保留"
                                : "快照文件（按文件名匹配），保留";
                            plan.Items.Add(item);
                            continue;
                        }
                        item.Reason = "同名快照文件已被修改，视为新增";
                    }
                }
                else if (cfg.ProtectSnapshot)
                {
                    item.Reason = "尚未建立快照，全部文件按新增处理";
                }

                plan.Items.Add(item);
            }

            return plan;
        }

        /// <summary>执行整理。</summary>
        public static TidyResult Execute(AppConfig cfg, TidyPlan plan, bool dryRun)
        {
            cfg = AppConfig.Normalize(cfg);
            TidyResult result = new TidyResult();
            result.TargetDir = plan.TargetDir;

            List<PlanItem> items = plan.PackItems;
            if (items.Count == 0)
            {
                result.Success = true;
                result.Message = "没有需要打包的新增文件。";
                return result;
            }
            if (dryRun)
            {
                result.Success = true;
                result.Message = string.Format(CultureInfo.InvariantCulture,
                    "试运行：将打包 {0} 个文件（{1}）。", items.Count, FsUtil.FormatSize(plan.PackBytes));
                return result;
            }

            UndoLogData log = new UndoLogData();
            log.CreatedLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            log.TargetDir = plan.TargetDir;

            try
            {
                Directory.CreateDirectory(plan.TargetDir);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "无法创建目标文件夹：" + ex.Message;
                return result;
            }

            foreach (PlanItem item in items)
            {
                try
                {
                    string dest = Path.Combine(plan.TargetDir, item.Name);
                    dest = FsUtil.UniqueFile(dest);
                    File.Move(item.FullPath, dest);

                    UndoItem u = new UndoItem();
                    u.From = item.FullPath;
                    u.To = dest;
                    log.Items.Add(u);
                    result.MovedCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(item.Name + " -> " + ex.Message);
                }
            }

            if (log.Items.Count > 0)
            {
                UndoLogEntry entry = new UndoLogEntry();
                entry.Data = log;
                entry.Path = UndoLogStore.Save(log);
                result.UndoEntry = entry;
            }

            result.Success = result.Errors.Count == 0 || result.MovedCount > 0;
            if (result.Errors.Count == 0)
                result.Message = string.Format(CultureInfo.InvariantCulture,
                    "已打包 {0} 个文件到「{1}」。", result.MovedCount, plan.FolderName);
            else
                result.Message = string.Format(CultureInfo.InvariantCulture,
                    "已打包 {0} 个文件，{1} 个失败（可能正被占用）。",
                    result.MovedCount, result.Errors.Count);
            return result;
        }

        /// <summary>撤销一次整理：把文件从日期文件夹移回桌面。</summary>
        public static TidyResult Undo(UndoLogEntry entry, bool dryRun)
        {
            TidyResult result = new TidyResult();
            if (entry == null || entry.Data == null)
            {
                result.Message = "没有可撤销的整理记录。";
                return result;
            }
            UndoLogData log = entry.Data;
            result.TargetDir = log.TargetDir;

            if (log.Undone)
            {
                result.Message = "这次整理已经撤销过了。";
                return result;
            }

            foreach (UndoItem u in log.Items)
            {
                if (!File.Exists(u.To))
                {
                    result.Errors.Add(Path.GetFileName(u.To) + " -> 回滚源文件不存在（可能已被移动或删除）");
                    continue;
                }
                try
                {
                    if (dryRun) { result.MovedCount++; continue; }
                    string parent = Path.GetDirectoryName(u.From);
                    if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                    string dest = FsUtil.UniqueFile(u.From);
                    File.Move(u.To, dest);
                    u.From = dest;
                    result.MovedCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(Path.GetFileName(u.To) + " -> " + ex.Message);
                }
            }

            if (!dryRun)
            {
                log.Undone = true;
                log.UndoneLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                UndoLogStore.Update(entry);
            }

            result.Success = result.MovedCount > 0;
            result.Message = string.Format(CultureInfo.InvariantCulture,
                "{0}回滚 {1} 个文件{2}。",
                dryRun ? "试运行：" : "",
                result.MovedCount,
                result.Errors.Count > 0 ? "，" + result.Errors.Count + " 个失败" : "");
            return result;
        }
    }
}
