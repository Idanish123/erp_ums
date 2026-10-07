using System.Data;
using System.Globalization;

namespace University_Management_System_2;

public sealed class ShellForm : Form
{
    private readonly IUniversityStore _store;
    private readonly UserSession _session;
    private readonly Panel _workspace = new() { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 20) };
    private readonly Dictionary<string, Button> _navigation = [];
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _mode = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ToolTip _tips = new();
    private EntitySpec? _entity;
    private DataGridView? _grid;
    private TextBox? _search;
    private Label? _count;
    private Button? _edit;
    private Button? _delete;
    private DataTable? _table;
    private bool _busy;
    private string _page = "overview";
    private int _generation;
    public bool LogoutRequested { get; private set; }

    public ShellForm(IUniversityStore store, UserSession session)
    {
        _store = store;
        _session = session;
        Text = "University management";
        Size = new Size(1280, 860);
        MinimumSize = new Size(960, 680);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        UiTheme.Apply(this);
        _workspace.BackColor = UiTheme.Canvas;
        BuildShell();
        Shown += async (_, _) => await ShowPageAsync("overview");
        FormClosed += (_, _) => _lifetime.Cancel();
    }

    private void BuildShell()
    {
        var footer = new StatusStrip { BackColor = UiTheme.Background, SizingGrip = true, AccessibleName = "Workspace status" };
        _mode.Text = _store.IsDemo ? "Demo · changes reset on exit" : "SQL Server workspace";
        footer.Items.AddRange([_status, _mode]);
        var body = new Panel { Dock = DockStyle.Fill };
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Left, Width = 220, BackColor = UiTheme.Background,
            Padding = new Padding(16, 20, 16, 16), ColumnCount = 1, RowCount = 2
        };
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var workspaceLabel = UiTheme.Label("Workspace", 11, true);
        workspaceLabel.Margin = new Padding(4, 0, 0, 14);
        links.Controls.Add(workspaceLabel);
        AddNavigation(links, "overview", "&Overview");
        foreach (var entity in Catalog.Entities.Where(e => !e.Finance)) AddNavigation(links, entity.Key, entity.Title);
        if (_session.Role == UserRole.Administrator)
        {
            var finance = UiTheme.Label("Finance", 10.5f, true);
            finance.Margin = new Padding(4, 22, 0, 10);
            links.Controls.Add(finance);
            foreach (var entity in Catalog.Entities.Where(e => e.Finance)) AddNavigation(links, entity.Key, entity.Title);
        }
        var userPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var user = UiTheme.Label(_session.Username, 10.5f, true);
        user.MaximumSize = new Size(180, 0);
        userPanel.Controls.Add(user);
        var role = UiTheme.Label(_session.Role.ToString(), 9.5f);
        role.ForeColor = UiTheme.Muted;
        userPanel.Controls.Add(role);
        if (_session.Role == UserRole.Administrator && !_store.IsDemo)
        {
            var account = UiTheme.Button("Create account");
            account.Width = 180;
            account.Click += (_, _) => { using var dialog = new AccountDialog(_store, _session); dialog.ShowDialog(this); };
            userPanel.Controls.Add(account);
        }
        var signOut = UiTheme.Button(_store.IsDemo ? "Exit demo" : "Sign &out");
        signOut.Width = 180;
        signOut.Click += (_, _) => { LogoutRequested = true; Close(); };
        userPanel.Controls.Add(signOut);
        nav.Controls.Add(links, 0, 0);
        nav.Controls.Add(userPanel, 0, 1);
        body.Controls.Add(_workspace);
        body.Controls.Add(nav);
        var header = new Panel { Dock = DockStyle.Top, Height = _store.IsDemo ? 90 : 64, BackColor = UiTheme.Background, Padding = new Padding(24, 12, 24, 10) };
        var title = UiTheme.Label("University management", 19);
        title.Dock = DockStyle.Top;
        header.Controls.Add(title);
        if (_store.IsDemo)
        {
            var demo = UiTheme.Label("Demo workspace · fictitious records · changes are not saved after exit", 10);
            demo.Dock = DockStyle.Bottom;
            demo.ForeColor = UiTheme.Muted;
            header.Controls.Add(demo);
        }
        Controls.Add(body);
        Controls.Add(header);
        Controls.Add(footer);
        _status.Text = "Ready";
    }

    private void AddNavigation(FlowLayoutPanel parent, string key, string title)
    {
        var button = UiTheme.Button(title);
        button.AutoSize = false;
        button.Width = 180;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Margin = new Padding(0, 0, 0, 4);
        button.AccessibleDescription = "Open " + title.Replace("&", "");
        button.Click += async (_, _) => await ShowPageAsync(key);
        _navigation.Add(key, button);
        parent.Controls.Add(button);
    }

    public async Task ShowPageAsync(string key)
    {
        if (_busy || IsDisposed) return;
        var entity = key == "overview" ? null : Catalog.Get(key);
        if (entity is not null && !_session.CanAccess(entity)) throw new UnauthorizedAccessException("Finance access requires an administrator.");
        _entity = entity;
        _page = key;
        _generation++;
        foreach (var (page, button) in _navigation)
        {
            button.BackColor = page == key ? UiTheme.Accent : UiTheme.Background;
            button.ForeColor = page == key ? SystemColors.HighlightText : UiTheme.Text;
            button.AccessibleDescription = (page == key ? "Current page. " : "Open ") + button.Text.Replace("&", "");
        }
        ClearWorkspace();
        if (entity is null) await BuildOverviewAsync();
        else await BuildRecordsAsync(entity);
    }

    private void ClearWorkspace()
    {
        _grid = null; _search = null; _edit = null; _delete = null; _count = null; _table = null;
        while (_workspace.Controls.Count > 0) _workspace.Controls[0].Dispose();
    }

    private TableLayoutPanel Page(string title, string subtitle)
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiTheme.Canvas };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = UiTheme.Label(title, 24);
        heading.Margin = new Padding(0, 0, 0, 8);
        var description = UiTheme.Label(subtitle);
        description.ForeColor = UiTheme.Muted;
        description.Margin = new Padding(0, 0, 0, 24);
        page.Controls.Add(heading, 0, 0);
        page.Controls.Add(description, 0, 1);
        _workspace.Controls.Add(page);
        return page;
    }

    private async Task BuildOverviewAsync()
    {
        var page = Page("Overview", "Your university at a glance. Choose a workspace to manage its records.");
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoScroll = true };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 294, ColumnCount = 3, RowCount = 2, Margin = new Padding(0) };
        for (var i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        for (var i = 0; i < 2; i++) cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        content.Controls.Add(cards, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 18, 0, 18) };
        var student = UiTheme.Button("Add student", primary: true);
        student.Click += async (_, _) => await EditRecordAsync(Catalog.Get("students"), null);
        actions.Controls.Add(student);
        var refresh = UiTheme.Button("&Refresh overview");
        refresh.Click += async (_, _) => await ShowPageAsync("overview");
        actions.Controls.Add(refresh);
        content.Controls.Add(actions, 0, 1);
        var help = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background, Padding = new Padding(24), MinimumSize = new Size(0, 155) };
        var guidance = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        guidance.Controls.Add(UiTheme.Label("A clear place for every task", 15));
        var guidanceText = UiTheme.Label("Academic workspaces manage students, faculty, courses and campuses.\nFinance records payments separately. Select a row before editing or deleting it.\nCtrl+F searches a workspace; Ctrl+N creates a record; Ctrl+S edits the selected record.");
        guidanceText.ForeColor = UiTheme.Muted;
        guidanceText.Margin = new Padding(0, 12, 0, 0);
        guidance.Controls.Add(guidanceText);
        help.Controls.Add(guidance);
        content.Controls.Add(help, 0, 2);
        page.Controls.Add(content, 0, 2);
        await RunAsync(async () =>
        {
            var counts = new List<(string Key, string Title, string Value, string Hint)>();
            foreach (var key in new[] { "students", "faculty", "departments", "campuses" })
            {
                var data = await _store.LoadAsync(_session, Catalog.Get(key), _lifetime.Token);
                counts.Add((key, Catalog.Get(key).Title, data.Rows.Count.ToString("N0"), "View records →"));
            }
            if (_session.Role == UserRole.Administrator)
            {
                foreach (var (key, column, title) in new[] { ("fees", "Amount", "Fees recorded"), ("salaries", "Salary", "Salaries recorded") })
                {
                    var data = await _store.LoadAsync(_session, Catalog.Get(key), _lifetime.Token);
                    var sum = data.Rows.Cast<DataRow>().Where(r => r[column] != DBNull.Value).Sum(r => Convert.ToDecimal(r[column], CultureInfo.InvariantCulture));
                    counts.Add((key, title, "Rs. " + sum.ToString("N2"), "All recorded payments →"));
                }
            }
            else counts.Add(("courses", "Academic access", "Registrar", "Finance is restricted to administrators"));
            for (var index = 0; index < counts.Count; index++)
            {
                var item = counts[index];
                var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiTheme.Background,
                    Padding = new Padding(20, 15, 20, 15), Margin = new Padding(0, 0, 12, 12), CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
                card.Controls.Add(UiTheme.Label(item.Title), 0, 0);
                var value = UiTheme.Label(item.Value, item.Key is "fees" or "salaries" ? 21 : 30);
                value.Margin = new Padding(0, 8, 0, 8);
                value.AutoEllipsis = true;
                value.Dock = DockStyle.Fill;
                card.Controls.Add(value, 0, 1);
                var link = new LinkLabel { Text = item.Hint, AutoSize = true, LinkColor = UiTheme.Accent, ActiveLinkColor = UiTheme.Accent,
                    AccessibleName = "Open " + Catalog.Get(item.Key).Title, Font = Font };
                link.LinkClicked += async (_, _) => await ShowPageAsync(item.Key);
                card.Controls.Add(link, 0, 2);
                cards.Controls.Add(card, index % 3, index / 3);
            }
            _status.Text = "Overview updated · " + DateTime.Now.ToString("t");
        }, "Loading overview…");
    }

    private async Task BuildRecordsAsync(EntitySpec entity)
    {
        var page = Page(entity.Title, entity.Finance
            ? "Record a payment, review its details, or explicitly delete a selected payment."
            : $"Search and manage {entity.Title.ToLowerInvariant()}. Select a record to edit its details.");
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 16) };
        var searchLabel = UiTheme.Label("&Search");
        searchLabel.Margin = new Padding(0, 10, 8, 0);
        _search = new TextBox { Name = "SearchBox", Width = 218, PlaceholderText = "Search these records", AccessibleName = "Search " + entity.Title,
            Margin = new Padding(0, 6, 16, 0), MaxLength = 100 };
        _search.TextChanged += (_, _) => ApplySearch();
        toolbar.Controls.Add(searchLabel);
        toolbar.Controls.Add(_search);
        var add = UiTheme.Button(entity.Finance ? "Record &payment" : "&New record", primary: true);
        add.Name = "NewRecordButton";
        add.Click += async (_, _) => await EditRecordAsync(entity, null);
        _edit = UiTheme.Button("&Edit selected");
        _edit.Name = "EditSelectedButton";
        _edit.Enabled = false;
        _edit.Click += async (_, _) => await EditSelectedAsync();
        _delete = UiTheme.Button("&Delete selected", danger: true);
        _delete.Name = "DeleteSelectedButton";
        _delete.Enabled = false;
        _delete.Click += async (_, _) => await DeleteSelectedAsync();
        var refresh = UiTheme.Button("&Refresh");
        refresh.Click += async (_, _) => await ReloadAsync();
        toolbar.Controls.AddRange([add, _edit, _delete, refresh]);
        _tips.SetToolTip(add, "Ctrl+N · Create a new record");
        _tips.SetToolTip(_edit, "Ctrl+S · Edit the selected record");
        _grid = new DataGridView
        {
            Name = "RecordsGrid", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToOrderColumns = true, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = UiTheme.Background,
            BorderStyle = BorderStyle.None, RowHeadersVisible = false, EnableHeadersVisualStyles = false,
            AccessibleName = entity.Title + " records", AccessibleDescription = "Read-only table. Arrow keys select records. Use Edit selected to change a record.",
            ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 36 }, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = UiTheme.Border
        };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = UiTheme.Background, ForeColor = UiTheme.Text, Font = UiTheme.GetFont(bold: true), Padding = new Padding(8) };
        _grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = UiTheme.Background, ForeColor = UiTheme.Text,
            SelectionBackColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(222, 235, 255),
            SelectionForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : UiTheme.Text, Padding = new Padding(8, 3, 8, 3) };
        _grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.Canvas;
        _grid.SelectionChanged += (_, _) => UpdateSelection();
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditSelectedAsync(); };
        _count = UiTheme.Label("Loading records…", 10);
        _count.ForeColor = UiTheme.Muted;
        _count.Margin = new Padding(0, 12, 0, 0);
        content.Controls.Add(toolbar, 0, 0);
        content.Controls.Add(_grid, 0, 1);
        content.Controls.Add(_count, 0, 2);
        page.Controls.Add(content, 0, 2);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_entity is null || _grid is null || _busy) return;
        var entity = _entity;
        var grid = _grid;
        var generation = _generation;
        await RunAsync(async () =>
        {
            var data = await _store.LoadAsync(_session, entity, _lifetime.Token);
            if (generation != _generation || grid.IsDisposed) return;
            _table = data;
            grid.Columns.Clear();
            foreach (DataColumn column in data.Columns)
            {
                // Derived names make relationship selectors readable; retain IDs only when no display name exists.
                if (entity.Derived.Any(d => d.ForeignKey == column.ColumnName)) continue;
                var field = entity.Fields.FirstOrDefault(f => f.Name == column.ColumnName);
                var item = new DataGridViewTextBoxColumn { Name = column.ColumnName, DataPropertyName = column.ColumnName,
                    HeaderText = Catalog.ColumnLabel(entity, column.ColumnName), MinimumWidth = 95,
                    FillWeight = column.ColumnName == entity.IdColumn ? 45 : field?.Kind == FieldKind.Multiline ? 145 : 100 };
                item.MinimumWidth = Math.Max(item.MinimumWidth,
                    TextRenderer.MeasureText(item.HeaderText, grid.ColumnHeadersDefaultCellStyle.Font).Width + 34);
                if (field?.Kind == FieldKind.Money) item.DefaultCellStyle.Format = "N2";
                if (field?.Kind == FieldKind.Date) item.DefaultCellStyle.Format = "dd MMM yyyy";
                grid.Columns.Add(item);
            }
            grid.DataSource = data.DefaultView;
            ApplySearch();
            grid.ClearSelection();
            grid.CurrentCell = null;
            UpdateSelection();
            _status.Text = "Records updated · " + DateTime.Now.ToString("t");
        }, "Loading " + entity.Title.ToLowerInvariant() + "…");
    }

    private void ApplySearch()
    {
        if (_table is null) return;
        var term = (_search?.Text ?? "").Trim();
        var escaped = term.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        _table.DefaultView.RowFilter = term.Length == 0 ? "" : string.Join(" OR ", _table.Columns.Cast<DataColumn>()
            .Select(c => $"CONVERT([{c.ColumnName}], 'System.String') LIKE '%{escaped}%'"));
        if (_count is not null) _count.Text = _table.Rows.Count == 0 ? "No records yet. Choose New record or Record payment to get started."
            : _table.DefaultView.Count == 0 ? "No matching records. Try another search."
            : $"{_table.DefaultView.Count:N0} of {_table.Rows.Count:N0} records · select a row to edit or delete";
        UpdateSelection();
    }

    private DataRow? SelectedRow() => _grid?.SelectedRows.Count > 0 && _grid.SelectedRows[0].DataBoundItem is DataRowView view ? view.Row : null;
    private void UpdateSelection()
    {
        var selected = !_busy && SelectedRow() is not null;
        if (_edit is not null) _edit.Enabled = selected;
        if (_delete is not null) _delete.Enabled = selected;
    }
    private async Task EditSelectedAsync()
    {
        if (_busy || _entity is null || SelectedRow() is not DataRow row) return;
        await EditRecordAsync(_entity, row);
    }
    private async Task EditRecordAsync(EntitySpec entity, DataRow? row)
    {
        if (_busy) return;
        try
        {
            using var editor = new RecordEditorDialog(_store, _session, entity, row);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            if (_entity?.Key == entity.Key) await ReloadAsync();
            else await ShowPageAsync(_page);
            _status.Text = row is null ? "New " + entity.Singular + " saved." : "Selected " + entity.Singular + " updated.";
        }
        catch (Exception ex)
        {
            _status.Text = ex is InvalidOperationException ? ex.Message : "This existing record contains unsupported values. Ask an administrator to correct them before editing.";
            MessageBox.Show(this, _status.Text, "Unable to open record", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    private async Task DeleteSelectedAsync()
    {
        if (_busy || _entity is null || SelectedRow() is not DataRow row) return;
        var entity = _entity;
        var id = Convert.ToInt32(row[entity.IdColumn], CultureInfo.InvariantCulture);
        var labelColumn = entity.Finance ? (entity.Key == "fees" ? "StName" : "F_Name") : entity.DisplayColumn;
        var name = row.Table.Columns.Contains(labelColumn) ? Convert.ToString(row[labelColumn], CultureInfo.InvariantCulture) : entity.Singular;
        if (MessageBox.Show(this, $"Delete {entity.Singular} #{id} ({name})?\n\nThis removes the record permanently. Choose No to keep it.",
                "Delete selected " + entity.Singular, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var deleted = false;
        await RunAsync(async () => { await _store.DeleteAsync(_session, entity, id, _lifetime.Token); deleted = true; }, "Deleting selected record…");
        if (deleted) { await ReloadAsync(); _status.Text = "Selected " + entity.Singular + " deleted."; }
    }

    private async Task RunAsync(Func<Task> operation, string message)
    {
        if (_busy) return;
        SetBusy(true);
        _status.Text = message;
        try { await operation(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (UnauthorizedAccessException ex) { _status.Text = ex.Message; }
        catch (InvalidOperationException ex)
        {
            _status.Text = ex.Message;
            if (!IsDisposed) MessageBox.Show(this, ex.Message, "Unable to complete action", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception)
        {
            _status.Text = "Unable to load the workspace. Check the database connection, then refresh.";
            if (!IsDisposed) MessageBox.Show(this, "The database could not confirm this action. Check the connection and refresh the records before retrying.",
                "Connection unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        foreach (var button in _navigation.Values) button.Enabled = !busy;
        _workspace.Enabled = !busy;
        UpdateSelection();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_busy && keyData == (Keys.Control | Keys.F) && _search is not null) { _search.Focus(); _search.SelectAll(); return true; }
        if (!_busy && keyData == (Keys.Control | Keys.N) && _entity is not null) { _ = EditRecordAsync(_entity, null); return true; }
        if (!_busy && keyData == (Keys.Control | Keys.S) && _entity is not null) { _ = EditSelectedAsync(); return true; }
        if (!_busy && keyData == Keys.F5) { _ = _entity is null ? ShowPageAsync("overview") : ReloadAsync(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _lifetime.Cancel(); _lifetime.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }
}
