using System.Data;
using System.Globalization;

namespace University_Management_System_2;

/// <summary>Ephemeral fictional records. This store never reads or writes an MDF file.</summary>
public sealed class DemoUniversityStore : IUniversityStore
{
    private readonly Dictionary<string, DataTable> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Hash, UserRole Role)> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _nextIds = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsDemo => true;

    public DemoUniversityStore(bool seed = true)
    {
        foreach (EntitySpec entity in Catalog.Entities)
        {
            DataTable table = new(entity.Table) { Locale = CultureInfo.InvariantCulture };
            table.Columns.Add(entity.IdColumn, typeof(int));
            foreach (FieldSpec field in entity.Fields)
                table.Columns.Add(field.Name, field.Kind switch
                {
                    FieldKind.Number or FieldKind.Lookup => typeof(int),
                    FieldKind.Money => typeof(decimal), FieldKind.Date => typeof(DateTime), _ => typeof(string)
                });
            table.PrimaryKey = [table.Columns[entity.IdColumn]!];
            _tables.Add(entity.Key, table);
            _nextIds.Add(entity.Key, 1);
        }
        if (seed) Seed();
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<bool> HasAccountsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return _accounts.Count > 0; }
        finally { _gate.Release(); }
    }

    public async Task<UserSession?> SignInAsync(string username, string password, CancellationToken ct = default)
    {
        string name;
        try { name = PasswordHasher.NormalizeUsername(username); }
        catch (InvalidOperationException) { return null; }
        await _gate.WaitAsync(ct);
        try
        {
            if (!_accounts.TryGetValue(name, out var account))
            {
                PasswordHasher.PadMissingAccount(password);
                return null;
            }
            return PasswordHasher.Verify(password, account.Hash) ? new UserSession(name, account.Role) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task CreateAccountAsync(UserSession? actor, string username, string password, UserRole role, CancellationToken ct = default)
    {
        string name = PasswordHasher.NormalizeUsername(username);
        PasswordHasher.ValidateNewPassword(password);
        if (!Enum.IsDefined(role)) throw new InvalidOperationException("Choose a supported account role.");
        await _gate.WaitAsync(ct);
        try
        {
            if (_accounts.Count == 0)
            {
                if (actor is not null || role != UserRole.Administrator)
                    throw new InvalidOperationException("The first account must be an administrator created during setup.");
            }
            else if (actor?.Role != UserRole.Administrator ||
                     !_accounts.TryGetValue(actor.Username, out var existing) || existing.Role != UserRole.Administrator)
                throw new InvalidOperationException("Sign in as an administrator to create accounts.");
            if (_accounts.ContainsKey(name)) throw new InvalidOperationException("That username is already in use. Choose another username.");
            _accounts.Add(name, (PasswordHasher.Hash(password), role));
        }
        finally { _gate.Release(); }
    }

    public async Task<DataTable> LoadAsync(UserSession actor, EntitySpec entity, CancellationToken ct = default)
    {
        RecordValidation.EnsurePermission(actor, entity);
        await _gate.WaitAsync(ct);
        try
        {
            DataTable result = _tables[entity.Key].Copy();
            foreach (DerivedField derived in entity.Derived) result.Columns.Add(derived.Name, typeof(string));
            foreach (DataRow row in result.Rows)
                foreach (DerivedField derived in entity.Derived)
                    row[derived.Name] = ResolveDerived(entity, row, derived) ?? (object)DBNull.Value;
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<int> SaveAsync(UserSession actor, EntitySpec entity, int? id, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        RecordValidation.EnsurePermission(actor, entity);
        Dictionary<string, object?> normalized = RecordValidation.Normalize(entity, values);
        await _gate.WaitAsync(ct);
        try
        {
            DataTable table = _tables[entity.Key];
            DataRow? row = id.HasValue ? table.Rows.Find(id.Value) : null;
            if (id.HasValue && (id.Value <= 0 || row is null))
                throw new InvalidOperationException("This record no longer exists. Refresh the list and choose a record again.");
            foreach (FieldSpec field in entity.Fields.Where(f => f.Kind == FieldKind.Lookup))
                if (_tables[field.LookupKey!].Rows.Find(normalized[field.Name]) is null)
                    throw new InvalidOperationException($"The selected {field.Label.ToLowerInvariant()} no longer exists. Refresh the list and choose again.");
            if (entity.Key == "courses")
            {
                DataRow faculty = _tables["faculty"].Rows.Find(normalized["Fac_Id"])!;
                if (!Equals(faculty["DeptId"], normalized["DeptId"]))
                    throw new InvalidOperationException("Choose a faculty member from the course's department.");
            }
            if (entity.Key == "faculty" && id.HasValue &&
                _tables["courses"].AsEnumerable().Any(r => Equals(r["Fac_Id"], id.Value) && !Equals(r["DeptId"], normalized["DeptId"])))
                throw new InvalidOperationException("Reassign this faculty member's courses before changing their department.");
            bool added = row is null;
            row ??= table.NewRow();
            int recordId = id ?? _nextIds[entity.Key]++;
            row[entity.IdColumn] = recordId;
            foreach (var value in normalized) row[value.Key] = value.Value ?? DBNull.Value;
            if (added) table.Rows.Add(row);
            return recordId;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(UserSession actor, EntitySpec entity, int id, CancellationToken ct = default)
    {
        RecordValidation.EnsurePermission(actor, entity);
        await _gate.WaitAsync(ct);
        try
        {
            DataRow? selected = _tables[entity.Key].Rows.Find(id);
            if (id <= 0 || selected is null)
                throw new InvalidOperationException("This record no longer exists. Refresh the list and choose a record again.");
            foreach (EntitySpec child in Catalog.Entities)
                foreach (FieldSpec field in child.Fields.Where(f => f.Kind == FieldKind.Lookup && f.LookupKey == entity.Key))
                    if (_tables[child.Key].AsEnumerable().Any(row => Equals(row[field.Name], id)))
                        throw new InvalidOperationException($"This {entity.Singular} is used by {child.Title.ToLowerInvariant()}. Reassign or remove those records first.");
            _tables[entity.Key].Rows.Remove(selected);
        }
        finally { _gate.Release(); }
    }

    private object? ResolveDerived(EntitySpec entity, DataRow row, DerivedField derived)
    {
        EntitySpec lookup = Catalog.Get(derived.LookupKey);
        DataRow? parent = _tables[lookup.Key].Rows.Find(row[derived.ForeignKey]);
        if (parent is null) return null;
        DerivedField? nested = lookup.Derived.FirstOrDefault(d => d.Name == derived.SourceColumn);
        return nested is null ? parent[derived.SourceColumn] : ResolveDerived(lookup, parent, nested);
    }

    private void Seed()
    {
        Add("departments", "Computer Science", 180, 95000m);
        Add("departments", "Business Administration", 120, 82000m);
        Add("departments", "Electrical Engineering", 90, 110000m);
        Add("faculty", "Dr. Sana Rahman", new DateTime(1984, 5, 12), "Female", "17 Garden Avenue, Lahore", "PhD Computer Science", 12, 1, 165000m);
        Add("faculty", "Prof. Hamza Ali", new DateTime(1981, 9, 3), "Male", "42 College Road, Islamabad", "MBA / MPhil", 15, 2, 150000m);
        Add("faculty", "Dr. Mariam Shah", new DateTime(1988, 2, 20), "Female", "8 University Road, Karachi", "PhD Electrical Engineering", 9, 3, 172000m);
        Add("students", "Ayesha Malik", new DateTime(2004, 4, 10), "Female", "35 Park Street, Lahore", 1, "+92 300 555 0101", "5");
        Add("students", "Bilal Ahmed", new DateTime(2005, 8, 22), "Male", "12 Model Town, Islamabad", 1, "+92 300 555 0102", "3");
        Add("students", "Hira Hassan", new DateTime(2003, 11, 14), "Female", "29 Clifton Road, Karachi", 2, "+92 300 555 0103", "7");
        Add("students", "Omar Khan", new DateTime(2005, 1, 6), "Male", "6 Green Lane, Lahore", 3, "+92 300 555 0104", "3");
        Add("courses", "Human–Computer Interaction", 3, 1, 1);
        Add("courses", "Data Structures and Algorithms", 4, 1, 1);
        Add("courses", "Organizational Behaviour", 3, 2, 2);
        Add("courses", "Digital Logic Design", 4, 3, 3);
        Add("campuses", "Main Campus", "Lahore", "Dr. Nadia Farooq", "1", new DateTime(2012, 8, 1));
        Add("campuses", "City Campus", "Islamabad", "Dr. Saad Qureshi", "2", new DateTime(2018, 9, 1));
        Add("fees", 1, 95000m, "5", DateTime.Today.AddDays(-8));
        Add("fees", 2, 47500m, "3", DateTime.Today.AddDays(-5));
        Add("fees", 3, 82000m, "7", DateTime.Today.AddDays(-3));
        Add("salaries", 1, 165000m, DateTime.Today.AddDays(-10));
        Add("salaries", 2, 150000m, DateTime.Today.AddDays(-10));
        Add("salaries", 3, 172000m, DateTime.Today.AddDays(-10));
    }

    private void Add(string key, params object[] values)
    {
        object[] complete = new object[values.Length + 1];
        complete[0] = _nextIds[key]++;
        values.CopyTo(complete, 1);
        _tables[key].Rows.Add(complete);
    }
}
