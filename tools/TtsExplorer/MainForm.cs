using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TtsExplorer;

public sealed class MainForm : Form
{
    private readonly TtsSaveLoader _loader = new();

    private readonly SplitContainer _mainSplit = new();
    private readonly SplitContainer _rightSplit = new();
    private readonly TextBox _searchBox = new();
    private readonly Button _openButton = new();
    private readonly Button _exportButton = new();
    private readonly TreeView _tree = new();
    private readonly TextBox _details = new();
    private readonly TabControl _tabs = new();
    private readonly TextBox _luaText = new();
    private readonly TextBox _xmlText = new();
    private readonly TextBox _jsonText = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    public MainForm()
    {
        Text = "TTS Explorer - X-Wing Unified 1E Dev Tool";
        Width = 1450;
        Height = 900;

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 42 };
        _openButton.Text = "Open TTS JSON";
        _openButton.Left = 8;
        _openButton.Top = 8;
        _openButton.Width = 130;
        _openButton.Click += (_, _) => OpenJson();

        _exportButton.Text = "Export Scripts";
        _exportButton.Left = 146;
        _exportButton.Top = 8;
        _exportButton.Width = 110;
        _exportButton.Enabled = false;
        _exportButton.Click += (_, _) => ExportScripts();

        _searchBox.Left = 270;
        _searchBox.Top = 9;
        _searchBox.Width = 500;
        _searchBox.PlaceholderText = "Search GUID, nickname, URL, script text...";
        _searchBox.TextChanged += (_, _) => PopulateTree();

        topPanel.Controls.Add(_openButton);
        topPanel.Controls.Add(_exportButton);
        topPanel.Controls.Add(_searchBox);

        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.SplitterDistance = 440;

        _tree.Dock = DockStyle.Fill;
        _tree.AfterSelect += (_, e) => ShowNode(e.Node);
        _mainSplit.Panel1.Controls.Add(_tree);

        _rightSplit.Dock = DockStyle.Fill;
        _rightSplit.Orientation = Orientation.Horizontal;
        _rightSplit.SplitterDistance = 220;

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.ReadOnly = true;
        _details.Font = new Font("Consolas", 10);

        _tabs.Dock = DockStyle.Fill;
        _luaText.Multiline = true;
        _luaText.ScrollBars = ScrollBars.Both;
        _luaText.ReadOnly = true;
        _luaText.Font = new Font("Consolas", 10);

        _xmlText.Multiline = true;
        _xmlText.ScrollBars = ScrollBars.Both;
        _xmlText.ReadOnly = true;
        _xmlText.Font = new Font("Consolas", 10);

        _jsonText.Multiline = true;
        _jsonText.ScrollBars = ScrollBars.Both;
        _jsonText.ReadOnly = true;
        _jsonText.Font = new Font("Consolas", 10);

        var luaPage = new TabPage("Lua");
        luaPage.Controls.Add(_luaText);

        var xmlPage = new TabPage("XML UI");
        xmlPage.Controls.Add(_xmlText);

        var jsonPage = new TabPage("Raw JSON");
        jsonPage.Controls.Add(_jsonText);

        _tabs.TabPages.Add(luaPage);
        _tabs.TabPages.Add(xmlPage);
        _tabs.TabPages.Add(jsonPage);

        _rightSplit.Panel1.Controls.Add(_details);
        _rightSplit.Panel2.Controls.Add(_tabs);
        _mainSplit.Panel2.Controls.Add(_rightSplit);

        _status.Items.Add(_statusLabel);

