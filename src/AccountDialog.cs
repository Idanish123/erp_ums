namespace University_Management_System_2;

public sealed class AccountDialog : Form
{
    private sealed record RoleItem(UserRole Role, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly IUniversityStore _store;
    private readonly UserSession? _actor;
    private readonly TextBox _username = new() { MaxLength = 100 };
    private readonly TextBox _password = new() { MaxLength = 256, UseSystemPasswordChar = true };
    private readonly TextBox _confirmation = new() { MaxLength = 256, UseSystemPasswordChar = true };
    private readonly ComboBox _role = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _showPassword = new() { Text = "&Show passwords", AutoSize = true, AccessibleName = "Show passwords" };
    private readonly Button _create = UiTheme.Button("Create &account", primary: true);
    private readonly Button _cancel = UiTheme.Button("&Cancel");
    private readonly Label _status = UiTheme.Label("");
    private readonly TableLayoutPanel _fields;
    private readonly ErrorProvider _errors;
    private bool _busy;
    private bool _saved;
    private readonly UserRole _initialRole;

    public string? CreatedUsername { get; private set; }

    public AccountDialog(IUniversityStore store, UserSession? actor)
    {
        _store = store;
        _actor = actor;
        _initialRole = actor is null ? UserRole.Administrator : UserRole.Registrar;
        UiTheme.Apply(this);
        Text = actor is null ? "Set up administrator account" : "Create account";
        ClientSize = new Size(560, 650);
        MinimumSize = new Size(500, 460);
        MinimizeBox = false;
        _errors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(UiTheme.Label(Text, 22), 0, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true,
            Padding = new Padding(0, 12, 28, 12)
        };
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var help = UiTheme.Label(actor is null
            ? "Create the first administrator to start using university records."
            : "Administrators manage all records and accounts. Registrars manage academic records.");
        help.ForeColor = UiTheme.Muted;
        help.Dock = DockStyle.Fill;
        AddRow(help);
        AddField("&Username", _username, "Username", 1);
        AddField("&Password", _password, "Password", 3);
        var guidance = UiTheme.Label("Use a password or passphrase of 12–256 characters.", 9.5f);
        guidance.ForeColor = UiTheme.Muted;
        guidance.Dock = DockStyle.Fill;
        AddRow(guidance);
        AddField("Con&firm password", _confirmation, "Confirm password", 5);
        _showPassword.TabIndex = 6;
        _showPassword.Margin = new Padding(0, 8, 0, 12);
        AddRow(_showPassword);
        AddField("&Role", _role, "Account role", 8);
        _role.Items.AddRange([new RoleItem(UserRole.Registrar, "Registrar"), new RoleItem(UserRole.Administrator, "Administrator")]);
        _role.SelectedItem = _role.Items.OfType<RoleItem>().Single(i => i.Role == _initialRole);
        _role.Enabled = actor is not null;
        _role.AccessibleDescription = actor is null ? "The first account is always an administrator." : "Select the permissions for this account.";
        scroll.Controls.Add(_fields);
        layout.Controls.Add(scroll, 0, 1);
        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(0, 12, 0, 4);
        _status.AccessibleName = "Account creation status";
        layout.Controls.Add(_status, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        _create.TabIndex = 9;
        _cancel.TabIndex = 10;
        actions.Controls.AddRange([_create, _cancel]);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        AcceptButton = _create;
        CancelButton = _cancel;
        _showPassword.CheckedChanged += (_, _) =>
        {
            _password.UseSystemPasswordChar = !_showPassword.Checked;
            _confirmation.UseSystemPasswordChar = !_showPassword.Checked;
        };
        _create.Click += async (_, _) => await CreateAsync();
        _cancel.Click += (_, _) => Close();
        Shown += (_, _) => _username.Focus();
        FormClosing += OnClosing;
        if (actor is not null && actor.Role != UserRole.Administrator)
        {
            _create.Enabled = false;
            ShowStatus("Administrator access is required to create an account.", true);
        }
    }

    private void AddRow(Control control) => _fields.Controls.Add(control, 0, _fields.RowCount++);

    private void AddField(string labelText, Control input, string accessibleName, int index)
    {
        var label = UiTheme.Label(labelText);
        label.TabIndex = index - 1;
        label.Margin = new Padding(0, 12, 0, 4);
        AddRow(label);
        input.Dock = DockStyle.Fill;
        input.TabIndex = index;
        input.AccessibleName = accessibleName;
        input.AccessibleDescription = $"Required. {accessibleName}.";
        input.BackColor = UiTheme.Canvas;
        input.ForeColor = UiTheme.Text;
        input.Margin = new Padding(0, 0, 0, 4);
        if (input is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
        AddRow(input);
    }

    private async Task CreateAsync()
    {
        if (_busy || (_actor is not null && _actor.Role != UserRole.Administrator)) return;
        _errors.Clear();
        Control? firstInvalid = null;
        void Error(Control input, string message)
        {
            _errors.SetError(input, message);
            input.AccessibleDescription = message;
            firstInvalid ??= input;
        }
        var username = _username.Text.Trim();
        if (username.Length is < 3 or > 100 || username.Any(char.IsControl))
            Error(_username, "Use a username of 3–100 characters without control characters.");
        if (_password.Text.Length is < 12 or > 256 || string.IsNullOrWhiteSpace(_password.Text))
            Error(_password, "Use a password or passphrase of 12–256 characters.");
        if (!string.Equals(_password.Text, _confirmation.Text, StringComparison.Ordinal))
            Error(_confirmation, "The passwords do not match.");
        if (_role.SelectedItem is not RoleItem) Error(_role, "Select an account role.");
        if (firstInvalid is not null)
        {
            ShowStatus(_errors.GetError(firstInvalid), true);
            firstInvalid.Focus();
            return;
        }
        SetBusy(true);
        ShowStatus("Creating account…");
        try
        {
            var role = ((RoleItem)_role.SelectedItem!).Role;
            await _store.CreateAccountAsync(_actor, username, _password.Text, role);
            CreatedUsername = username;
            _saved = true;
            _password.Clear();
            _confirmation.Clear();
            SetBusy(false);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            var message = ex is InvalidOperationException && ex.Message.StartsWith("That username is already in use", StringComparison.Ordinal)
                ? "That username is already in use. Choose another username."
                : RecordEditorDialog.SafeError(ex, "create the account");
            ShowStatus(message, true);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _fields.Enabled = !busy;
        _create.Enabled = !busy && (_actor is null || _actor.Role == UserRole.Administrator);
        _cancel.Enabled = !busy;
        _role.Enabled = !busy && _actor is not null;
        UseWaitCursor = busy;
    }

    private void ShowStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? UiTheme.Danger : UiTheme.Muted;
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        bool dirty = _username.Text.Length > 0 || _password.Text.Length > 0 || _confirmation.Text.Length > 0
            || (_role.SelectedItem as RoleItem)?.Role != _initialRole;
        if (!_saved && dirty && MessageBox.Show(this, "Discard this unsaved account?", "Unsaved changes",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
        {
            e.Cancel = true;
            return;
        }
        _password.Clear();
        _confirmation.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _errors.Dispose();
        base.Dispose(disposing);
    }
}
