using System.Data;
using System.Globalization;

namespace University_Management_System_2;

public sealed class RecordEditorDialog : Form
{
    private sealed record LookupItem(int Id, string Display, bool Available = true)
    {
        public override string ToString() => Display;
    }

    private readonly IUniversityStore _store;
    private readonly UserSession _session;
    private readonly EntitySpec _entity;
    private readonly int? _id;
    private readonly Dictionary<string, Control> _inputs = [];
    private readonly Dictionary<string, string> _initial = [];
    private readonly DataRow? _original;
    private readonly ErrorProvider _errors;
    private readonly Panel _fieldsHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Label _status = UiTheme.Label("Preparing form…");
    private readonly Button _save;
    private readonly Button _reset = UiTheme.Button("&Reset fields");
    private readonly Button _retry = UiTheme.Button("Retry &loading");
    private readonly Button _cancel = UiTheme.Button("&Cancel");
    private Task? _prepareTask;
    private bool _busy;
    private bool _ready;
    private bool _discardConfirmed;

    public int? SavedId { get; private set; }

    public RecordEditorDialog(IUniversityStore store, UserSession session, EntitySpec entity, DataRow? row = null)
    {
        _store = store;
        _session = session;
        _entity = entity;
        _original = row;
        _id = row is null ? null : Convert.ToInt32(row[entity.IdColumn], CultureInfo.InvariantCulture);
        UiTheme.Apply(this);
        Text = row is null ? $"Add {entity.Singular}" : $"Edit {entity.Singular}";
        ClientSize = new Size(640, 670);
        MinimumSize = new Size(540, 420);
        MaximizeBox = true;
        MinimizeBox = false;
        _errors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
        _save = UiTheme.Button(entity.Finance ? (row is null ? "&Record payment" : "&Save payment") : "&Save record", primary: true);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(24)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(UiTheme.Label(Text, 22), 0, 0);
        var help = UiTheme.Label("All fields are required. Changes are saved only when you select Save.");
        help.ForeColor = UiTheme.Muted;
        help.Dock = DockStyle.Fill;
        layout.Controls.Add(help, 0, 1);
        layout.Controls.Add(_fieldsHost, 0, 2);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
            Padding = new Padding(0, 12, 28, 8)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var tabIndex = 0;
        var mnemonics = new HashSet<char>(['s', 'c', 'r', 'l']);
        foreach (var field in entity.Fields)
        {
            var label = UiTheme.Label(WithMnemonic(field.Label, mnemonics));
            label.TabIndex = tabIndex++;
            label.Margin = new Padding(0, 12, 0, 4);
            var input = CreateInput(field);
            input.Name = field.Name;
            input.AccessibleName = field.Label;
            input.AccessibleDescription = $"Required. {field.Label}.";
            input.TabIndex = tabIndex++;
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, 0, 0, 4);
            _inputs.Add(field.Name, input);
            fields.Controls.Add(label, 0, fields.RowCount++);
            fields.Controls.Add(input, 0, fields.RowCount++);
            PopulateValue(field, input, row);
            _errors.SetIconAlignment(input, ErrorIconAlignment.MiddleRight);
        }
        _fieldsHost.Controls.Add(fields);
        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(0, 12, 0, 4);
        _status.AccessibleName = "Form status";
        layout.Controls.Add(_status, 0, 3);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = true
        };
        _save.TabIndex = tabIndex++;
        _cancel.TabIndex = tabIndex++;
        _reset.TabIndex = tabIndex++;
        _retry.TabIndex = tabIndex;
        actions.Controls.AddRange([_save, _cancel, _reset, _retry]);
        layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout);
        _retry.Visible = false;
        _save.Enabled = false;
        _save.Click += async (_, _) => await SaveAsync();
        _cancel.Click += (_, _) => Close();
        _reset.Click += (_, _) => ResetFields();
        _retry.Click += async (_, _) => { _prepareTask = null; await PrepareAsync(); };
        AcceptButton = _save;
        CancelButton = _cancel;
        Shown += async (_, _) => await PrepareAsync();
        FormClosing += OnClosing;
    }

    public Task PrepareAsync() => _prepareTask ??= PrepareCoreAsync();

    private async Task PrepareCoreAsync()
    {
        SetBusy(true, "Loading available choices…");
        try
        {
            var lookupTables = new Dictionary<string, DataTable>();
            foreach (var key in _entity.Fields.Where(f => f.Kind == FieldKind.Lookup).Select(f => f.LookupKey!).Distinct())
                lookupTables[key] = await _store.LoadAsync(_session, Catalog.Get(key));
            foreach (var field in _entity.Fields.Where(f => f.Kind == FieldKind.Lookup))
            {
                var combo = (ComboBox)_inputs[field.Name];
                var source = Catalog.Get(field.LookupKey!);
                combo.Items.Clear();
                foreach (DataRow row in lookupTables[field.LookupKey!].Rows)
                {
                    var id = Convert.ToInt32(row[source.IdColumn], CultureInfo.InvariantCulture);
                    var display = Convert.ToString(row[source.DisplayColumn], CultureInfo.CurrentCulture) ?? "Unnamed record";
                    combo.Items.Add(new LookupItem(id, $"{display} · ID {id}"));
                }
                var originalValue = ValueOf(_original, field.Name);
                if (originalValue is not null)
                {
                    var originalId = Convert.ToInt32(originalValue, CultureInfo.InvariantCulture);
                    var selected = combo.Items.OfType<LookupItem>().FirstOrDefault(i => i.Id == originalId);
                    if (selected is null)
                    {
                        selected = new LookupItem(originalId, $"Unavailable record · ID {originalId}", false);
                        combo.Items.Add(selected);
                    }
                    combo.SelectedItem = selected;
                }
                else combo.SelectedIndex = -1;
            }
            _ready = true;
            RememberInitialValues();
            _retry.Visible = false;
            ShowStatus(_id is null ? "Ready to add a record." : $"Editing record ID {_id}.");
            if (_inputs.Values.FirstOrDefault() is { } first) ActiveControl = first;
        }
        catch (Exception ex)
        {
            _ready = false;
            _retry.Visible = true;
            ShowStatus(SafeError(ex, "load available choices"), true);
        }
        finally { SetBusy(false); }
    }

    private Control CreateInput(FieldSpec field) => field.Kind switch
    {
        FieldKind.Number or FieldKind.Money => new NumericUpDown
        {
            Minimum = 0, Maximum = field.Max, Value = 0,
            DecimalPlaces = field.Kind == FieldKind.Money ? 2 : 0,
            ThousandsSeparator = true,
            Increment = field.Kind == FieldKind.Money ? 100 : 1,
            BackColor = UiTheme.Canvas, ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        },
        FieldKind.Date => new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy",
            ShowCheckBox = field.Name == "DOB", Checked = field.Name != "DOB",
            MinDate = new DateTime(1900, 1, 1), MaxDate = DateTime.Today,
            Value = field.Name == "DOB" ? DateTime.Today.AddYears(-18) : DateTime.Today
        },
        FieldKind.Choice or FieldKind.Lookup => new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = UiTheme.Canvas, ForeColor = UiTheme.Text,
            MaxDropDownItems = 14, IntegralHeight = false, DropDownHeight = 280
        },
        _ => new TextBox
        {
            MaxLength = field.MaxLength, Multiline = field.Kind == FieldKind.Multiline,
            Height = field.Kind == FieldKind.Multiline ? 76 : 32,
            ScrollBars = field.Kind == FieldKind.Multiline ? ScrollBars.Vertical : ScrollBars.None,
            AcceptsReturn = field.Kind == FieldKind.Multiline,
            BackColor = UiTheme.Canvas, ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        }
    };

    private static string WithMnemonic(string label, HashSet<char> used)
    {
        for (int i = 0; i < label.Length; i++)
        {
            char candidate = char.ToLowerInvariant(label[i]);
            if (char.IsLetter(candidate) && used.Add(candidate)) return label.Insert(i, "&");
        }
        return label;
    }

    private static object? ValueOf(DataRow? row, string column) => row is not null && row.Table.Columns.Contains(column) && !row.IsNull(column) ? row[column] : null;

    private static void PopulateValue(FieldSpec field, Control input, DataRow? row)
    {
        var value = ValueOf(row, field.Name);
        switch (input)
        {
            case TextBox text: text.Text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? ""; break;
            case NumericUpDown number:
                var numberValue = value is null ? 0 : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                // Preserve a legacy value until the user explicitly corrects it.
                // Save validates the catalog range; opening an editor must not truncate data.
                number.Minimum = Math.Min(number.Minimum, numberValue);
                number.Maximum = Math.Max(number.Maximum, numberValue);
                number.Value = numberValue;
                break;
            case DateTimePicker date:
                if (value is not null)
                {
                    var dateValue = Convert.ToDateTime(value, CultureInfo.InvariantCulture).Date;
                    if (dateValue < DateTimePicker.MinimumDateTime || dateValue > DateTimePicker.MaximumDateTime)
                        throw new InvalidOperationException($"The existing {field.Label.ToLowerInvariant()} cannot be displayed by Windows. Correct it in the database before editing this record.");
                    if (dateValue < date.MinDate) date.MinDate = dateValue;
                    if (dateValue > date.MaxDate) date.MaxDate = dateValue;
                    date.Value = dateValue;
                    date.Checked = true;
                }
                break;
            case ComboBox combo when field.Kind == FieldKind.Choice:
                combo.Items.Clear();
                combo.Items.AddRange(field.Options ?? []);
                if (value is not null)
                {
                    var option = Convert.ToString(value, CultureInfo.InvariantCulture)!;
                    if (!combo.Items.Contains(option)) combo.Items.Add(option);
                    combo.SelectedItem = option;
                }
                else combo.SelectedIndex = -1;
                break;
        }
    }

    private async Task SaveAsync()
    {
        if (_busy || !_ready) return;
        _errors.Clear();
        var values = new Dictionary<string, object?>();
        Control? firstInvalid = null;
        foreach (var field in _entity.Fields)
        {
            var input = _inputs[field.Name];
            string? error = null;
            object? value = null;
            switch (input)
            {
                case TextBox text:
                    value = text.Text.Trim();
                    if (string.IsNullOrWhiteSpace((string)value)) error = $"Enter {field.Label.ToLowerInvariant()}.";
                    else if (((string)value).Length > field.MaxLength) error = $"Use {field.MaxLength} characters or fewer.";
                    else if (((string)value).Any(c => char.IsControl(c) && (field.Kind != FieldKind.Multiline || c is not ('\r' or '\n' or '\t'))))
                        error = $"Remove unsupported control characters from {field.Label.ToLowerInvariant()}.";
                    break;
                case NumericUpDown number:
                    value = field.Kind == FieldKind.Number ? (object)decimal.ToInt32(number.Value) : number.Value;
                    if (number.Value < field.Min || number.Value > field.Max)
                        error = $"Enter a value between {field.Min:N2} and {field.Max:N2}.";
                    break;
                case DateTimePicker date:
                    value = date.Value.Date;
                    if (date.ShowCheckBox && !date.Checked) error = $"Select {field.Label.ToLowerInvariant()}.";
                    else if (date.Value.Date < new DateTime(1900, 1, 1) || date.Value.Date > DateTime.Today)
                        error = "Choose a date between 1 January 1900 and today.";
                    break;
                case ComboBox combo when field.Kind == FieldKind.Lookup:
                    if (combo.SelectedItem is LookupItem { Available: true } selected) value = selected.Id;
                    else error = $"Select an available {field.Label.ToLowerInvariant()}.";
                    break;
                case ComboBox combo:
                    value = combo.SelectedItem?.ToString();
                    if (value is null || !(field.Options ?? []).Contains((string)value)) error = $"Select {field.Label.ToLowerInvariant()} from the list.";
                    break;
            }
            if (error is not null)
            {
                _errors.SetError(input, error);
                input.AccessibleDescription = error;
                firstInvalid ??= input;
            }
            else input.AccessibleDescription = $"Required. {field.Label}.";
            values[field.Name] = value;
        }
        if (firstInvalid is not null)
        {
            ShowStatus(_errors.GetError(firstInvalid), true);
            firstInvalid.Focus();
            return;
        }
        SetBusy(true, "Saving record…");
        try
        {
            SavedId = await _store.SaveAsync(_session, _entity, _id, values);
            _discardConfirmed = true;
            SetBusy(false);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { ShowStatus(SafeError(ex, "save this record"), true); }
        finally { SetBusy(false); }
    }

    private static string InputValue(Control control) => control switch
    {
        DateTimePicker date => $"{date.Checked}:{date.Value.Date:yyyy-MM-dd}",
        NumericUpDown number => number.Value.ToString(CultureInfo.InvariantCulture),
        ComboBox combo => combo.SelectedItem is LookupItem item ? item.Id.ToString(CultureInfo.InvariantCulture) : combo.SelectedItem?.ToString() ?? "",
        _ => control.Text
    };

    private void RememberInitialValues()
    {
        _initial.Clear();
        foreach (var pair in _inputs) _initial[pair.Key] = InputValue(pair.Value);
    }

    private bool IsDirty => _ready && _inputs.Any(pair => _initial.GetValueOrDefault(pair.Key) != InputValue(pair.Value));

    private bool ConfirmDiscard() => !IsDirty || MessageBox.Show(this,
        "Discard the changes you have made?", "Unsaved changes", MessageBoxButtons.OKCancel,
        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.OK;

    private void ResetFields()
    {
        if (_busy || !ConfirmDiscard()) return;
        foreach (var field in _entity.Fields)
        {
            var input = _inputs[field.Name];
            if (input is ComboBox combo && field.Kind == FieldKind.Lookup)
            {
                var originalValue = ValueOf(_original, field.Name);
                combo.SelectedItem = originalValue is null ? null : combo.Items.OfType<LookupItem>()
                    .FirstOrDefault(i => i.Id == Convert.ToInt32(originalValue, CultureInfo.InvariantCulture));
            }
            else
            {
                if (input is DateTimePicker date && _original is null)
                {
                    date.Value = field.Name == "DOB" ? DateTime.Today.AddYears(-18) : DateTime.Today;
                    date.Checked = field.Name != "DOB";
                }
                PopulateValue(field, input, _original);
            }
            input.AccessibleDescription = $"Required. {field.Label}.";
        }
        _errors.Clear();
        ShowStatus(_id is null ? "Fields cleared. No records have been changed." : "Fields restored to the saved values.");
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }
        if (!_discardConfirmed && !ConfirmDiscard()) e.Cancel = true;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _fieldsHost.Enabled = !busy && _ready;
        _save.Enabled = !busy && _ready;
        _reset.Enabled = !busy && _ready;
        _cancel.Enabled = !busy;
        _retry.Enabled = !busy;
        UseWaitCursor = busy;
        if (status is not null) ShowStatus(status);
    }

    private void ShowStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? UiTheme.Danger : UiTheme.Muted;
    }

    internal static string SafeError(Exception error, string action) => error switch
    {
        ArgumentException => $"Could not {action}. Check the entered values and selected records.",
        UnauthorizedAccessException => "Your account does not have permission for this action.",
        InvalidOperationException => $"Could not {action}. {error.Message}",
        _ => $"Could not {action}. Check your database connection and try again. Your input has been kept."
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _errors.Dispose();
        base.Dispose(disposing);
    }
}
