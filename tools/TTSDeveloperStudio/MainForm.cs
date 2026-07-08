using System.Text;
using System.Text.Json;

namespace TtsExplorer;

public sealed class MainForm : Form
{
    private readonly TtsSaveLoader _loader = new();

    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripButton _openButton = new("Open TTS JSON");
    private readonly ToolStripButton _exportButton = new("Export Scripts");
    private readonly ToolStripSeparator _toolbarSeparator1 = new();
    private readonly ToolStripLabel _searchLabel = new("Search objects:");
    private readonly ToolStripTextBox _searchBox = new();
    private readonly ToolStripButton _clearSearchButton = new("Clear");
    private readonly ToolStripSeparator _toolbarSeparator2 = new();
    private readonly ToolStripLabel _findLabel = new("Find:");
    private readonly ToolStripTextBox _findBox = new();
    private readonly ToolStripButton _findPreviousButton = new("Previous");
    private readonly ToolStripButton _findNextButton = new("Next");

    private readonly SplitContainer _mainSplit = new();
    private readonly SplitContainer _rightSplit = new();
    private readonly TreeView _tree = new();
    private readonly TabControl _tabs = new();
    private readonly RichTextBox _luaText = new();
    private readonly RichTextBox _xmlText = new();
    private readonly RichTextBox _jsonText = new();
    private readonly RichTextBox _details = new();

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _objectCountLabel = new();
    private readonly ToolStripStatusLabel _selectedLabel = new();

    private string _currentFile = string.Empty;
    private bool _suppressFindHighlight;

    public MainForm()
    {
        Text = "TTS Developer Studio - X-Wing Unified 1E";
        Width = 1600;
        Height = 950;
        MinimumSize = new Size(1100, 700);
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;

        BuildToolbar();
        BuildMainLayout();
        BuildStatusBar();

        Controls.Add(_mainSplit);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        Shown += (_, _) => ApplyInitialSplitterPositions();
    }

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Stretch = true;
        _toolbar.ImageScalingSize = new Size(20, 20);

        _openButton.Click += (_, _) => OpenJson();

        _exportButton.Enabled = false;
        _exportButton.Click += (_, _) => ExportScripts();

