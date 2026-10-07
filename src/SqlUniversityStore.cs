using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace University_Management_System_2;

/// <summary>
/// The caller selects a copied working database or an explicitly managed SQL database.
/// SQL identifiers come exclusively from Catalog; user values are always parameters.
/// </summary>
public sealed class SqlUniversityStore : IUniversityStore
{
    private readonly string _connectionString;
    public bool IsDemo => false;

    public SqlUniversityStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Configure a database connection before continuing.");
        try { _connectionString = new SqlConnectionStringBuilder(connectionString).ConnectionString; }
        catch (ArgumentException) { throw new InvalidOperationException("The database connection setting is invalid. Check UMS_CONNECTION_STRING."); }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await using SqlConnection connection = await OpenAsync(ct);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            using Stream stream = typeof(SqlUniversityStore).Assembly.GetManifestResourceStream("University_Management_System_2.Schema.sql")
                ?? throw new InvalidOperationException("The database setup resource is missing. Reinstall the application.");
            using StreamReader reader = new(stream);
            string schema = await reader.ReadToEndAsync(ct);
            await using (SqlCommand command = Command(connection, transaction, schema)) await command.ExecuteNonQueryAsync(ct);
            await UpgradeMonetaryColumnsAsync(connection, transaction, ct);
            await using (SqlCommand count = Command(connection, transaction, "SELECT COUNT(*) FROM dbo.UmsAccounts WITH (UPDLOCK,HOLDLOCK)"))
                if (Convert.ToInt32(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0)
                    await MigrateLegacyAccountsAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException ex) { throw DatabaseError(ex); }
    }

    public async Task<bool> HasAccountsAsync(CancellationToken ct = default)
    {
        try
        {
            await using SqlConnection connection = await OpenAsync(ct);
            await using SqlCommand command = Command(connection, null, "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.UmsAccounts) THEN 1 ELSE 0 END");
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
        }
        catch (SqlException ex) { throw DatabaseError(ex); }
    }

    public async Task<UserSession?> SignInAsync(string username, string password, CancellationToken ct = default)
    {
        string name;
        try { name = PasswordHasher.NormalizeUsername(username); }
        catch (InvalidOperationException) { return null; }
        if (password is null || password.Length is < 1 or > PasswordHasher.MaxPasswordLength) return null;
        try
        {
            await using SqlConnection connection = await OpenAsync(ct);
            await using SqlCommand command = Command(connection, null, "SELECT Username,PasswordHash,Role FROM dbo.UmsAccounts WHERE Username=@username");
            command.Parameters.Add("@username", SqlDbType.NVarChar, 100).Value = name;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                await Task.Run(() => PasswordHasher.PadMissingAccount(password), ct);
                return null;
            }
            string actualName = reader.GetString(0), hash = reader.GetString(1), roleText = reader.GetString(2);
            bool valid = await Task.Run(() => PasswordHasher.Verify(password, hash), ct);
            return valid && Enum.TryParse(roleText, false, out UserRole role) && Enum.IsDefined(role)
                ? new UserSession(actualName, role) : null;
        }
        catch (SqlException ex) { throw DatabaseError(ex); }
    }

    public async Task CreateAccountAsync(UserSession? actor, string username, string password, UserRole role, CancellationToken ct = default)
    {
        string name = PasswordHasher.NormalizeUsername(username);
        PasswordHasher.ValidateNewPassword(password);
        if (!Enum.IsDefined(role)) throw new InvalidOperationException("Choose a supported account role.");
        string hash = await Task.Run(() => PasswordHasher.Hash(password), ct);
        try
        {
            await using SqlConnection connection = await OpenAsync(ct);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            int count;
            await using (SqlCommand command = Command(connection, transaction, "SELECT COUNT(*) FROM dbo.UmsAccounts WITH (UPDLOCK,HOLDLOCK)"))
                count = Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (count == 0)
            {
                if (actor is not null || role != UserRole.Administrator)
                    throw new InvalidOperationException("The first account must be an administrator created during setup.");
            }
            else
            {
                if (actor?.Role != UserRole.Administrator)
                    throw new InvalidOperationException("Sign in as an administrator to create accounts.");
                await EnsureActorAsync(connection, transaction, actor, ct);
            }
            await InsertAccountAsync(connection, transaction, name, hash, role, ct);
            await MigrateLegacyAccountsAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException ex) { throw DatabaseError(ex); }
    }

    public async Task<DataTable> LoadAsync(UserSession actor, EntitySpec entity, CancellationToken ct = default)
    {
        RecordValidation.EnsurePermission(actor, entity);
        try
        {
            await using SqlConnection connection = await OpenAsync(ct);
            await EnsureActorAsync(connection, null, actor, ct);
            await using SqlCommand command = Command(connection, null, BuildSelect(entity));
            await using SqlDataReader reader = await command.ExecuteReaderAsync(ct);
            DataTable result = new(entity.Table) { Locale = CultureInfo.InvariantCulture };
            for (int i = 0; i < reader.FieldCount; i++) result.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
            while (await reader.ReadAsync(ct))
            {
                object[] values = new object[reader.FieldCount];
                reader.GetValues(values);
                result.Rows.Add(values);
            }
            return result;
        }
        catch (SqlException ex) { throw DatabaseError(ex); }
    }

    public async Task<int> SaveAsync(UserSession actor, EntitySpec entity, int? id, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        RecordValidation.EnsurePermission(actor, entity);
        Dictionary<string, object?> normalized = RecordValidation.Normalize(entity, values);
        if (id.HasValue && id.Value <= 0) throw StaleRecord();
        try
        {
            await using SqlConnection connection = await OpenAsync(ct);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await EnsureActorAsync(connection, transaction, actor, ct);
            if (id.HasValue) await EnsureRecordAsync(connection, transaction, entity, id.Value, ct);
            foreach (FieldSpec field in entity.Fields.Where(f => f.Kind == FieldKind.Lookup))
            {
                EntitySpec parent = Catalog.Get(field.LookupKey!);
                if (!await ExistsAsync(connection, transaction, parent, Convert.ToInt32(normalized[field.Name]), ct))
                    throw new InvalidOperationException($"The selected {field.Label.ToLowerInvariant()} no longer exists. Refresh the list and choose again.");
            }
            await ValidateRelationshipsAsync(connection, transaction, entity, id, normalized, ct);
            foreach (DerivedField derived in entity.Derived)
                normalized[derived.Name] = await ReadDerivedAsync(connection, transaction, entity, normalized, derived, ct);
            string[] columns = entity.Fields.Select(f => f.Name).Concat(entity.Derived.Select(d => d.Name)).ToArray();
            string sql = id.HasValue
                ? $"UPDATE {Table(entity)} SET {string.Join(",", columns.Select((c, i) => $"{Quote(c)}=@v{i}"))} OUTPUT INSERTED.{Quote(entity.IdColumn)} WHERE {Quote(entity.IdColumn)}=@id"
                : $"INSERT INTO {Table(entity)} ({string.Join(",", columns.Select(Quote))}) OUTPUT INSERTED.{Quote(entity.IdColumn)} VALUES ({string.Join(",", columns.Select((_, i) => $"@v{i}"))})";
            await using SqlCommand command = Command(connection, transaction, sql);
            for (int i = 0; i < columns.Length; i++) AddValue(command, $"@v{i}", entity.Fields.FirstOrDefault(f => f.Name == columns[i]), normalized[columns[i]]);
            if (id.HasValue) command.Parameters.Add("@id", SqlDbType.Int).Value = id.Value;
            object? result = await command.ExecuteScalarAsync(ct);
            if (result is null || result is DBNull) throw StaleRecord();
            int savedId = Convert.ToInt32(result, CultureInfo.InvariantCulture);
            await transaction.CommitAsync(ct);
            return savedId;
        }
        catch (SqlException ex) { throw DatabaseError(ex); }
    }

    public async Task DeleteAsync(UserSession actor, EntitySpec entity, int id, CancellationToken ct = default)
    {
        RecordValidation.EnsurePermission(actor, entity);
        if (id <= 0) throw StaleRecord();
        try
        {
            await using SqlConnection connection = await OpenAsync(ct);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await EnsureActorAsync(connection, transaction, actor, ct);
            await EnsureRecordAsync(connection, transaction, entity, id, ct);
            foreach (EntitySpec child in Catalog.Entities)
                foreach (FieldSpec field in child.Fields.Where(f => f.Kind == FieldKind.Lookup && f.LookupKey == entity.Key))
                {
                    await using SqlCommand check = Command(connection, transaction,
                        $"SELECT TOP (1) 1 FROM {Table(child)} WITH (UPDLOCK,HOLDLOCK) WHERE {Quote(field.Name)}=@id");
                    check.Parameters.Add("@id", SqlDbType.Int).Value = id;
                    if (await check.ExecuteScalarAsync(ct) is not null)
                        throw new InvalidOperationException($"This {entity.Singular} is used by {child.Title.ToLowerInvariant()}. Reassign or remove those records first.");
                }
            await using SqlCommand command = Command(connection, transaction, $"DELETE FROM {Table(entity)} WHERE {Quote(entity.IdColumn)}=@id");
            command.Parameters.Add("@id", SqlDbType.Int).Value = id;
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw StaleRecord();
            await transaction.CommitAsync(ct);
        }
        catch (SqlException ex) { throw DatabaseError(ex); }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        SqlConnection connection = new(_connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql)
        => new(sql, connection, transaction) { CommandTimeout = 30 };
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
    private static string Table(EntitySpec entity) => $"[dbo].{Quote(entity.Table)}";
    private static InvalidOperationException StaleRecord() => new("This record no longer exists. Refresh the list and choose a record again.");

    private static void AddValue(SqlCommand command, string name, FieldSpec? field, object? value)
    {
        SqlParameter parameter = field?.Kind switch
        {
            FieldKind.Number or FieldKind.Lookup => command.Parameters.Add(name, SqlDbType.Int),
            FieldKind.Date => command.Parameters.Add(name, SqlDbType.Date),
            FieldKind.Money => command.Parameters.Add(name, SqlDbType.Decimal),
            _ => command.Parameters.Add(name, SqlDbType.NVarChar, field?.MaxLength ?? 100)
        };
        if (field?.Kind == FieldKind.Money) { parameter.Precision = 18; parameter.Scale = 2; }
        parameter.Value = value ?? DBNull.Value;
    }

    private static async Task EnsureActorAsync(SqlConnection connection, SqlTransaction? transaction, UserSession actor, CancellationToken ct)
    {
        await using SqlCommand command = Command(connection, transaction, "SELECT Role FROM dbo.UmsAccounts WHERE Username=@username");
        command.Parameters.Add("@username", SqlDbType.NVarChar, 100).Value = actor.Username;
        if (!string.Equals(await command.ExecuteScalarAsync(ct) as string, actor.Role.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("Your account access has changed. Sign in again before continuing.");
    }

    private static async Task<bool> ExistsAsync(SqlConnection connection, SqlTransaction transaction, EntitySpec entity, int id, CancellationToken ct)
    {
        await using SqlCommand command = Command(connection, transaction, $"SELECT TOP (1) 1 FROM {Table(entity)} WITH (UPDLOCK,HOLDLOCK) WHERE {Quote(entity.IdColumn)}=@id");
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task EnsureRecordAsync(SqlConnection connection, SqlTransaction transaction, EntitySpec entity, int id, CancellationToken ct)
    {
        if (!await ExistsAsync(connection, transaction, entity, id, ct)) throw StaleRecord();
    }

    private static async Task ValidateRelationshipsAsync(SqlConnection connection, SqlTransaction transaction, EntitySpec entity, int? id, Dictionary<string, object?> values, CancellationToken ct)
    {
        if (entity.Key == "courses")
        {
            await using SqlCommand command = Command(connection, transaction, "SELECT DeptId FROM dbo.FacultyTbl WITH (UPDLOCK,HOLDLOCK) WHERE F_Id=@faculty");
            command.Parameters.Add("@faculty", SqlDbType.Int).Value = values["Fac_Id"]!;
            if (!Equals(await command.ExecuteScalarAsync(ct), values["DeptId"]))
                throw new InvalidOperationException("Choose a faculty member from the course's department.");
        }
        if (entity.Key == "faculty" && id.HasValue)
        {
            await using SqlCommand command = Command(connection, transaction,
                "SELECT TOP (1) 1 FROM dbo.CourseTbl WITH (UPDLOCK,HOLDLOCK) WHERE Fac_Id=@faculty AND (DeptId<>@department OR DeptId IS NULL)");
            command.Parameters.Add("@faculty", SqlDbType.Int).Value = id.Value;
            command.Parameters.Add("@department", SqlDbType.Int).Value = values["DeptId"]!;
            if (await command.ExecuteScalarAsync(ct) is not null)
                throw new InvalidOperationException("Reassign this faculty member's courses before changing their department.");
        }
    }

    private static string BuildSelect(EntitySpec entity)
    {
        var joins = new List<string>();
        var columns = new List<string> { $"t0.{Quote(entity.IdColumn)}" };
        columns.AddRange(entity.Fields.Select(f => $"t0.{Quote(f.Name)}"));
        foreach (DerivedField derived in entity.Derived)
            columns.Add($"{DerivedExpression(entity, "t0", derived, joins)} AS {Quote(derived.Name)}");
        return $"SELECT {string.Join(",", columns)} FROM {Table(entity)} t0 {string.Join(" ", joins)} ORDER BY t0.{Quote(entity.IdColumn)} DESC";
    }

    private static string DerivedExpression(EntitySpec entity, string alias, DerivedField derived, List<string> joins)
    {
        EntitySpec lookup = Catalog.Get(derived.LookupKey);
        string next = $"t{joins.Count + 1}";
        joins.Add($"LEFT JOIN {Table(lookup)} {next} ON {alias}.{Quote(derived.ForeignKey)}={next}.{Quote(lookup.IdColumn)}");
        DerivedField? nested = lookup.Derived.FirstOrDefault(d => d.Name == derived.SourceColumn);
        return nested is null ? $"{next}.{Quote(derived.SourceColumn)}" : DerivedExpression(lookup, next, nested, joins);
    }

    private static async Task<object?> ReadDerivedAsync(SqlConnection connection, SqlTransaction transaction, EntitySpec entity, Dictionary<string, object?> values, DerivedField derived, CancellationToken ct)
    {
        EntitySpec lookup = Catalog.Get(derived.LookupKey);
        DerivedField? nested = lookup.Derived.FirstOrDefault(d => d.Name == derived.SourceColumn);
        var joins = new List<string>();
        string expression = nested is null ? $"t0.{Quote(derived.SourceColumn)}" : DerivedExpression(lookup, "t0", nested, joins);
        await using SqlCommand command = Command(connection, transaction,
            $"SELECT {expression} FROM {Table(lookup)} t0 {string.Join(" ", joins)} WHERE t0.{Quote(lookup.IdColumn)}=@id");
        command.Parameters.Add("@id", SqlDbType.Int).Value = values[derived.ForeignKey]!;
        object? result = await command.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            throw new InvalidOperationException($"The selected {lookup.Singular} has incomplete related information. Update it before continuing.");
        return result;
    }

    private static async Task InsertAccountAsync(SqlConnection connection, SqlTransaction transaction, string username, string hash, UserRole role, CancellationToken ct)
    {
        await using SqlCommand command = Command(connection, transaction,
            "INSERT INTO dbo.UmsAccounts (Username,PasswordHash,Role) VALUES (@username,@hash,@role)");
        command.Parameters.Add("@username", SqlDbType.NVarChar, 100).Value = username;
        command.Parameters.Add("@hash", SqlDbType.NVarChar, 200).Value = hash;
        command.Parameters.Add("@role", SqlDbType.NVarChar, 30).Value = role.ToString();
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("The account was not created. Retry setup.");
    }

    /// <summary>
    /// One-time import after admin bootstrap. Each legacy row is retained as a Registrar;
    /// case-insensitive duplicates/renamed invalid usernames get ~legacyN suffixes or a
    /// legacy-user-N name, with a noncredential mapping retained for administrator audit.
    /// Raw source files remain untouched by the caller's working-copy selection.
    /// </summary>
    private static async Task MigrateLegacyAccountsAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        await using (SqlCommand done = Command(connection, transaction, "SELECT [Value] FROM dbo.UmsSchemaMetadata WITH (UPDLOCK,HOLDLOCK) WHERE [Key]=N'LegacyAccountsImported'"))
            if (await done.ExecuteScalarAsync(ct) is not null) return;
        bool legacyExists;
        await using (SqlCommand exists = Command(connection, transaction, "SELECT CASE WHEN OBJECT_ID(N'dbo.LoginTbl',N'U') IS NULL THEN 0 ELSE 1 END"))
            legacyExists = Convert.ToInt32(await exists.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
        if (legacyExists)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (SqlCommand names = Command(connection, transaction, "SELECT Username FROM dbo.UmsAccounts WITH (UPDLOCK,HOLDLOCK)"))
            await using (SqlDataReader reader = await names.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) taken.Add(reader.GetString(0));
            var legacy = new List<(string Username, string Password)>();
            await using (SqlCommand command = Command(connection, transaction,
                "SELECT CAST([UserName] AS nvarchar(4000)),CAST([Password] AS nvarchar(max)) FROM dbo.LoginTbl WITH (UPDLOCK,HOLDLOCK) ORDER BY [UserName],[Password]"))
            await using (SqlDataReader reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    legacy.Add((reader.IsDBNull(0) ? "" : reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
            int ordinal = 0;
            foreach (var account in legacy)
            {
                ct.ThrowIfCancellationRequested();
                ordinal++;
                if (account.Password.Length is < 1 or > PasswordHasher.MaxPasswordLength)
                    throw new InvalidOperationException("A legacy account has an empty or oversized password. Repair that account in the copied database before setup; no account migration was committed.");
                string basis;
                try { basis = PasswordHasher.NormalizeUsername(account.Username); }
                catch (InvalidOperationException) { basis = $"legacy-user-{ordinal}"; }
                string name = basis;
                int suffix = ordinal;
                while (taken.Contains(name) || await AccountExistsAsync(connection, transaction, name, ct))
                {
                    string ending = $"~legacy{suffix++}";
                    name = basis[..Math.Min(basis.Length, 100 - ending.Length)] + ending;
                }
                taken.Add(name);
                string hash = await Task.Run(() => PasswordHasher.Hash(account.Password), ct);
                await InsertAccountAsync(connection, transaction, name, hash, UserRole.Registrar, ct);
                await using SqlCommand map = Command(connection, transaction,
                    "INSERT INTO dbo.UmsLegacyAccountMap (MigrationOrdinal,OriginalUsername,NewUsername) VALUES (@ordinal,@original,@new)");
                map.Parameters.Add("@ordinal", SqlDbType.Int).Value = ordinal;
                map.Parameters.Add("@original", SqlDbType.NVarChar, 4000).Value = account.Username;
                map.Parameters.Add("@new", SqlDbType.NVarChar, 100).Value = name;
                await map.ExecuteNonQueryAsync(ct);
            }
            // Both replacement and completed marker commit with every hash/account/map row.
            // Ten-character marker fits the original UI's common varchar(50) password field.
            await using SqlCommand scrub = Command(connection, transaction, "UPDATE dbo.LoginTbl SET [Password]=N'!migrated!'");
            await scrub.ExecuteNonQueryAsync(ct);
        }
        await using SqlCommand complete = Command(connection, transaction,
            "INSERT INTO dbo.UmsSchemaMetadata ([Key],[Value]) VALUES (N'LegacyAccountsImported',N'1')");
        await complete.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpgradeMonetaryColumnsAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        foreach (EntitySpec entity in Catalog.Entities)
        foreach (FieldSpec field in entity.Fields.Where(f => f.Kind == FieldKind.Money))
        {
            string type;
            byte precision, scale;
            bool nullable;
            await using (SqlCommand metadata = Command(connection, transaction,
                "SELECT TYPE_NAME(c.system_type_id),c.precision,c.scale,c.is_nullable FROM sys.columns c WHERE c.object_id=OBJECT_ID(@table) AND c.name=@column"))
            {
                metadata.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = $"dbo.{entity.Table}";
                metadata.Parameters.Add("@column", SqlDbType.NVarChar, 128).Value = field.Name;
                await using SqlDataReader reader = await metadata.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException($"The database is missing the {field.Label} column. Restore a compatible database before continuing.");
                type = reader.GetString(0); precision = reader.GetByte(1); scale = reader.GetByte(2); nullable = reader.GetBoolean(3);
            }
            if ((type is "decimal" or "numeric") && precision == 18 && scale == 2) continue;
            if (type is not ("decimal" or "numeric" or "money" or "smallmoney" or "int" or "bigint" or "smallint" or "tinyint" or "float" or "real"))
                throw new InvalidOperationException($"{entity.Title}: {field.Label} uses an unsupported database type. Ask an administrator to migrate it to decimal(18,2).");
            string column = Quote(field.Name);
            await using (SqlCommand check = Command(connection, transaction,
                $"SELECT TOP (1) 1 FROM {Table(entity)} WHERE {column} IS NOT NULL AND (TRY_CONVERT(decimal(18,2),{column}) IS NULL OR {column}<>TRY_CONVERT(decimal(18,2),{column}))"))
                if (await check.ExecuteScalarAsync(ct) is not null)
                    throw new InvalidOperationException($"{entity.Title}: {field.Label} contains values that cannot be converted without rounding or data loss. Repair the copied database before continuing.");
            await using SqlCommand alter = Command(connection, transaction,
                $"ALTER TABLE {Table(entity)} ALTER COLUMN {column} decimal(18,2) {(nullable ? "NULL" : "NOT NULL")}");
            await alter.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<bool> AccountExistsAsync(SqlConnection connection, SqlTransaction transaction, string username, CancellationToken ct)
    {
        // SQL collation can consider more Unicode spellings equivalent than the
        // in-memory ordinal comparer, so the database is the final authority.
        await using SqlCommand command = Command(connection, transaction,
            "SELECT TOP (1) 1 FROM dbo.UmsAccounts WITH (UPDLOCK,HOLDLOCK) WHERE Username=@username");
        command.Parameters.Add("@username", SqlDbType.NVarChar, 100).Value = username;
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private static InvalidOperationException DatabaseError(SqlException exception) => new(exception.Number switch
    {
        2601 or 2627 => "That username or record key already exists. Choose a different value and retry.",
        547 => "Related records or a database rule prevent this change. Check the selected department, faculty member or student.",
        8152 or 2628 => "A value exceeds this database's field size. Shorten it or ask an administrator to upgrade the existing column.",
        1205 => "Another user changed related records at the same time. Refresh the list and retry.",
        -2 => "The database took too long to respond. Check the connection and retry.",
        207 or 208 => "The database schema differs from this application's supported tables. Ask an administrator to check the copied database.",
        4922 or 5074 => "An existing database constraint or index prevents a monetary-column upgrade. Ask an administrator to migrate that column in the copied database; no upgrade was committed.",
        _ => "The database operation could not be completed. Check SQL Server / LocalDB and the database connection, then retry."
    }, exception);
}
