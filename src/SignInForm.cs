namespace University_Management_System_2;

public sealed class SignInForm : Form
{
    private readonly IUniversityStore _store;
    private readonly TextBox _username = new() { MaxLength = 100, AccessibleName = "Username", TabIndex = 1 };
    private readonly TextBox _password = new() { MaxLength = 256, UseSystemPasswordChar = true, AccessibleName = "Password", TabIndex = 3 };
    private readonly CheckBox _showPassword = new() { Text = "Sho&w password", AccessibleName = "Show password", TabIndex = 4, AutoSize = true };
    private readonly Button _signIn = UiTheme.Button("&Sign in", primary: true);
    private readonly Button _setup = UiTheme.Button("Set up &administrator");
    private readonly Button _retry = UiTheme.Button("&Retry connection");
    private readonly Button _close = UiTheme.Button("&Close");
    private readonly Label _status = UiTheme.Label("Connecting to university records…");
    private readonly TableLayoutPanel _fields;
    private readonly ErrorProvider _errors;
    private bool _busy;
    private bool _connected;
    private bool _hasAccounts;

    public UserSession? Session { get; private set; }

    public SignInForm(IUniversityStore store)
    {
        _store = store;
        UiTheme.Apply(this);
        Text = "University management · Sign in";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 570);
        MinimumSize = new Size(500, 420);
        _errors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(32) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(UiTheme.Label("University management", 24), 0, 0);
        var introduction = UiTheme.Label("Sign in to manage academic and financial records.");
        introduction.ForeColor = UiTheme.Muted;
        introduction.Dock = DockStyle.Fill;
        layout.Controls.Add(introduction, 0, 1);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _fields = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(0, 20, 28, 8) };
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField("&Username", _username, 0);
        AddField("&Password", _password, 2);
        _showPassword.Margin = new Padding(0, 8, 0, 8);
        _fields.Controls.Add(_showPassword, 0, _fields.RowCount++);
        scroll.Controls.Add(_fields);
        layout.Controls.Add(scroll, 0, 2);
        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(0, 12, 0, 8);
        _status.AccessibleName = "Sign-in status";
        layout.Controls.Add(_status, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        _signIn.TabIndex = 5;
        _close.TabIndex = 6;
        _setup.TabIndex = 7;
        _retry.TabIndex = 8;
        actions.Controls.AddRange([_signIn, _close, _setup, _retry]);
        layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout);
        _setup.Visible = false;
        _retry.Visible = false;
        _signIn.Enabled = false;
        _fields.Enabled = false;
        AcceptButton = _signIn;
        CancelButton = _close;
        _showPassword.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPassword.Checked;
        _signIn.Click += async (_, _) => await SignInAsync();
        _setup.Click += async (_, _) => await SetupAsync();
        _retry.Click += async (_, _) => await ConnectAsync();
        _close.Click += (_, _) => Close();
        Shown += async (_, _) => await ConnectAsync();
        FormClosing += (_, e) =>
        {
            if (_busy) { e.Cancel = true; return; }
            _password.Clear();
        };
    }

    private void AddField(string labelText, TextBox input, int labelIndex)
    {
        var label = UiTheme.Label(labelText);
        label.TabIndex = labelIndex;
        label.Margin = new Padding(0, 12, 0, 4);
        _fields.Controls.Add(label, 0, _fields.RowCount++);
        input.Dock = DockStyle.Fill;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.BackColor = UiTheme.Canvas;
        input.ForeColor = UiTheme.Text;
        input.AccessibleDescription = $"Required. {input.AccessibleName}.";
        input.Margin = new Padding(0, 0, 0, 4);
        _fields.Controls.Add(input, 0, _fields.RowCount++);
    }

    private async Task ConnectAsync()
    {
        if (_busy) return;
        SetBusy(true);
        ShowStatus("Connecting to university records…");
        _retry.Visible = false;
        try
        {
            await _store.InitializeAsync();
            _hasAccounts = await _store.HasAccountsAsync();
            _connected = true;
            _setup.Visible = !_hasAccounts;
            ShowStatus(_hasAccounts ? "Ready. Enter your username and password." : "No accounts exist yet. Set up the first administrator to continue.");
            AcceptButton = _hasAccounts ? _signIn : _setup;
        }
        catch (Exception)
        {
            _connected = false;
            _setup.Visible = false;
            _retry.Visible = true;
            ShowStatus("Could not connect to university records. Install SQL Server LocalDB or set UMS_CONNECTION_STRING, then retry.", true);
            AcceptButton = _retry;
        }
        finally { SetBusy(false); }
        if (_connected && _hasAccounts) _username.Focus();
        else if (_connected) _setup.Focus();
    }

    private async Task SetupAsync()
    {
        if (_busy || !_connected || _hasAccounts) return;
        try
        {
            using var dialog = new AccountDialog(_store, actor: null);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _username.Text = dialog.CreatedUsername ?? "";
            await ConnectAsync();
            if (_connected)
            {
                ShowStatus("Administrator account created. Sign in with your new password.");
                _password.Focus();
            }
        }
        catch (Exception ex) { ShowStatus(RecordEditorDialog.SafeError(ex, "set up the administrator account"), true); }
    }

    private async Task SignInAsync()
    {
        if (_busy || !_connected || !_hasAccounts) return;
        _errors.Clear();
        var username = _username.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            _errors.SetError(_username, "Enter your username.");
            _username.AccessibleDescription = "Enter your username.";
            ShowStatus("Enter your username.", true);
            _username.Focus();
            return;
        }
        if (_password.Text.Length == 0)
        {
            _errors.SetError(_password, "Enter your password.");
            _password.AccessibleDescription = "Enter your password.";
            ShowStatus("Enter your password.", true);
            _password.Focus();
            return;
        }
        SetBusy(true);
        ShowStatus("Signing in…");
        try
        {
            var session = await _store.SignInAsync(username, _password.Text);
            if (session is null)
            {
                ShowStatus("The username or password is incorrect. Try again.", true);
                _password.SelectAll();
            }
            else
            {
                Session = session;
                _password.Clear();
                SetBusy(false);
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception)
        {
            _connected = false;
            _retry.Visible = true;
            ShowStatus("Could not sign in because the database connection failed. Retry the connection; your username has been kept.", true);
            AcceptButton = _retry;
        }
        finally { SetBusy(false); }
        if (Session is null && _connected) _password.Focus();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _fields.Enabled = !busy && _connected && _hasAccounts;
        _signIn.Enabled = !busy && _connected && _hasAccounts;
        _setup.Enabled = !busy && _connected && !_hasAccounts;
        _retry.Enabled = !busy;
        _close.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void ShowStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? UiTheme.Danger : UiTheme.Muted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _errors.Dispose();
        base.Dispose(disposing);
    }
}
