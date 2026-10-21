using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DeskTidy
{
    public class MainForm : Form
    {
        private AppConfig _cfg;
        private SnapshotData _snapshot;
        private TidyPlan _plan;
        private bool _fillingGrid;

        private TextBox _txtDesktop;
        private Label _lblSummary;
        private Label _lblPlanInfo;

        private TabControl _tabs;

        private DataGridView _gridPlan;
        private CheckBox _chkDryRun;
        private Button _btnTidy;

        private Label _lblSnapInfo;
        private DataGridView _gridSnap;
        private Button _btnSnapNow;
        private Button _btnSnapClear;

        private TextBox _txtExcludePatterns;
        private TextBox _txtExcludeExts;
        private TextBox _txtDateFormat;
        private Label _lblDateSample;
        private CheckBox _chkReuseDay;
        private CheckBox _chkSnapOn;
        private CheckBox _chkStrictSnap;
        private CheckBox _chkOpenAfter;

        private DataGridView _gridHistory;
        private List<UndoLogEntry> _history = new List<UndoLogEntry>();

        private Label _status;

        /// <summary>装载设置到界面期间为 true，避免控件回填事件把未初始化的值写回配置。</summary>
        private bool _loadingUi;

        public MainForm()
        {
            _cfg = AppConfig.Load();
            AppPaths.EnsureDirs();
            _snapshot = SnapshotStore.Load();

            BuildUi();
            LoadSettingsIntoUi();
            RefreshAll();
        }

        // ------------------------------------------------------------ UI 搭建

        private void BuildUi()
        {
            Text = "桌面整理助手 v1.0";
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 700);
            MinimumSize = new Size(900, 600);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.Padding = new Padding(10, 10, 10, 6);
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildTabs(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 1;
            p.RowCount = 3;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel row1 = new TableLayoutPanel();
            row1.Dock = DockStyle.Fill;
            row1.ColumnCount = 4;
            row1.RowCount = 1;
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            row1.Margin = new Padding(0);

            Label lb = new Label();
            lb.Text = "桌面路径";
            lb.TextAlign = ContentAlignment.MiddleLeft;
            lb.Dock = DockStyle.Fill;
            lb.Margin = new Padding(0, 0, 6, 0);
            row1.Controls.Add(lb, 0, 0);

            _txtDesktop = new TextBox();
            _txtDesktop.Dock = DockStyle.Fill;
            _txtDesktop.Margin = new Padding(0, 4, 6, 4);
            _txtDesktop.TextChanged += delegate { };
            row1.Controls.Add(_txtDesktop, 1, 0);

            Button browse = new Button();
            browse.Text = "浏览...";
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(0, 3, 6, 3);
            browse.Click += delegate { BrowseDesktop(); };
            row1.Controls.Add(browse, 2, 0);

            Button refresh = new Button();
            refresh.Text = "刷新";
            refresh.Dock = DockStyle.Fill;
            refresh.Margin = new Padding(0, 3, 0, 3);
            refresh.Click += delegate { ReloadSnapshotAndPlan(); };
            row1.Controls.Add(refresh, 3, 0);

            p.Controls.Add(row1, 0, 0);

            _lblSummary = new Label();
            _lblSummary.Dock = DockStyle.Fill;
            _lblSummary.TextAlign = ContentAlignment.MiddleLeft;
            _lblSummary.ForeColor = Color.FromArgb(70, 70, 70);
            _lblSummary.Margin = new Padding(0, 0, 0, 0);
            p.Controls.Add(_lblSummary, 0, 1);

            _lblPlanInfo = new Label();
            _lblPlanInfo.Dock = DockStyle.Fill;
            _lblPlanInfo.TextAlign = ContentAlignment.MiddleLeft;
            _lblPlanInfo.ForeColor = Color.FromArgb(0, 90, 170);
            _lblPlanInfo.Margin = new Padding(0, 0, 0, 0);
            p.Controls.Add(_lblPlanInfo, 0, 2);

            return p;
        }

        private Control BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Margin = new Padding(0, 6, 0, 6);

            TabPage t1 = new TabPage("整理预览");
            t1.Padding = new Padding(6);
            t1.Controls.Add(BuildPlanTab());
            _tabs.TabPages.Add(t1);

            TabPage t2 = new TabPage("固定文件快照");
            t2.Padding = new Padding(6);
            t2.Controls.Add(BuildSnapshotTab());
            _tabs.TabPages.Add(t2);

            TabPage t3 = new TabPage("规则与设置");
            t3.Padding = new Padding(6);
            t3.Controls.Add(BuildRulesTab());
            _tabs.TabPages.Add(t3);

            TabPage t4 = new TabPage("整理记录 / 撤销");
            t4.Padding = new Padding(6);
            t4.Controls.Add(BuildHistoryTab());
            _tabs.TabPages.Add(t4);

            _tabs.SelectedIndexChanged += delegate
            {
                if (_tabs.SelectedIndex == 3) RefreshHistory();
                if (_tabs.SelectedIndex == 1) RefreshSnapshotGrid();
            };

            return _tabs;
        }

        private Control BuildPlanTab()
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 1;
            p.RowCount = 2;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0, 0, 0, 4);
            bar.WrapContents = false;

            Button bAll = new Button();
            bAll.Text = "全部勾选";
            bAll.Width = 86;
            bAll.Click += delegate { SetAllChoices(true); };
            bar.Controls.Add(bAll);

            Button bNone = new Button();
            bNone.Text = "全部取消";
            bNone.Width = 86;
            bNone.Click += delegate { SetAllChoices(false); };
            bar.Controls.Add(bNone);

            Button bReset = new Button();
            bReset.Text = "恢复自动判断";
            bReset.Width = 110;
            bReset.Click += delegate { ResetChoices(); };
            bar.Controls.Add(bReset);

            Label tip = new Label();
            tip.Text = "提示：只有文件会被打包，文件夹一律不动；「保留」的文件会留在桌面上。";
            tip.AutoSize = true;
            tip.ForeColor = Color.FromArgb(110, 110, 110);
            tip.Margin = new Padding(10, 7, 0, 0);
            bar.Controls.Add(tip);

            p.Controls.Add(bar, 0, 0);

            _gridPlan = new DataGridView();
            _gridPlan.Dock = DockStyle.Fill;
            _gridPlan.AllowUserToAddRows = false;
            _gridPlan.AllowUserToDeleteRows = false;
            _gridPlan.AllowUserToResizeRows = false;
            _gridPlan.RowHeadersVisible = false;
            _gridPlan.BackgroundColor = Color.White;
            _gridPlan.BorderStyle = BorderStyle.FixedSingle;
            _gridPlan.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _gridPlan.MultiSelect = false;
            _gridPlan.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _gridPlan.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _gridPlan.ColumnHeadersHeight = 28;
            _gridPlan.RowTemplate.Height = 24;

            DataGridViewCheckBoxColumn cPack = new DataGridViewCheckBoxColumn();
            cPack.HeaderText = "打包";
            cPack.Name = "colPack";
            cPack.FillWeight = 42F;
            cPack.SortMode = DataGridViewColumnSortMode.NotSortable;
            _gridPlan.Columns.Add(cPack);

            _gridPlan.Columns.Add(MakeTextColumn("colName", "文件名", 240F, DataGridViewContentAlignment.MiddleLeft));
            _gridPlan.Columns.Add(MakeTextColumn("colSize", "大小", 70F, DataGridViewContentAlignment.MiddleRight));
            _gridPlan.Columns.Add(MakeTextColumn("colTime", "修改时间", 130F, DataGridViewContentAlignment.MiddleLeft));
            _gridPlan.Columns.Add(MakeTextColumn("colState", "判断", 110F, DataGridViewContentAlignment.MiddleLeft));
            _gridPlan.Columns.Add(MakeTextColumn("colReason", "说明", 260F, DataGridViewContentAlignment.MiddleLeft));

            _gridPlan.CellValueChanged += GridPlanCellValueChanged;
            _gridPlan.CurrentCellDirtyStateChanged += delegate
            {
                if (_gridPlan.IsCurrentCellDirty) _gridPlan.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            p.Controls.Add(_gridPlan, 0, 1);
            return p;
        }

        private static DataGridViewTextBoxColumn MakeTextColumn(string name, string header, float weight, DataGridViewContentAlignment align)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.FillWeight = weight;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.DefaultCellStyle.Alignment = align;
            return c;
        }

        private Control BuildSnapshotTab()
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 1;
            p.RowCount = 4;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label desc = new Label();
            desc.Dock = DockStyle.Fill;
            desc.Text = "快照 = 把桌面「当前已有的文件」记录成基线。快照里的文件永远不会被整理打包。"
                + "以后新出现的文件才会进日期文件夹。建议在桌面干净时建立一次。";
            desc.ForeColor = Color.FromArgb(70, 70, 70);
            p.Controls.Add(desc, 0, 0);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = false;
            bar.Margin = new Padding(0);

            _btnSnapNow = new Button();
            _btnSnapNow.Text = "建立 / 更新快照";
            _btnSnapNow.Width = 150;
            _btnSnapNow.Height = 30;
            _btnSnapNow.Click += delegate { CaptureSnapshot(); };
            bar.Controls.Add(_btnSnapNow);

            _btnSnapClear = new Button();
            _btnSnapClear.Text = "删除快照";
            _btnSnapClear.Width = 100;
            _btnSnapClear.Height = 30;
            _btnSnapClear.Click += delegate { ClearSnapshot(); };
            bar.Controls.Add(_btnSnapClear);

            Button bAdd = new Button();
            bAdd.Text = "把当前桌面全部文件设为固定";
            bAdd.Width = 210;
            bAdd.Height = 30;
            bAdd.Click += delegate { CaptureSnapshot(); };
            bar.Controls.Add(bAdd);

            p.Controls.Add(bar, 0, 1);

            _lblSnapInfo = new Label();
            _lblSnapInfo.Dock = DockStyle.Fill;
            _lblSnapInfo.TextAlign = ContentAlignment.MiddleLeft;
            _lblSnapInfo.ForeColor = Color.FromArgb(0, 110, 60);
            p.Controls.Add(_lblSnapInfo, 0, 2);

            _gridSnap = new DataGridView();
            _gridSnap.Dock = DockStyle.Fill;
            _gridSnap.AllowUserToAddRows = false;
            _gridSnap.AllowUserToDeleteRows = false;
            _gridSnap.ReadOnly = true;
            _gridSnap.RowHeadersVisible = false;
            _gridSnap.BackgroundColor = Color.White;
            _gridSnap.BorderStyle = BorderStyle.FixedSingle;
            _gridSnap.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _gridSnap.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _gridSnap.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _gridSnap.ColumnHeadersHeight = 28;
            _gridSnap.RowTemplate.Height = 24;
            _gridSnap.Columns.Add(MakeTextColumn("colSName", "固定文件", 260F, DataGridViewContentAlignment.MiddleLeft));
            _gridSnap.Columns.Add(MakeTextColumn("colSSize", "大小", 80F, DataGridViewContentAlignment.MiddleRight));
            _gridSnap.Columns.Add(MakeTextColumn("colSTime", "修改时间", 140F, DataGridViewContentAlignment.MiddleLeft));
            p.Controls.Add(_gridSnap, 0, 3);

            return p;
        }

        private Control BuildRulesTab()
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 2;
            p.RowCount = 4;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            GroupBox g1 = new GroupBox();
            g1.Text = "始终保留的文件（通配符，一行一条，可用 * 和 ?）";
            g1.Dock = DockStyle.Fill;
            g1.Margin = new Padding(0, 0, 6, 6);
            _txtExcludePatterns = new TextBox();
            _txtExcludePatterns.Multiline = true;
            _txtExcludePatterns.ScrollBars = ScrollBars.Vertical;
            _txtExcludePatterns.Dock = DockStyle.Fill;
            _txtExcludePatterns.Font = new Font("Consolas", 9.5F);
            _txtExcludePatterns.TextChanged += delegate { OnRulesChanged(); };
            g1.Controls.Add(_txtExcludePatterns);
            p.Controls.Add(g1, 0, 0);
            p.SetColumnSpan(g1, 2);

            GroupBox g2 = new GroupBox();
            g2.Text = "始终保留的扩展名（逗号分隔，如 .lnk , .exe）";
            g2.Dock = DockStyle.Fill;
            g2.Margin = new Padding(0, 0, 6, 6);
            _txtExcludeExts = new TextBox();
            _txtExcludeExts.Dock = DockStyle.Top;
            _txtExcludeExts.TextChanged += delegate { OnRulesChanged(); };
            g2.Controls.Add(_txtExcludeExts);
            p.Controls.Add(g2, 0, 1);

            GroupBox g3 = new GroupBox();
            g3.Text = "日期文件夹名格式";
            g3.Dock = DockStyle.Fill;
            g3.Margin = new Padding(0, 0, 0, 6);
            _txtDateFormat = new TextBox();
            _txtDateFormat.Dock = DockStyle.Top;
            _txtDateFormat.TextChanged += delegate { OnRulesChanged(); };
            g3.Controls.Add(_txtDateFormat);
            _lblDateSample = new Label();
            _lblDateSample.Dock = DockStyle.Bottom;
            _lblDateSample.Height = 20;
            _lblDateSample.ForeColor = Color.FromArgb(110, 110, 110);
            g3.Controls.Add(_lblDateSample);
            p.Controls.Add(g3, 1, 1);

            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.Dock = DockStyle.Fill;
            opts.FlowDirection = FlowDirection.TopDown;
            opts.WrapContents = false;
            opts.Margin = new Padding(0, 4, 0, 0);

            _chkReuseDay = new CheckBox();
            _chkReuseDay.Text = "同一天多次整理，合并进同一个日期文件夹";
            _chkReuseDay.AutoSize = true;
            _chkReuseDay.CheckedChanged += delegate { OnRulesChanged(); };
            opts.Controls.Add(_chkReuseDay);

            _chkSnapOn = new CheckBox();
            _chkSnapOn.Text = "启用快照保护（快照内的文件不打包）";
            _chkSnapOn.AutoSize = true;
            _chkSnapOn.CheckedChanged += delegate { OnRulesChanged(); };
            opts.Controls.Add(_chkSnapOn);

            _chkStrictSnap = new CheckBox();
            _chkStrictSnap.Text = "严格匹配快照（名字 + 大小 + 修改时间都要一致）";
            _chkStrictSnap.AutoSize = true;
            _chkStrictSnap.CheckedChanged += delegate { OnRulesChanged(); };
            opts.Controls.Add(_chkStrictSnap);

            _chkOpenAfter = new CheckBox();
            _chkOpenAfter.Text = "整理完成后自动打开日期文件夹";
            _chkOpenAfter.AutoSize = true;
            _chkOpenAfter.CheckedChanged += delegate { OnRulesChanged(); };
            opts.Controls.Add(_chkOpenAfter);

            p.Controls.Add(opts, 0, 2);
            p.SetColumnSpan(opts, 2);

            Label note = new Label();
            note.Dock = DockStyle.Fill;
            note.ForeColor = Color.FromArgb(110, 110, 110);
            note.Text = "设置会自动保存。规则优先级：排除规则 > 快照保护 > 打包。";
            note.Margin = new Padding(0, 0, 0, 0);
            p.Controls.Add(note, 0, 3);
            p.SetColumnSpan(note, 2);

            return p;
        }

        private Control BuildHistoryTab()
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 1;
            p.RowCount = 3;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = false;
            bar.Margin = new Padding(0);

            Button bUndo = new Button();
            bUndo.Text = "撤销选中的整理";
            bUndo.Width = 140;
            bUndo.Click += delegate { UndoSelected(); };
            bar.Controls.Add(bUndo);

            Button bOpen = new Button();
            bOpen.Text = "打开数据目录";
            bOpen.Width = 120;
            bOpen.Click += delegate { OpenPath(AppPaths.Root); };
            bar.Controls.Add(bOpen);

            Button bReload = new Button();
            bReload.Text = "刷新";
            bReload.Width = 70;
            bReload.Click += delegate { RefreshHistory(); };
            bar.Controls.Add(bReload);

            p.Controls.Add(bar, 0, 0);

            _gridHistory = new DataGridView();
            _gridHistory.Dock = DockStyle.Fill;
            _gridHistory.AllowUserToAddRows = false;
            _gridHistory.AllowUserToDeleteRows = false;
            _gridHistory.ReadOnly = true;
            _gridHistory.RowHeadersVisible = false;
            _gridHistory.BackgroundColor = Color.White;
            _gridHistory.BorderStyle = BorderStyle.FixedSingle;
            _gridHistory.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _gridHistory.MultiSelect = false;
            _gridHistory.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _gridHistory.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _gridHistory.ColumnHeadersHeight = 28;
            _gridHistory.RowTemplate.Height = 24;
            _gridHistory.Columns.Add(MakeTextColumn("colHTime", "整理时间", 150F, DataGridViewContentAlignment.MiddleLeft));
            _gridHistory.Columns.Add(MakeTextColumn("colHDir", "目标文件夹", 220F, DataGridViewContentAlignment.MiddleLeft));
            _gridHistory.Columns.Add(MakeTextColumn("colHCount", "文件数", 70F, DataGridViewContentAlignment.MiddleRight));
            _gridHistory.Columns.Add(MakeTextColumn("colHState", "状态", 90F, DataGridViewContentAlignment.MiddleLeft));
            p.Controls.Add(_gridHistory, 0, 1);

            Label note = new Label();
            note.Dock = DockStyle.Fill;
            note.ForeColor = Color.FromArgb(110, 110, 110);
            note.Text = "撤销会把那次打包的文件原样移回桌面（同名文件自动改名，不覆盖）。";
            p.Controls.Add(note, 0, 2);

            return p;
        }

        private Control BuildFooter()
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.ColumnCount = 3;
            p.RowCount = 1;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

            _status = new Label();
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Color.FromArgb(60, 60, 60);
            p.Controls.Add(_status, 0, 0);

            _chkDryRun = new CheckBox();
            _chkDryRun.Text = "先试运行（不移动文件）";
            _chkDryRun.Dock = DockStyle.Fill;
            _chkDryRun.TextAlign = ContentAlignment.MiddleLeft;
            _chkDryRun.Checked = false;
            p.Controls.Add(_chkDryRun, 1, 0);

            _btnTidy = new Button();
            _btnTidy.Text = "一键整理";
            _btnTidy.Dock = DockStyle.Fill;
            _btnTidy.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            _btnTidy.BackColor = Color.FromArgb(0, 120, 215);
            _btnTidy.ForeColor = Color.White;
            _btnTidy.FlatStyle = FlatStyle.Flat;
            _btnTidy.FlatAppearance.BorderSize = 0;
            _btnTidy.Margin = new Padding(0, 4, 0, 4);
            _btnTidy.Click += delegate { RunTidy(); };
            p.Controls.Add(_btnTidy, 2, 0);

            return p;
        }

        // ------------------------------------------------------------ 数据刷新

        private void LoadSettingsIntoUi()
        {
            _loadingUi = true;
            try
            {
                _txtDesktop.Text = _cfg.DesktopPath;
                _txtExcludePatterns.Text = string.Join(Environment.NewLine, _cfg.ExcludePatterns);
                _txtExcludeExts.Text = string.Join(", ", _cfg.ExcludeExtensions);
                _txtDateFormat.Text = _cfg.DateFormat;
                _chkReuseDay.Checked = _cfg.ReuseSameDayFolder;
                _chkSnapOn.Checked = _cfg.ProtectSnapshot;
                _chkStrictSnap.Checked = _cfg.StrictSnapshotMatch;
                _chkOpenAfter.Checked = _cfg.OpenFolderAfterTidy;
            }
            finally
            {
                _loadingUi = false;
            }
            UpdateDateSample();
        }

        private void ReloadSnapshotAndPlan()
        {
            _snapshot = SnapshotStore.Load();
            _cfg.DesktopPath = _txtDesktop.Text.Trim();
            _cfg = AppConfig.Normalize(_cfg);
            RebuildPlan();
            RefreshSnapshotGrid();
            RefreshHistory();
        }

        private void RefreshAll()
        {
            RebuildPlan();
            RefreshSnapshotGrid();
            UpdateDateSample();
        }

        private void RebuildPlan()
        {
            _cfg.DesktopPath = _txtDesktop.Text.Trim();

            if (!Directory.Exists(_cfg.DesktopPath))
            {
                _plan = new TidyPlan();
                _gridPlan.Rows.Clear();
                _lblSummary.Text = "桌面路径不存在，请重新选择。";
                _lblSummary.ForeColor = Color.Firebrick;
                _lblPlanInfo.Text = "";
                _btnTidy.Enabled = false;
                return;
            }

            _lblSummary.ForeColor = Color.FromArgb(70, 70, 70);
            _plan = Organizer.BuildPlan(_cfg, _snapshot, DateTime.Now);
            FillPlanGrid(_plan);
            _btnTidy.Enabled = true;
            UpdateSummary();
        }

        private void FillPlanGrid(TidyPlan plan)
        {
            _fillingGrid = true;
            try
            {
                _gridPlan.SuspendLayout();
                _gridPlan.Rows.Clear();
                foreach (PlanItem item in plan.Items)
                {
                    int idx = _gridPlan.Rows.Add(
                        item.WillPack,
                        item.Name,
                        FsUtil.FormatSize(item.Length),
                        item.LastWrite == DateTime.MinValue ? "-" : item.LastWrite.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        DecisionText(item),
                        item.Reason);
                    _gridPlan.Rows[idx].Tag = item;
                    if (item.Decision == PackDecision.ProtectedSnap || item.Decision == PackDecision.ProtectedRule)
                        _gridPlan.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(130, 130, 130);
                }
                _gridPlan.ResumeLayout();
            }
            finally
            {
                _fillingGrid = false;
            }
        }

        private static string DecisionText(PlanItem item)
        {
            switch (item.Decision)
            {
                case PackDecision.Pack: return "打包";
                case PackDecision.ProtectedSnap: return "保留（快照）";
                case PackDecision.ProtectedRule: return "保留（规则）";
                case PackDecision.IsDirectory: return "文件夹跳过";
                default: return "跳过";
            }
        }

        private void UpdateSummary()
        {
            if (_plan == null) return;

            int total = _plan.Items.Count;
            int pack = _plan.PackCount;
            int keep = total - pack;

            string snapText;
            if (_snapshot == null || _snapshot.Entries.Count == 0)
                snapText = "快照：未建立";
            else
                snapText = string.Format("快照：{0} 个固定文件（{1}）",
                    _snapshot.Entries.Count, _snapshot.CreatedLocal);

            _lblSummary.Text = string.Format(
                "桌面文件 {0} 个　|　本次打包 {1} 个（{2}）　|　保留 {3} 个　|　{4}",
                total, pack, FsUtil.FormatSize(_plan.PackBytes), keep, snapText);

            _lblPlanInfo.Text = string.Format("目标文件夹：{0}",
                Path.Combine(_plan.DesktopPath, _plan.FolderName));
        }

        private void RefreshSnapshotGrid()
        {
            _gridSnap.SuspendLayout();
            _gridSnap.Rows.Clear();

            if (_snapshot == null || _snapshot.Entries.Count == 0)
            {
                _lblSnapInfo.ForeColor = Color.Firebrick;
                _lblSnapInfo.Text = "尚未建立快照 —— 当前所有桌面文件都会被判定为「新增」。请先点左侧「建立 / 更新快照」。";
                _btnSnapClear.Enabled = false;
            }
            else
            {
                _lblSnapInfo.ForeColor = Color.FromArgb(0, 110, 60);
                _lblSnapInfo.Text = string.Format("快照时间：{0}　共 {1} 个固定文件",
                    _snapshot.CreatedLocal, _snapshot.Entries.Count);
                _btnSnapClear.Enabled = true;

                foreach (SnapshotEntry e in _snapshot.Entries)
                {
                    DateTime t = new DateTime(e.LastWriteUtcTicks, DateTimeKind.Utc).ToLocalTime();
                    _gridSnap.Rows.Add(e.Name, FsUtil.FormatSize(e.Length),
                        t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                }
            }
            _gridSnap.ResumeLayout();
        }

        private void RefreshHistory()
        {
            if (_gridHistory == null) return;
            _history = UndoLogStore.ListAll();

            _gridHistory.SuspendLayout();
            _gridHistory.Rows.Clear();
            foreach (UndoLogEntry e in _history)
            {
                _gridHistory.Rows.Add(
                    e.Data.CreatedLocal,
                    e.Data.TargetDir,
                    e.Data.Items.Count.ToString(CultureInfo.InvariantCulture),
                    e.Data.Undone ? "已撤销" : "有效");
            }
            _gridHistory.ResumeLayout();
        }

        private void UpdateDateSample()
        {
            if (_lblDateSample == null) return;
            string name = Organizer.BuildFolderName(_cfg, DateTime.Now);
            _lblDateSample.Text = "示例：" + name;
        }

        // ------------------------------------------------------------ 交互

        private void GridPlanCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_fillingGrid) return;
            if (e.RowIndex < 0 || e.ColumnIndex != _gridPlan.Columns["colPack"].Index) return;

            DataGridViewRow row = _gridPlan.Rows[e.RowIndex];
            PlanItem item = row.Tag as PlanItem;
            if (item == null) return;

            object v = row.Cells["colPack"].Value;
            bool wanted = v is bool && (bool)v;

            if (wanted == (item.Decision == PackDecision.Pack)) item.UserChoice = null;
            else item.UserChoice = wanted;

            UpdateSummary();
        }

        private void SetAllChoices(bool pack)
        {
            foreach (PlanItem item in _plan.Items)
                item.UserChoice = pack ? (bool?)true : false;

            _fillingGrid = true;
            try
            {
                foreach (DataGridViewRow row in _gridPlan.Rows)
                {
                    PlanItem item = row.Tag as PlanItem;
                    if (item != null) row.Cells["colPack"].Value = item.WillPack;
                }
            }
            finally
            {
                _fillingGrid = false;
            }
            UpdateSummary();
        }

        private void ResetChoices()
        {
            foreach (PlanItem item in _plan.Items) item.UserChoice = null;
            _fillingGrid = true;
            try
            {
                foreach (DataGridViewRow row in _gridPlan.Rows)
                {
                    PlanItem item = row.Tag as PlanItem;
                    if (item != null) row.Cells["colPack"].Value = item.WillPack;
                }
            }
            finally
            {
                _fillingGrid = false;
            }
            UpdateSummary();
        }

        private void BrowseDesktop()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择要整理的桌面文件夹";
                d.SelectedPath = Directory.Exists(_txtDesktop.Text) ? _txtDesktop.Text : AppPaths.DefaultDesktopPath();
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    _txtDesktop.Text = d.SelectedPath;
                    ReloadSnapshotAndPlan();
                }
            }
        }

        private void CaptureSnapshot()
        {
            if (!Directory.Exists(_cfg.DesktopPath))
            {
                MessageBox.Show(this, "桌面路径不存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int count = Directory.GetFiles(_cfg.DesktopPath).Length;
            if (_snapshot != null && _snapshot.Entries.Count > 0)
            {
                DialogResult r = MessageBox.Show(this,
                    string.Format("已有快照包含 {0} 个文件，现在会被桌面当前的 {1} 个文件覆盖。\n\n"
                        + "注意：如果桌面已经有新增文件，先整理，再更新快照，否则这些新增文件也会被当成固定文件。\n\n继续吗？",
                        _snapshot.Entries.Count, count),
                    "更新快照", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            _snapshot = SnapshotStore.Capture(_cfg.DesktopPath);
            SnapshotStore.Save(_snapshot);

            RefreshSnapshotGrid();
            RebuildPlan();
            _status.Text = string.Format("已建立快照：{0} 个文件被设为固定。", _snapshot.Entries.Count);
        }

        private void ClearSnapshot()
        {
            if (MessageBox.Show(this, "删除快照后，桌面上所有文件都会被当作「新增文件」打包。确定删除吗？",
                "删除快照", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            SnapshotStore.Delete();
            _snapshot = null;
            RefreshSnapshotGrid();
            RebuildPlan();
            _status.Text = "已删除快照。";
        }

        private void OnRulesChanged()
        {
            // 装载设置时控件的 TextChanged / CheckedChanged 会依次触发，
            // 此刻其它控件还是默认值，若照常回写会把用户配置清成默认值。
            if (_loadingUi) return;
            if (_txtExcludePatterns == null) return;

            _cfg.DesktopPath = _txtDesktop.Text.Trim();
            _cfg.ExcludePatterns = Wildcards.Split(_txtExcludePatterns.Text).ToArray();
            _cfg.ExcludeExtensions = SplitList(_txtExcludeExts.Text);
            _cfg.DateFormat = _txtDateFormat.Text.Trim();
            _cfg.ReuseSameDayFolder = _chkReuseDay.Checked;
            _cfg.ProtectSnapshot = _chkSnapOn.Checked;
            _cfg.StrictSnapshotMatch = _chkStrictSnap.Checked;
            _cfg.OpenFolderAfterTidy = _chkOpenAfter.Checked;

            try
            {
                _cfg.Save();
                _status.Text = "设置已保存。";
            }
            catch (Exception ex)
            {
                _status.Text = "设置保存失败：" + ex.Message;
            }

            UpdateDateSample();
            if (_plan != null) RebuildPlan();
        }

        private static string[] SplitList(string text)
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(text))
            {
                string[] parts = text.Split(new char[] { ',', ';', ' ', '\t', '|' });
                foreach (string raw in parts)
                {
                    string s = raw.Trim();
                    if (s.Length == 0) continue;
                    list.Add(s);
                }
            }
            return list.ToArray();
        }

        private void RunTidy()
        {
            if (_plan == null || !Directory.Exists(_cfg.DesktopPath)) return;

            _cfg.DateFormat = _txtDateFormat.Text.Trim();
            _plan = Organizer.BuildPlan(_cfg, _snapshot, DateTime.Now);

            // 保留界面上的手动勾选
            Dictionary<string, bool?> choices = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _gridPlan.Rows)
            {
                PlanItem it = row.Tag as PlanItem;
                if (it == null) continue;
                choices[it.Name] = it.UserChoice;
            }
            foreach (PlanItem it in _plan.Items)
            {
                bool? c;
                if (choices.TryGetValue(it.Name, out c)) it.UserChoice = c;
            }

            List<PlanItem> packItems = _plan.PackItems;
            if (packItems.Count == 0)
            {
                MessageBox.Show(this, "桌面上没有需要打包的新增文件。", "无需整理",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool dryRun = _chkDryRun.Checked;
            if (!dryRun)
            {
                string target = _plan.TargetDir;
                DialogResult r = MessageBox.Show(this,
                    string.Format("将把 {0} 个文件（{1}）移动到：\n{2}\n\n文件夹和其他保留文件不受影响。继续吗？",
                        packItems.Count, FsUtil.FormatSize(_plan.PackBytes), target),
                    "确认整理", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }

            Cursor = Cursors.WaitCursor;
            _btnTidy.Enabled = false;
            try
            {
                TidyResult res = Organizer.Execute(_cfg, _plan, dryRun);

                if (!dryRun && res.MovedCount > 0 && _chkOpenAfter.Checked)
                    OpenPath(res.TargetDir);

                if (res.Errors.Count > 0)
                {
                    string detail = string.Join(Environment.NewLine, res.Errors.ToArray(), 0, Math.Min(10, res.Errors.Count));
                    MessageBox.Show(this, res.Message + Environment.NewLine + Environment.NewLine + detail,
                        dryRun ? "试运行结果" : "部分文件未能移动",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    _status.Text = res.Message;
                    if (dryRun)
                        MessageBox.Show(this, res.Message, "试运行结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                // 整理后刷新：已移走的文件不再出现
                _snapshot = SnapshotStore.Load();
                RebuildPlan();
                RefreshHistory();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "整理失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                _btnTidy.Enabled = true;
            }
        }

        private void UndoSelected()
        {
            if (_gridHistory.CurrentRow == null)
            {
                MessageBox.Show(this, "请先在列表里选中一条整理记录。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int idx = _gridHistory.CurrentRow.Index;
            if (idx < 0 || idx >= _history.Count) return;
            UndoLogEntry entry = _history[idx];

            if (entry.Data.Undone)
            {
                MessageBox.Show(this, "这次整理已经撤销过了。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                string.Format("把 {0} 个文件从「{1}」移回桌面？", entry.Data.Items.Count, entry.Data.TargetDir),
                "确认撤销", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            TidyResult res = Organizer.Undo(entry, false);
            MessageBox.Show(this, res.Message + (res.Errors.Count > 0
                ? Environment.NewLine + string.Join(Environment.NewLine, res.Errors.ToArray(), 0, Math.Min(10, res.Errors.Count))
                : ""), "撤销结果", MessageBoxButtons.OK,
                res.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            _status.Text = res.Message;
            RefreshHistory();
            RebuildPlan();
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (Directory.Exists(path) || File.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }
    }
}
