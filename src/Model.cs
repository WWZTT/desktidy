using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DeskTidy
{
    // ---------------------------------------------------------------- 路径

    public static class AppPaths
    {
        private static string _root;

        /// <summary>仅供自测使用：把数据根目录指向临时目录。</summary>
        public static string RootOverride
        {
            set { _root = value; }
        }

        public static string Root
        {
            get
            {
                if (_root == null)
                    _root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "DeskTidy");
                return _root;
            }
        }

        public static string ConfigFile { get { return Path.Combine(Root, "config.json"); } }
        public static string SnapshotFile { get { return Path.Combine(Root, "snapshot.json"); } }
        public static string LogDir { get { return Path.Combine(Root, "logs"); } }

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LogDir);
        }

        public static string DefaultDesktopPath()
        {
            string p = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(p))
                p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            return p;
        }
    }

    // ---------------------------------------------------------------- JSON

    internal static class Json
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static string Write(object value)
        {
            Ser.MaxJsonLength = int.MaxValue;
            return Ser.Serialize(value);
        }

        public static T Read<T>(string text) where T : class
        {
            Ser.MaxJsonLength = int.MaxValue;
            return Ser.Deserialize<T>(text);
        }
    }

    // ---------------------------------------------------------------- 配置

    public class AppConfig
    {
        public string DesktopPath { get; set; }
        public string DateFormat { get; set; }
        public bool ReuseSameDayFolder { get; set; }
        public bool ProtectSnapshot { get; set; }
        public bool StrictSnapshotMatch { get; set; }
        public bool OpenFolderAfterTidy { get; set; }
        public string[] ExcludePatterns { get; set; }
        public string[] ExcludeExtensions { get; set; }

        public static AppConfig CreateDefault()
        {
            AppConfig c = new AppConfig();
            c.DesktopPath = AppPaths.DefaultDesktopPath();
            c.DateFormat = "yyyy-MM-dd";
            c.ReuseSameDayFolder = true;
            c.ProtectSnapshot = true;
            c.StrictSnapshotMatch = false;
            c.OpenFolderAfterTidy = true;
            c.ExcludePatterns = new string[] { "desktop.ini", "thumbs.db", "*.tmp", "~$*" };
            c.ExcludeExtensions = new string[0];
            return c;
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigFile)) return CreateDefault();
                AppConfig c = Json.Read<AppConfig>(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8));
                return Normalize(c);
            }
            catch
            {
                return CreateDefault();
            }
        }

        public static AppConfig Normalize(AppConfig c)
        {
            if (c == null) return CreateDefault();
            if (string.IsNullOrEmpty(c.DesktopPath)) c.DesktopPath = AppPaths.DefaultDesktopPath();
            if (string.IsNullOrEmpty(c.DateFormat)) c.DateFormat = "yyyy-MM-dd";
            if (c.ExcludePatterns == null) c.ExcludePatterns = new string[0];
            if (c.ExcludeExtensions == null) c.ExcludeExtensions = new string[0];
            return c;
        }

        public void Save()
        {
            AppPaths.EnsureDirs();
            File.WriteAllText(AppPaths.ConfigFile, Json.Write(this), Encoding.UTF8);
        }

        public AppConfig Clone()
        {
            AppConfig c = new AppConfig();
            c.DesktopPath = DesktopPath;
            c.DateFormat = DateFormat;
            c.ReuseSameDayFolder = ReuseSameDayFolder;
            c.ProtectSnapshot = ProtectSnapshot;
            c.StrictSnapshotMatch = StrictSnapshotMatch;
            c.OpenFolderAfterTidy = OpenFolderAfterTidy;
            c.ExcludePatterns = (string[])ExcludePatterns.Clone();
            c.ExcludeExtensions = (string[])ExcludeExtensions.Clone();
            return c;
        }
    }

    // ---------------------------------------------------------------- 快照

    public class SnapshotEntry
    {
        public string Name { get; set; }
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
    }

    public class SnapshotData
    {
        public string DesktopPath { get; set; }
        public string CreatedLocal { get; set; }
        public List<SnapshotEntry> Entries { get; set; }

        public SnapshotData()
        {
            Entries = new List<SnapshotEntry>();
        }
    }

    public static class SnapshotStore
    {
        public static SnapshotData Load()
        {
            try
            {
                if (!File.Exists(AppPaths.SnapshotFile)) return null;
                SnapshotData s = Json.Read<SnapshotData>(File.ReadAllText(AppPaths.SnapshotFile, Encoding.UTF8));
                if (s == null) return null;
                if (s.Entries == null) s.Entries = new List<SnapshotEntry>();
                return s;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(SnapshotData s)
        {
            AppPaths.EnsureDirs();
            File.WriteAllText(AppPaths.SnapshotFile, Json.Write(s), Encoding.UTF8);
        }

        public static void Delete()
        {
            if (File.Exists(AppPaths.SnapshotFile)) File.Delete(AppPaths.SnapshotFile);
        }

        /// <summary>记录桌面顶层的全部文件（含隐藏文件），作为"受保护"基线。</summary>
        public static SnapshotData Capture(string desktopPath)
        {
            SnapshotData s = new SnapshotData();
            s.DesktopPath = desktopPath;
            s.CreatedLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (!Directory.Exists(desktopPath)) return s;

            List<string> files = new List<string>(Directory.GetFiles(desktopPath));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(path);
                    SnapshotEntry e = new SnapshotEntry();
                    e.Name = fi.Name;
                    e.Length = fi.Length;
                    e.LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks;
                    s.Entries.Add(e);
                }
                catch
                {
                    // 单个文件读不到属性就跳过，不影响整体快照
                }
            }
            return s;
        }

        public static Dictionary<string, SnapshotEntry> ToIndex(SnapshotData s)
        {
            Dictionary<string, SnapshotEntry> map =
                new Dictionary<string, SnapshotEntry>(StringComparer.OrdinalIgnoreCase);
            if (s != null && s.Entries != null)
            {
                foreach (SnapshotEntry e in s.Entries)
                    if (e != null && !string.IsNullOrEmpty(e.Name))
                        map[e.Name] = e;
            }
            return map;
        }
    }

    // ---------------------------------------------------------------- 撤销日志

    public class UndoItem
    {
        public string From { get; set; }
        public string To { get; set; }
    }

    public class UndoLogData
    {
        public string CreatedLocal { get; set; }
        public string TargetDir { get; set; }
        public bool Undone { get; set; }
        public string UndoneLocal { get; set; }
        public List<UndoItem> Items { get; set; }

        public UndoLogData()
        {
            Items = new List<UndoItem>();
        }
    }

    public class UndoLogEntry
    {
        public string Path { get; set; }
        public UndoLogData Data { get; set; }
    }

    public static class UndoLogStore
    {
        public static string Save(UndoLogData log)
        {
            AppPaths.EnsureDirs();
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".json";
            string path = Path.Combine(AppPaths.LogDir, name);
            File.WriteAllText(path, Json.Write(log), Encoding.UTF8);
            return path;
        }

        public static void Update(UndoLogEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Path)) return;
            File.WriteAllText(entry.Path, Json.Write(entry.Data), Encoding.UTF8);
        }

        /// <summary>按时间倒序返回全部整理日志。</summary>
        public static List<UndoLogEntry> ListAll()
        {
            List<UndoLogEntry> list = new List<UndoLogEntry>();
            if (!Directory.Exists(AppPaths.LogDir)) return list;

            string[] files = Directory.GetFiles(AppPaths.LogDir, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = files.Length - 1; i >= 0; i--)
            {
                try
                {
                    UndoLogData d = Json.Read<UndoLogData>(File.ReadAllText(files[i], Encoding.UTF8));
                    if (d == null || d.Items == null) continue;
                    UndoLogEntry e = new UndoLogEntry();
                    e.Path = files[i];
                    e.Data = d;
                    list.Add(e);
                }
                catch
                {
                    // 坏日志跳过
                }
            }
            return list;
        }
    }

    // ---------------------------------------------------------------- 通配符

    internal static class Wildcards
    {
        public static bool IsMatch(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern) || text == null) return false;
            if (pattern == "*") return true;

            StringBuilder sb = new StringBuilder("^");
            foreach (char ch in pattern)
            {
                if (ch == '*') sb.Append(".*");
                else if (ch == '?') sb.Append('.');
                else sb.Append(Regex.Escape(ch.ToString()));
            }
            sb.Append("$");
            return Regex.IsMatch(text, sb.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>把多行文本拆成规则列表；换行、分号、竖线都算分隔符，# 开头为注释。</summary>
        public static List<string> Split(string multiLine)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(multiLine)) return result;

            string text = multiLine.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] parts = text.Split(new char[] { '\n', ';', '|' });
            foreach (string raw in parts)
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                if (s.StartsWith("#")) continue;
                result.Add(s);
            }
            return result;
        }
    }

    // ---------------------------------------------------------------- 文件系统工具

    public static class FsUtil
    {
        public static string UniqueFile(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i < 10000; i++)
            {
                string candidate = Path.Combine(dir, string.Format("{0} ({1}){2}", name, i, ext));
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException("无法生成不冲突的文件名：" + path);
        }

        public static string UniqueDir(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return path;
            for (int i = 1; i < 10000; i++)
            {
                string candidate = path + " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
                if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
            }
            throw new IOException("无法生成不冲突的文件夹名：" + path);
        }

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "未命名";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool ok = true;
                for (int i = 0; i < bad.Length; i++)
                {
                    if (bad[i] == c) { ok = false; break; }
                }
                sb.Append(ok ? c : '-');
            }
            string s = sb.ToString().Trim().TrimEnd('.');
            return s.Length == 0 ? "未命名" : s;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