        Controls.Add(_mainSplit);
        Controls.Add(topPanel);
        Controls.Add(_status);
    }

    private void OpenJson()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "TTS JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open Tabletop Simulator JSON"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            _loader.Load(dialog.FileName);
            _details.Text = _loader.BuildSummary();
            _exportButton.Enabled = true;
            PopulateTree();
            _statusLabel.Text = $"Loaded {_loader.Objects.Count} objects from {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PopulateTree()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        var q = _searchBox.Text.Trim();

        var root = new TreeNode("Save")
        {
            Tag = "summary"
        };

        var global = new TreeNode($"Global Script ({_loader.GlobalLua.Length:N0} chars)")
        {
            Tag = "global"
        };
        root.Nodes.Add(global);

        IEnumerable<TtsObjectInfo> objects = _loader.Objects;

        if (!string.IsNullOrWhiteSpace(q))
        {
            objects = objects.Where(o =>
                Contains(o.Guid, q) ||
                Contains(o.Name, q) ||
                Contains(o.Nickname, q) ||
                Contains(o.Description, q) ||
                Contains(o.MeshUrl, q) ||
                Contains(o.DiffuseUrl, q) ||
                Contains(o.ColliderUrl, q) ||
                Contains(TtsSaveLoader.GetString(o.Source, "LuaScript"), q));
        }

        foreach (var obj in objects.OrderBy(o => o.Nickname).ThenBy(o => o.Name))
        {
            var node = new TreeNode(obj.DisplayName) { Tag = obj };
            root.Nodes.Add(node);
        }

        _tree.Nodes.Add(root);
        root.Expand();
        _tree.EndUpdate();

        _statusLabel.Text = $"{root.Nodes.Count - 1} objects shown";
    }

    private static bool Contains(string value, string q)
    {
        return value?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ShowNode(TreeNode? node)
    {
        if (node?.Tag is null) return;

        if (node.Tag is string s && s == "summary")
        {
            _details.Text = _loader.BuildSummary();
            _luaText.Clear();
            _xmlText.Clear();
            _jsonText.Clear();
            return;
        }

        if (node.Tag is string g && g == "global")
        {
            _details.Text = "Global script/UI";
            _luaText.Text = _loader.GlobalLua;
            _xmlText.Text = _loader.GlobalXml;
            _jsonText.Text = _loader.Root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";
            return;
        }

        if (node.Tag is TtsObjectInfo obj)
        {
            var lua = TtsSaveLoader.GetString(obj.Source, "LuaScript");
            var xml = TtsSaveLoader.GetString(obj.Source, "XmlUI");

            _details.Text =
$@"GUID: {obj.Guid}
Name: {obj.Name}
Nickname: {obj.Nickname}
Description: {obj.Description}
Path: {obj.Path}

Lua length: {obj.LuaLength:N0}
XML length: {obj.XmlLength:N0}

Mesh:
{obj.MeshUrl}

Diffuse:
{obj.DiffuseUrl}

Collider:
{obj.ColliderUrl}";

            _luaText.Text = lua;
            _xmlText.Text = xml;
            _jsonText.Text = obj.Source.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private void ExportScripts()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose export folder"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var root = Path.Combine(dialog.SelectedPath, "tts-explorer-export");
        var scripts = Path.Combine(root, "scripts");
        var ui = Path.Combine(root, "ui");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(ui);

        File.WriteAllText(Path.Combine(scripts, "Global.lua"), _loader.GlobalLua, Encoding.UTF8);
        File.WriteAllText(Path.Combine(ui, "Global.xml"), _loader.GlobalXml, Encoding.UTF8);

        foreach (var obj in _loader.Objects)
        {
            var safe = MakeSafeFileName($"{obj.Guid}_{(string.IsNullOrWhiteSpace(obj.Nickname) ? obj.Name : obj.Nickname)}");
            var lua = TtsSaveLoader.GetString(obj.Source, "LuaScript");
            var xml = TtsSaveLoader.GetString(obj.Source, "XmlUI");

            if (!string.IsNullOrWhiteSpace(lua))
                File.WriteAllText(Path.Combine(scripts, $"{safe}.lua"), lua, Encoding.UTF8);

            if (!string.IsNullOrWhiteSpace(xml))
                File.WriteAllText(Path.Combine(ui, $"{safe}.xml"), xml, Encoding.UTF8);
        }

        MessageBox.Show(this, $"Exported to {root}", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Length > 160 ? value[..160] : value;
    }
}