        _searchBox.AutoSize = false;
        _searchBox.Width = 430;
        _searchBox.ToolTipText = "Search Global Lua/XML and all object GUIDs, names, Lua, XML, URLs and JSON";
        _searchBox.TextChanged += (_, _) => PopulateTree();
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                _searchBox.Clear();
                e.SuppressKeyPress = true;
            }
        };

        _clearSearchButton.Click += (_, _) =>
        {
            _searchBox.Clear();
            _searchBox.Focus();
        };

        _findBox.AutoSize = false;
        _findBox.Width = 300;
        _findBox.ToolTipText = "Find in the current Lua/XML/JSON/details editor. Ctrl+F focuses this box.";
        _findBox.TextChanged += (_, _) => HighlightFindMatches();
        _findBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                FindNext(reverse: e.Shift);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _findBox.Clear();
                CurrentEditor().Focus();
                e.SuppressKeyPress = true;
            }
        };

        _findPreviousButton.Click += (_, _) => FindNext(reverse: true);
        _findNextButton.Click += (_, _) => FindNext(reverse: false);

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            _openButton,
            _exportButton,
            _toolbarSeparator1,
            _searchLabel,
            _searchBox,
            _clearSearchButton,
            _toolbarSeparator2,
            _findLabel,
            _findBox,
            _findPreviousButton,
            _findNextButton
        });
    }

    private void BuildMainLayout()
    {
        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.FixedPanel = FixedPanel.Panel1;
        _mainSplit.Panel1MinSize = 260;
        _mainSplit.Panel2MinSize = 600;
        _mainSplit.SplitterWidth = 7;

        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.FullRowSelect = true;
        _tree.ShowNodeToolTips = true;
        _tree.AfterSelect += (_, e) => ShowNode(e.Node);
        _tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node.Tag is TtsObjectInfo)
                _tabs.SelectedTab = _tabs.TabPages["Lua"] ?? _tabs.TabPages[0];
        };

        var treeHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "  Objects",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold)
        };

        _mainSplit.Panel1.Controls.Add(_tree);
        _mainSplit.Panel1.Controls.Add(treeHeader);

        _rightSplit.Dock = DockStyle.Fill;
        _rightSplit.Orientation = Orientation.Horizontal;
        _rightSplit.FixedPanel = FixedPanel.Panel2;
        _rightSplit.Panel1MinSize = 420;
        _rightSplit.Panel2MinSize = 95;
        _rightSplit.SplitterWidth = 7;

        BuildTabs();
        BuildDetailsPanel();

        _rightSplit.Panel1.Controls.Add(_tabs);
        _rightSplit.Panel2.Controls.Add(_details);

        _mainSplit.Panel2.Controls.Add(_rightSplit);
    }

    private void BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Name = "Editors";
        _tabs.SelectedIndexChanged += (_, _) => HighlightFindMatches();

        ConfigureEditor(_luaText);
        ConfigureEditor(_xmlText);
        ConfigureEditor(_jsonText);

        _tabs.TabPages.Add(CreateEditorPage("Lua", _luaText));
        _tabs.TabPages.Add(CreateEditorPage("XML UI", _xmlText));
        _tabs.TabPages.Add(CreateEditorPage("Raw JSON", _jsonText));
    }

    private static TabPage CreateEditorPage(string title, Control editor)
    {
        var page = new TabPage(title) { Name = title };
        page.Controls.Add(editor);
        return page;
    }

    private void BuildDetailsPanel()
    {
        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.BorderStyle = BorderStyle.None;
        _details.BackColor = SystemColors.Control;
        _details.Font = new Font("Consolas", 9.5f);
        _details.WordWrap = false;
        _details.ScrollBars = RichTextBoxScrollBars.Both;
        _details.Text = "Open a Tabletop Simulator JSON save to begin.";
    }

    private void BuildStatusBar()
    {
        _status.Dock = DockStyle.Bottom;
        _statusLabel.Text = "Ready";
        _objectCountLabel.Text = "No save loaded";
        _selectedLabel.Spring = true;
        _selectedLabel.TextAlign = ContentAlignment.MiddleRight;

        _status.Items.Add(_statusLabel);
        _status.Items.Add(new ToolStripStatusLabel { Text = " | " });
        _status.Items.Add(_objectCountLabel);
        _status.Items.Add(_selectedLabel);
    }

    private static void ConfigureEditor(RichTextBox editor)
    {
        editor.Dock = DockStyle.Fill;
        editor.ReadOnly = true;
        editor.BorderStyle = BorderStyle.None;
        editor.WordWrap = false;
        editor.DetectUrls = true;
        editor.ScrollBars = RichTextBoxScrollBars.Both;
        editor.Font = new Font("Consolas", 10.5f);
        editor.BackColor = Color.White;
        editor.HideSelection = false;
    }

    private void ApplyInitialSplitterPositions()
    {
        _mainSplit.SplitterDistance = Math.Min(390, Math.Max(280, ClientSize.Width / 4));
        _rightSplit.SplitterDistance = Math.Max(420, _rightSplit.Height - 145);
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O)
        {
            OpenJson();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.F)
        {
            _findBox.Focus();
            _findBox.SelectAll();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F3)
        {
            FindNext(reverse: e.Shift);
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.E)
        {
            if (_exportButton.Enabled) ExportScripts();
            e.SuppressKeyPress = true;
        }
    }

    private RichTextBox CurrentEditor()
    {
        if (_details.Focused) return _details;
        return _tabs.SelectedTab?.Controls.OfType<RichTextBox>().FirstOrDefault() ?? _luaText;
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
            _currentFile = dialog.FileName;
            _exportButton.Enabled = true;
            _searchBox.Clear();
            _findBox.Clear();
            PopulateTree();
            ShowSummary();
            _statusLabel.Text = $"Loaded {Path.GetFileName(dialog.FileName)}";
            _objectCountLabel.Text = $"{_loader.Objects.Count:N0} objects";
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
        var rootText = string.IsNullOrEmpty(_currentFile) ? "Save" : Path.GetFileName(_currentFile);
        var root = new TreeNode(rootText) { Tag = NodeTag.Summary, ToolTipText = _currentFile };

        AddGlobalNodeIfMatched(root, q);

        var objects = _loader.Objects.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
            objects = objects.Where(o => Contains(o.SearchText, q) || Contains(ToJson(o), q));

        var materialised = objects.ToList();
        foreach (var group in materialised.GroupBy(GetGroupName).OrderBy(g => GroupSortOrder(g.Key)).ThenBy(g => g.Key))
        {
            var groupNode = new TreeNode($"{group.Key} ({group.Count():N0})")
            {
                Tag = NodeTag.Group,
                ToolTipText = group.Key
            };

            foreach (var obj in group.OrderByDescending(o => o.LuaLength).ThenBy(o => DisplaySortName(o)))
            {
                groupNode.Nodes.Add(CreateObjectNode(obj));
            }

            root.Nodes.Add(groupNode);
        }

        _tree.Nodes.Add(root);
        root.Expand();

        if (!string.IsNullOrWhiteSpace(q))
        {
            foreach (TreeNode node in root.Nodes)
                node.Expand();
        }

        _tree.EndUpdate();

        _statusLabel.Text = string.IsNullOrWhiteSpace(q)
            ? "Object tree loaded"
            : $"Search matched {materialised.Count:N0} objects";
        _objectCountLabel.Text = $"Showing {materialised.Count:N0} / {_loader.Objects.Count:N0} objects";
    }

    private void AddGlobalNodeIfMatched(TreeNode root, string q)
    {
        if (!string.IsNullOrWhiteSpace(q) && !Contains(_loader.GlobalLua, q) && !Contains(_loader.GlobalXml, q))
            return;

        var global = new TreeNode($"Global Lua/XML ({_loader.GlobalLua.Length:N0} Lua chars)")
        {
            Tag = NodeTag.Global,
            ToolTipText = "Global LuaScript and XmlUI"
        };
        root.Nodes.Add(global);
    }

    private static TreeNode CreateObjectNode(TtsObjectInfo obj)
    {
        var suffix = obj.LuaLength > 0 ? $"  [{obj.LuaLength:N0} Lua]" : string.Empty;
        var node = new TreeNode($"{obj.DisplayName}{suffix}")
        {
            Tag = obj,
            ToolTipText = $"GUID: {obj.Guid}\nName: {obj.Name}\nNickname: {obj.Nickname}\nPath: {obj.Path}"
        };
        return node;
    }

    private void ShowNode(TreeNode? node)
    {
        if (node?.Tag is null) return;

        if (node.Tag is NodeTag.Summary)
        {
            ShowSummary();
            return;
        }

        if (node.Tag is NodeTag.Global)
        {
            ShowGlobal();
            return;
        }

        if (node.Tag is NodeTag.Group)
        {
            _selectedLabel.Text = node.Text;
            _details.Text = $"Group: {node.Text}\nDouble-click an object to jump to its Lua tab. Use the object search box to filter by GUID, name, URL, Lua, XML or JSON.";
            return;
        }

        if (node.Tag is TtsObjectInfo obj)
        {
            ShowObject(obj);
        }
    }

    private void ShowSummary()
    {
        _details.Text = _loader.BuildSummary();
        SetEditorText(_luaText, _loader.GlobalLua);
        SetEditorText(_xmlText, _loader.GlobalXml);
        SetEditorText(_jsonText, _loader.Root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? string.Empty);
        _selectedLabel.Text = string.IsNullOrEmpty(_currentFile) ? "Summary" : _currentFile;
        _tabs.SelectedTab = _tabs.TabPages["Lua"] ?? _tabs.TabPages[0];
        HighlightFindMatches();
    }

    private void ShowGlobal()
    {
        _details.Text =
$@"Global script/UI
File: {_currentFile}

Lua length: {_loader.GlobalLua.Length:N0}
XML length: {_loader.GlobalXml.Length:N0}
Objects: {_loader.Objects.Count:N0}";

        SetEditorText(_luaText, _loader.GlobalLua);
        SetEditorText(_xmlText, _loader.GlobalXml);
        SetEditorText(_jsonText, _loader.Root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? string.Empty);
        _selectedLabel.Text = "Global Lua/XML";
        _tabs.SelectedTab = _tabs.TabPages["Lua"] ?? _tabs.TabPages[0];
        HighlightFindMatches();
    }

    private void ShowObject(TtsObjectInfo obj)
    {
        var lua = TtsSaveLoader.GetString(obj.Source, "LuaScript");
        var xml = TtsSaveLoader.GetString(obj.Source, "XmlUI");
        var json = obj.Source.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        _details.Text = BuildObjectDetails(obj, lua, xml);
        SetEditorText(_luaText, lua);
        SetEditorText(_xmlText, xml);
        SetEditorText(_jsonText, json);

        _selectedLabel.Text = $"{obj.Guid} | {obj.DisplayName}";

        if (!string.IsNullOrWhiteSpace(lua))
            _tabs.SelectedTab = _tabs.TabPages["Lua"] ?? _tabs.TabPages[0];
        else if (!string.IsNullOrWhiteSpace(xml))
            _tabs.SelectedTab = _tabs.TabPages["XML UI"] ?? _tabs.TabPages[1];
        else
            _tabs.SelectedTab = _tabs.TabPages["Raw JSON"] ?? _tabs.TabPages[2];

        HighlightFindMatches();
    }

    private static string BuildObjectDetails(TtsObjectInfo obj, string lua, string xml)
    {
        var spawnHints = ExtractSpawnHints(lua + "\n" + xml + "\n" + ToJson(obj));

        return
$@"GUID: {obj.Guid}
Name: {obj.Name}
Nickname: {obj.Nickname}
Description: {obj.Description}
Path: {obj.Path}
Group: {GetGroupName(obj)}

Lua length: {obj.LuaLength:N0}
XML length: {obj.XmlLength:N0}
JSON length: {ToJson(obj).Length:N0}

Spawn/search hints:
{spawnHints}

Mesh:
{obj.MeshUrl}

Diffuse:
{obj.DiffuseUrl}

Collider:
{obj.ColliderUrl}";
    }

    private static string ExtractSpawnHints(string text)
    {
        var hints = new[]
        {
            "spawn", "Spawn", "ship", "Ship", "pilot", "Pilot", "dial", "Dial", "upgrade", "Upgrade", "bag", "Bag", "guid", "GUID"
        };

        var matches = hints
            .Where(h => text.Contains(h, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 0 ? "  No obvious spawn-related keywords found." : "  " + string.Join(", ", matches);
    }

    private static void SetEditorText(RichTextBox editor, string text)
    {
        editor.SuspendLayout();
        editor.Text = text ?? string.Empty;
        editor.SelectionStart = 0;
        editor.SelectionLength = 0;
        editor.ScrollToCaret();
        editor.ResumeLayout();
    }

    private void FindNext(bool reverse)
    {
        var query = _findBox.Text;
        if (string.IsNullOrWhiteSpace(query)) return;

        var editor = CurrentEditor();
        var text = editor.Text;
        if (string.IsNullOrEmpty(text))
        {
            _statusLabel.Text = "Current editor is empty";
            return;
        }

        var idx = reverse ? FindPreviousIndex(editor, query) : FindNextIndex(editor, query);

        if (idx >= 0)
        {
            _suppressFindHighlight = true;
            editor.Focus();
            editor.SelectionStart = idx;
            editor.SelectionLength = query.Length;
            editor.ScrollToCaret();
            _suppressFindHighlight = false;
            _statusLabel.Text = $"Found '{query}' at character {idx:N0}";
        }
        else
        {
            _statusLabel.Text = $"No match for '{query}'";
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private static int FindNextIndex(RichTextBox editor, string query)
    {
        var start = editor.SelectionStart + Math.Max(editor.SelectionLength, 1);
        if (start >= editor.TextLength) start = 0;

        var idx = editor.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && start > 0)
            idx = editor.Text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        return idx;
    }

    private static int FindPreviousIndex(RichTextBox editor, string query)
    {
        var start = Math.Max(0, editor.SelectionStart - 1);
        var idx = editor.Text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && start < editor.TextLength - 1)
            idx = editor.Text.LastIndexOf(query, editor.TextLength - 1, StringComparison.OrdinalIgnoreCase);
        return idx;
    }

    private void HighlightFindMatches()
    {
        if (_suppressFindHighlight) return;

        var editor = CurrentEditor();
        var query = _findBox.Text;
        ClearHighlight(editor);

        if (string.IsNullOrWhiteSpace(query) || editor.TextLength == 0)
            return;

        var originalStart = editor.SelectionStart;
        var originalLength = editor.SelectionLength;

        _suppressFindHighlight = true;
        try
        {
            var idx = 0;
            var matchCount = 0;
            while (idx < editor.TextLength)
            {
                idx = editor.Text.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                editor.Select(idx, query.Length);
                editor.SelectionBackColor = Color.Khaki;
                idx += query.Length;
                matchCount++;

                if (matchCount >= 1000) break;
            }

            editor.Select(Math.Min(originalStart, editor.TextLength), Math.Min(originalLength, Math.Max(0, editor.TextLength - originalStart)));
            _statusLabel.Text = matchCount == 0 ? $"No match for '{query}'" : $"{matchCount:N0} visible matches for '{query}'";
        }
        finally
        {
            _suppressFindHighlight = false;
        }
    }

    private static void ClearHighlight(RichTextBox editor)
    {
        if (editor.TextLength == 0) return;

        var originalStart = editor.SelectionStart;
        var originalLength = editor.SelectionLength;

        editor.SuspendLayout();
        editor.SelectAll();
        editor.SelectionBackColor = editor.BackColor;
        editor.Select(Math.Min(originalStart, editor.TextLength), Math.Min(originalLength, Math.Max(0, editor.TextLength - originalStart)));
        editor.ResumeLayout();
    }

    private void ExportScripts()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose export folder"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var root = Path.Combine(dialog.SelectedPath, "tts-developer-studio-export");
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

    private static string GetGroupName(TtsObjectInfo o)
    {
        var name = o.Name ?? string.Empty;
        var nick = o.Nickname ?? string.Empty;
        var combined = $"{name} {nick} {o.Description}";
        var lua = TtsSaveLoader.GetString(o.Source, "LuaScript");
        var xml = TtsSaveLoader.GetString(o.Source, "XmlUI");
        var all = $"{combined} {lua} {xml}";

        if (Contains(all, "spawn") || Contains(all, "squad") || Contains(all, "pilot") && o.LuaLength > 0) return "Spawn System / Scripted";
        if (Contains(name, "Bag") || Contains(nick, "Bag")) return "Bags";
        if (Contains(name, "Deck") || name.Equals("Card", StringComparison.OrdinalIgnoreCase) || Contains(nick, "Card")) return "Cards & Decks";
        if (Contains(combined, "Ship") || Contains(combined, "Fighter") || Contains(combined, "TIE") || Contains(combined, "X-Wing") || Contains(combined, "Y-Wing")) return "Ships / Models";
        if (Contains(name, "Dice") || Contains(nick, "Dice")) return "Dice";
        if (Contains(combined, "Token") || Contains(combined, "Marker")) return "Tokens / Markers";
        if (Contains(combined, "Asteroid") || Contains(combined, "Debris") || Contains(combined, "Obstacle")) return "Obstacles";
        if (Contains(combined, "Template") || Contains(combined, "Ruler") || Contains(combined, "Range") || Contains(combined, "Maneuver")) return "Templates & Rulers";
        if (o.LuaLength > 0) return "Other Scripted Objects";
        if (!string.IsNullOrWhiteSpace(o.MeshUrl)) return "Custom Models";
        return "Other";
    }

    private static int GroupSortOrder(string group)
    {
        return group switch
        {
            "Spawn System / Scripted" => 0,
            "Bags" => 1,
            "Ships / Models" => 2,
            "Cards & Decks" => 3,
            "Templates & Rulers" => 4,
            "Tokens / Markers" => 5,
            "Dice" => 6,
            "Obstacles" => 7,
            "Other Scripted Objects" => 8,
            "Custom Models" => 9,
            _ => 99
        };
    }

    private static string DisplaySortName(TtsObjectInfo obj)
    {
        if (!string.IsNullOrWhiteSpace(obj.Nickname)) return obj.Nickname;
        if (!string.IsNullOrWhiteSpace(obj.Name)) return obj.Name;
        return obj.Guid;
    }

    private static string ToJson(TtsObjectInfo obj)
    {
        return obj.Source.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static bool Contains(string? value, string q)
    {
        return !string.IsNullOrEmpty(value) && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Length > 160 ? value[..160] : value;
    }

    private enum NodeTag
    {
        Summary,
        Global,
        Group
    }
}
