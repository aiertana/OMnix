using System.Text;

namespace OilFXTool;

public sealed class MainForm : Form
{
    private readonly OilFxEngine _engine = new();
    private string _modsRoot = string.Empty;

    private TextBox _pathBox = null!;
    private Label _frameworkLabel = null!;
    private Button _installButton = null!;
    private TrackBar _strength = null!;
    private Label _strengthValue = null!;
    private CheckBox _realistic = null!;
    private ComboBox _skin = null!;
    private TreeView _tree = null!;
    private Label _status = null!;
    private bool _syncing;
    private bool _promptedInstall;

    private static readonly string[] DefaultModsRoots =
    [
        @"D:\mod\XXMI Launcher\WWMI\Mods",
        @"C:\XXMI Launcher\WWMI\Mods",
        @"D:\XXMI Launcher\WWMI\Mods",
        @"D:\Games\XXMI Launcher\WWMI\Mods",
        @"C:\Program Files\XXMI Launcher\WWMI\Mods",
    ];

    public MainForm()
    {
        Text = "CustomOilFX 油光/湿身效果注入工具 v1.0";
        MinimumSize = new Size(720, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 680);
        Font = new Font("Microsoft YaHei UI", 9f);

        BuildUi();
        var detected = DefaultModsRoots.FirstOrDefault(Directory.Exists);
        if (detected is not null)
        {
            _pathBox.Text = detected;
            LoadRoot(detected);
        }
        else
        {
            SetStatus("未自动找到 Mods 目录，请手动选择（XXMI Launcher 目录下的 WWMI\\Mods）");
        }
    }

    private void BuildUi()
    {
        // ── 顶部：目录选择 ──
        var top = new GroupBox { Text = "游戏 Mods 目录", Dock = DockStyle.Top, Height = 78, Padding = new Padding(10, 6, 10, 6) };
        _pathBox = new TextBox { Dock = DockStyle.Top, Width = 500 };
        var browse = new Button { Text = "浏览…", Dock = DockStyle.Left, Width = 70 };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "选择 XXMI 的 Mods 目录（含 Matcap.ini / 各角色 mod 文件夹）" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dlg.SelectedPath;
                LoadRoot(dlg.SelectedPath);
            }
        };
        var loadBtn = new Button { Text = "加载", Dock = DockStyle.Right, Width = 60 };
        loadBtn.Click += (_, _) => LoadRoot(_pathBox.Text.Trim());
        var row1 = new Panel { Dock = DockStyle.Top, Height = 30 };
        row1.Controls.AddRange([loadBtn, browse, _pathBox]);
        _frameworkLabel = new Label { Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
        _installButton = new Button { Text = "安装 / 修复框架", Dock = DockStyle.Right, Width = 130, Height = 26, Enabled = false };
        _installButton.Click += (_, _) => InstallFramework();
        var row2 = new Panel { Dock = DockStyle.Top, Height = 28 };
        row2.Controls.Add(_frameworkLabel);
        row2.Controls.Add(_installButton);
        top.Controls.AddRange([row1, row2]);

        // ── 参数区 ──
        var param = new GroupBox { Text = "效果参数（写进 Matcap.ini，游戏内 Alt+小键盘 0/1/2 可临时调）", Dock = DockStyle.Top, Height = 76, Padding = new Padding(10, 6, 10, 6) };
        _strength = new TrackBar { Dock = DockStyle.Left, Width = 220, Minimum = 0, Maximum = 20, TickFrequency = 2 };
        _strength.ValueChanged += (_, _) =>
        {
            _strengthValue.Text = (_strength.Value / 10.0).ToString("0.0");
            SaveParams();
        };
        _strengthValue = new Label { Dock = DockStyle.Left, Width = 32, TextAlign = ContentAlignment.MiddleCenter, Text = "0.7" };
        _realistic = new CheckBox { Dock = DockStyle.Left, Width = 90, Text = "写实", Padding = new Padding(6, 8, 0, 0) };
        _realistic.CheckedChanged += (_, _) => SaveParams();
        _skin = new ComboBox { Dock = DockStyle.Left, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        _skin.Items.AddRange(["0 · 禁用", "1 · 默认油光", "2 · 油光 A", "3 · 油光 B", "4 · 油光 C", "5 · 油光 D", "6 · 湿身"]);
        _skin.SelectedIndexChanged += (_, _) => SaveParams();
        var paramRow = new Panel { Dock = DockStyle.Fill };
        paramRow.Controls.AddRange([_skin, _realistic, _strengthValue, _strength]);
        param.Controls.Add(paramRow);

        // ── mod 列表 ──
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            HideSelection = true,
            ShowLines = false,
            FullRowSelect = true,
        };
        _tree.BeforeCheck += (_, e) =>
        {
            if (_syncing)
            {
                e.Cancel = true;
            }
        };
        _tree.AfterCheck += Tree_AfterCheck;

        // ── 底部按钮 + 状态 ──
        var allInject = new Button { Text = "全选注入", Dock = DockStyle.Left, Width = 90 };
        allInject.Click += (_, _) => ApplyAll(true);
        var allRestore = new Button { Text = "全部还原", Dock = DockStyle.Left, Width = 90 };
        allRestore.Click += (_, _) => ApplyAll(false);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(6, 4, 6, 4) };
        var bottomRow = new Panel { Dock = DockStyle.Top, Height = 30 };
        bottomRow.Controls.AddRange([allInject, allRestore]);
        _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.ControlText };
        bottom.Controls.AddRange([_status, bottomRow]);

        Controls.Add(_tree);
        Controls.Add(param);
        Controls.Add(top);
        Controls.Add(bottom);

        // Dock 布局顺序：Fill 先加入，Bottom/Top 后加入正确停靠
        Controls.SetChildIndex(bottom, 0);
        Controls.SetChildIndex(_tree, 1);
        Controls.SetChildIndex(param, 2);
        Controls.SetChildIndex(top, 3);
    }

    private void LoadRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            SetStatus("目录不存在");
            return;
        }

        _modsRoot = root.TrimEnd(Path.DirectorySeparatorChar);
        _installButton.Enabled = !_engine.IsFrameworkInstalled(_modsRoot) && Directory.Exists(ResourceDir);
        if (_engine.IsFrameworkInstalled(_modsRoot))
        {
            _frameworkLabel.Text = "框架状态：已安装";
            _frameworkLabel.ForeColor = Color.ForestGreen;
            var (strength, realistic, skin) = _engine.ReadParams(_modsRoot);
            _syncing = true;
            _strength.Value = Math.Clamp((int)Math.Round(strength * 10), 0, 20);
            _strengthValue.Text = (_strength.Value / 10.0).ToString("0.0");
            _realistic.Checked = realistic;
            _skin.SelectedIndex = skin;
            _syncing = false;
            ReloadTree();
        }
        else
        {
            _frameworkLabel.Text = Directory.Exists(ResourceDir)
                ? "框架状态：未安装（点右侧按钮一键安装 Matcap.ini + 油光贴图）"
                : "框架状态：未安装（缺少 Resources 资源，请确认 OilFXTool.exe 与 Resources 文件夹在同一目录）";
            _frameworkLabel.ForeColor = Color.Firebrick;
            _tree.Nodes.Clear();
            SetStatus("请先安装框架再注入");

            // 首次启动初始化：框架没装就主动询问并自动补全（Matcap.ini + 油光贴图）
            if (!_promptedInstall && Directory.Exists(ResourceDir))
            {
                _promptedInstall = true;
                var choice = MessageBox.Show(
                    this,
                    "检测到尚未安装 OilFX 框架。\n\n是否立即初始化？（安装 Matcap.ini + 油光贴图到 Mods 目录，之后即可勾选注入）",
                    "OilFX 首次初始化",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (choice == DialogResult.Yes)
                {
                    InstallFramework();
                }
            }
        }
    }

    private string ResourceDir => Path.Combine(AppContext.BaseDirectory, "Resources");

    private void InstallFramework()
    {
        try
        {
            var n = _engine.InstallFramework(_modsRoot, ResourceDir);
            _engine.WriteParams(_modsRoot, _strength.Value / 10.0, _realistic.Checked, Math.Max(_skin.SelectedIndex, 2));
            SetStatus($"框架安装完成（复制 {n} 个文件），键位 Alt+小键盘 0/1/2 已就绪");
            _installButton.Enabled = false;
            _frameworkLabel.Text = "框架状态：已安装";
            _frameworkLabel.ForeColor = Color.ForestGreen;
            ReloadTree();
        }
        catch (Exception ex)
        {
            SetStatus("框架安装失败：" + ex.Message);
        }
    }

    private void ReloadTree()
    {
        _syncing = true;
        _tree.Nodes.Clear();

        var groups = _engine.ScanModsRoot(_modsRoot);
        // 已注入的角色排前面
        groups = groups
            .OrderByDescending(g => g.Mods.Any(m => m.Injected))
            .ThenBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            var injectedCount = group.Mods.Count(m => m.Injected);
            var groupNode = new TreeNode(
                $"{group.GroupName}  [{injectedCount}/{group.Mods.Count} 已注入]" +
                (group.DisabledModsCount > 0 ? $"  (另有 {group.DisabledModsCount} 个禁用 mod 未列出)" : string.Empty))
            {
                Tag = null,
                Checked = group.Mods.Count > 0 && group.Mods.All(m => m.Injected),
                ForeColor = injectedCount > 0 ? Color.ForestGreen : SystemColors.WindowText,
            };
            foreach (var mod in group.Mods)
            {
                groupNode.Nodes.Add(MakeModNode(mod));
            }

            _tree.Nodes.Add(groupNode);
        }

        var totalMods = groups.Sum(g => g.Mods.Count);
        var totalInis = groups.Sum(g => g.Mods.Sum(m => m.Inis.Count));
        var injectedInis = groups.Sum(g => g.Mods.Sum(m => m.InjectedCount));
        SetStatus($"共 {groups.Count} 组 / {totalMods} 个 mod / {totalInis} 个 ini，已注入 {injectedInis} 个 ini");
        _syncing = false;
    }

    private static TreeNode MakeModNode(OilFxModInfo mod)
    {
        return new TreeNode
        {
            Text = $"{mod.FolderName}  [已注入 {mod.InjectedCount}/{mod.Inis.Count}]",
            Tag = mod,
            Checked = mod.Injected,
            ForeColor = mod.InjectedCount > 0 ? Color.ForestGreen : SystemColors.WindowText,
        };
    }

    private void Tree_AfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_syncing || e.Node is null)
        {
            return;
        }

        // 组节点勾选 = 全组 mod 同状态；mod 节点 = 注入/还原单个 mod
        if (e.Node.Tag is null)
        {
            foreach (TreeNode child in e.Node.Nodes)
            {
                if (child.Tag is OilFxModInfo mod)
                {
                    ApplyMod(mod, e.Node.Checked);
                }
            }

            _syncing = true;
            ReloadTreePreservingExpansion();
            _syncing = false;
            return;
        }

        if (e.Node.Tag is OilFxModInfo single)
        {
            ApplyMod(single, e.Node.Checked);
            _syncing = true;
            ReloadTreePreservingExpansion();
            _syncing = false;
        }
    }

    private void ApplyMod(OilFxModInfo mod, bool target)
    {
        try
        {
            var changed = 0;
            foreach (var ini in mod.Inis)
            {
                if (target && !ini.Injected)
                {
                    changed += _engine.InjectIni(ini.IniPath);
                }
                else if (!target && ini.Injected && ini.HasBackup)
                {
                    if (_engine.RestoreIni(ini.IniPath))
                    {
                        changed++;
                    }
                }
            }

            SetStatus(target
                ? $"{mod.FolderName}: 注入 {changed} 行"
                : $"{mod.FolderName}: 还原 {changed} 个 ini");
        }
        catch (Exception ex)
        {
            SetStatus($"操作 {mod.FolderName} 失败：{ex.Message}");
        }
    }

    private void ReloadTreePreservingExpansion()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpanded(_tree.Nodes, expanded);
        ReloadTree();
        RestoreExpansion(_tree.Nodes, expanded);
    }

    private static void CollectExpanded(TreeNodeCollection nodes, HashSet<string> set)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.IsExpanded && n.Tag is null)
            {
                var name = n.Text.Split("  [")[0];
                set.Add(name);
            }

            CollectExpanded(n.Nodes, set);
        }
    }

    private void RestoreExpansion(TreeNodeCollection nodes, HashSet<string> set)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is null && set.Contains(n.Text.Split("  [")[0]))
            {
                n.Expand();
            }

            RestoreExpansion(n.Nodes, set);
        }
    }

    private void ApplyAll(bool target)
    {
        if (!IsFrameworkReady())
        {
            return;
        }

        SetStatus(target ? "正在注入全部…" : "正在还原全部…");
        var count = 0;
        foreach (var group in _engine.ScanModsRoot(_modsRoot))
        {
            foreach (var mod in group.Mods)
            {
                foreach (var ini in mod.Inis)
                {
                    if (target && !ini.Injected)
                    {
                        count += _engine.InjectIni(ini.IniPath);
                    }
                    else if (!target && ini.Injected && ini.HasBackup && _engine.RestoreIni(ini.IniPath))
                    {
                        count++;
                    }
                }
            }
        }

        SetStatus(target ? $"全部注入完成：{count} 行" : $"全部还原完成：{count} 个 ini");
        ReloadTree();
    }

    private bool IsFrameworkReady()
    {
        if (!_engine.IsFrameworkInstalled(_modsRoot))
        {
            SetStatus("框架未安装，请先点\"安装 / 修复框架\"");
            return false;
        }

        return true;
    }

    private void SaveParams()
    {
        if (_syncing || string.IsNullOrEmpty(_modsRoot) || !_engine.IsFrameworkInstalled(_modsRoot))
        {
            return;
        }

        try
        {
            _engine.WriteParams(_modsRoot, _strength.Value / 10.0, _realistic.Checked, Math.Max(_skin.SelectedIndex, 0));
        }
        catch (Exception ex)
        {
            SetStatus("参数写入失败：" + ex.Message);
        }
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
    }
}
