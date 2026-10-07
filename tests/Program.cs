using System.Data;
using System.Drawing.Imaging;
using System.Globalization;
using University_Management_System_2;

internal static class Program
{
    private static readonly UserSession Admin = new("demo", UserRole.Administrator);
    private static readonly UserSession Registrar = new("registrar", UserRole.Registrar);
    private const string Passphrase = "University-tests-42!";

    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        ApplicationConfiguration.Initialize();
        try
        {
            int result = RunAsync().GetAwaiter().GetResult();
            if (result == 0 && args.Length == 2 && args[0] == "--render")
                RenderAsync(args[1]).GetAwaiter().GetResult();
            else if (args.Length != 0 && !(args.Length == 2 && args[0] == "--render"))
                throw new ArgumentException("Usage: Ums.Tests [--render <directory inside the project>]");
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL runner: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync()
    {
        List<(string Name, Func<Task> Run)> tests = [];
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        void AsyncTest(string name, Func<Task> run) => tests.Add((name, run));

        Test("catalog: supported tables and primary keys remain complete", () =>
        {
            Dictionary<string, (string Table, string Id)> expected = new()
            {
                ["students"] = ("StudentTbl", "StId"), ["faculty"] = ("FacultyTbl", "F_Id"),
                ["courses"] = ("CourseTbl", "Course_Id"), ["departments"] = ("DepartmentTbl", "DeptId"),
                ["campuses"] = ("CampusTbl", "Camp_Id"), ["fees"] = ("FeesTbl", "Fee_Id"),
                ["salaries"] = ("SalaryTbl", "Salary_Id")
            };
            Equal(expected.Count, Catalog.Entities.Length);
            Equal(Catalog.Entities.Length, Catalog.Entities.Select(e => e.Key).Distinct().Count());
            Equal(Catalog.Entities.Length, Catalog.Entities.Select(e => e.Table).Distinct().Count());
            foreach (EntitySpec entity in Catalog.Entities)
            {
                Equal(expected[entity.Key].Table, entity.Table);
                Equal(expected[entity.Key].Id, entity.IdColumn);
                string[] columns = [entity.IdColumn, .. entity.Fields.Select(f => f.Name), .. entity.Derived.Select(f => f.Name)];
                Equal(columns.Length, columns.Distinct(StringComparer.Ordinal).Count());
            }
        });
        Test("catalog: derived columns resolve existing lookup fields", () =>
        {
            foreach (EntitySpec entity in Catalog.Entities)
            foreach (DerivedField derived in entity.Derived)
            {
                FieldSpec foreignKey = entity.Fields.Single(f => f.Name == derived.ForeignKey);
                Equal(FieldKind.Lookup, foreignKey.Kind);
                Equal(derived.LookupKey, foreignKey.LookupKey);
                EntitySpec target = Catalog.Get(derived.LookupKey);
                True(target.Fields.Any(f => f.Name == derived.SourceColumn) || target.Derived.Any(f => f.Name == derived.SourceColumn));
            }
        });
        Test("catalog: displayed columns have distinct readable labels", () =>
        {
            foreach (EntitySpec entity in Catalog.Entities)
            {
                string[] columns = [entity.IdColumn,
                    .. entity.Fields.Where(f => !entity.Derived.Any(d => d.ForeignKey == f.Name)).Select(f => f.Name),
                    .. entity.Derived.Select(d => d.Name)];
                string[] labels = columns.Select(c => Catalog.ColumnLabel(entity, c)).ToArray();
                Equal(labels.Length, labels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                True(labels.All(label => !string.IsNullOrWhiteSpace(label) && !label.Contains('_')));
            }
        });
        AsyncTest("demo: all seven record types have seeded, typed schemas", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            True(store.IsDemo);
            foreach (EntitySpec entity in Catalog.Entities)
            {
                DataTable table = await store.LoadAsync(Admin, entity);
                True(table.Rows.Count > 0, $"No demo records for {entity.Key}.");
                Equal(typeof(int), table.Columns[entity.IdColumn]!.DataType);
                foreach (FieldSpec field in entity.Fields)
                {
                    Type expected = field.Kind switch
                    {
                        FieldKind.Number or FieldKind.Lookup => typeof(int),
                        FieldKind.Money => typeof(decimal), FieldKind.Date => typeof(DateTime), _ => typeof(string)
                    };
                    Equal(expected, table.Columns[field.Name]!.DataType);
                }
                foreach (DerivedField field in entity.Derived)
                    Equal(typeof(string), table.Columns[field.Name]!.DataType);
                Equal(table.Rows.Count, table.Rows.Cast<DataRow>().Select(r => r.Field<int>(entity.IdColumn)).Distinct().Count());
            }
        });

        foreach (string key in Catalog.Entities.Select(e => e.Key))
        {
            string testKey = key;
            AsyncTest($"{key}: add, update and delete target only the selected record", async () =>
            {
                DemoUniversityStore store = await DemoAsync();
                EntitySpec entity = Catalog.Get(testKey);
                DataTable before = await store.LoadAsync(Admin, entity);
                Dictionary<string, object?> original = RowValues(entity, before.Rows[0]);
                int originalId = before.Rows[0].Field<int>(entity.IdColumn);
                Dictionary<string, object?> added = await ValidValuesAsync(store, entity);
                int newId = await store.SaveAsync(Admin, entity, null, added);
                True(newId > 0 && newId != originalId);
                DataTable created = await store.LoadAsync(Admin, entity);
                Equal(before.Rows.Count + 1, created.Rows.Count);
                CompareValues(entity, original, Find(created, entity, originalId));
                Dictionary<string, object?> changed = new(added);
                FieldSpec? name = entity.Fields.FirstOrDefault(f => f.Kind == FieldKind.Text);
                if (name is not null) changed[name.Name] = "Updated regression record";
                else changed[entity.Fields.Single(f => f.Kind == FieldKind.Money).Name] = 234.56m;
                Equal(newId, await store.SaveAsync(Admin, entity, newId, changed));
                DataTable updated = await store.LoadAsync(Admin, entity);
                Equal(created.Rows.Count, updated.Rows.Count);
                CompareValues(entity, RecordValidation.Normalize(entity, changed), Find(updated, entity, newId));
                CompareValues(entity, original, Find(updated, entity, originalId));
                await store.DeleteAsync(Admin, entity, newId);
                DataTable deleted = await store.LoadAsync(Admin, entity);
                Equal(before.Rows.Count, deleted.Rows.Count);
                True(!deleted.Rows.Cast<DataRow>().Any(r => r.Field<int>(entity.IdColumn) == newId));
            });
        }
        AsyncTest("records: stale update and delete fail without altering data", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("campuses");
            Dictionary<string, object?> values = await ValidValuesAsync(store, entity);
            int id = await store.SaveAsync(Admin, entity, null, values);
            await store.DeleteAsync(Admin, entity, id);
            int count = (await store.LoadAsync(Admin, entity)).Rows.Count;
            await ThrowsAsync(() => store.SaveAsync(Admin, entity, id, values));
            await ThrowsAsync(() => store.DeleteAsync(Admin, entity, id));
            Equal(count, (await store.LoadAsync(Admin, entity)).Rows.Count);
        });
        foreach ((string Parent, string Child, string ForeignKey) relationship in new[]
        {
            ("departments", "students", "DeptId"), ("faculty", "courses", "Fac_Id"),
            ("students", "fees", "StId"), ("faculty", "salaries", "F_Id")
        })
        {
            var relation = relationship;
            AsyncTest($"relationships: {relation.Parent} deletion is blocked by {relation.Child}", async () =>
            {
                DemoUniversityStore store = await DemoAsync();
                EntitySpec parent = Catalog.Get(relation.Parent);
                DataTable child = await store.LoadAsync(Admin, Catalog.Get(relation.Child));
                int id = child.Rows[0].Field<int>(relation.ForeignKey);
                int count = (await store.LoadAsync(Admin, parent)).Rows.Count;
                await ThrowsAsync(() => store.DeleteAsync(Admin, parent, id));
                Equal(count, (await store.LoadAsync(Admin, parent)).Rows.Count);
            });
        }
        AsyncTest("relationships: missing lookup cannot be saved", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("students");
            Dictionary<string, object?> values = await ValidValuesAsync(store, entity);
            values["DeptId"] = int.MaxValue;
            int count = (await store.LoadAsync(Admin, entity)).Rows.Count;
            await ThrowsAsync(() => store.SaveAsync(Admin, entity, null, values));
            Equal(count, (await store.LoadAsync(Admin, entity)).Rows.Count);
        });
        AsyncTest("records: SQL-like text stays harmless record data", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("departments");
            Dictionary<string, object?> values = await ValidValuesAsync(store, entity);
            const string payload = "Computing'; DROP TABLE StudentTbl; --";
            values["Name"] = payload;
            int students = (await store.LoadAsync(Admin, Catalog.Get("students"))).Rows.Count;
            int id = await store.SaveAsync(Admin, entity, null, values);
            Equal(payload, Find(await store.LoadAsync(Admin, entity), entity, id)["Name"]);
            Equal(students, (await store.LoadAsync(Admin, Catalog.Get("students"))).Rows.Count);
        });
        AsyncTest("records: derived names follow current parent values", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec departments = Catalog.Get("departments");
            DataTable table = await store.LoadAsync(Admin, departments);
            int id = table.Rows[0].Field<int>(departments.IdColumn);
            Dictionary<string, object?> values = RowValues(departments, table.Rows[0]);
            values["Name"] = "Renamed computing department";
            await store.SaveAsync(Admin, departments, id, values);
            DataTable students = await store.LoadAsync(Admin, Catalog.Get("students"));
            DataRow[] related = students.Rows.Cast<DataRow>().Where(r => r.Field<int>("DeptId") == id).ToArray();
            True(related.Length > 0);
            True(related.All(r => r.Field<string>("Department") == (string)values["Name"]!));
        });
        AsyncTest("relationships: courses require faculty in the same department", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("courses");
            Dictionary<string, object?> values = await ValidValuesAsync(store, entity);
            DataTable departments = await store.LoadAsync(Admin, Catalog.Get("departments"));
            values["DeptId"] = departments.Rows[1].Field<int>("DeptId");
            int count = (await store.LoadAsync(Admin, entity)).Rows.Count;
            await ThrowsAsync(() => store.SaveAsync(Admin, entity, null, values));
            Equal(count, (await store.LoadAsync(Admin, entity)).Rows.Count);
        });
        AsyncTest("relationships: faculty department changes protect assigned courses", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("faculty");
            DataTable faculty = await store.LoadAsync(Admin, entity);
            Dictionary<string, object?> values = RowValues(entity, faculty.Rows[0]);
            int id = faculty.Rows[0].Field<int>(entity.IdColumn);
            object originalDepartment = values["DeptId"]!;
            DataTable departments = await store.LoadAsync(Admin, Catalog.Get("departments"));
            values["DeptId"] = departments.Rows[1].Field<int>("DeptId");
            await ThrowsAsync(() => store.SaveAsync(Admin, entity, id, values));
            Equal(originalDepartment, Find(await store.LoadAsync(Admin, entity), entity, id)["DeptId"]);
        });
        AsyncTest("records: modifying a loaded snapshot cannot bypass save validation", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("departments");
            DataTable table = await store.LoadAsync(Admin, entity);
            int id = table.Rows[0].Field<int>(entity.IdColumn);
            object original = table.Rows[0]["Name"];
            table.Rows[0]["Name"] = "Unsaved modification";
            Equal(original, Find(await store.LoadAsync(Admin, entity), entity, id)["Name"]);
        });

        Test("validation: required and oversized text is rejected", () =>
        {
            EntitySpec entity = Catalog.Get("departments");
            Dictionary<string, object?> values = DepartmentValues();
            values.Remove("Name");
            Throws(() => RecordValidation.Normalize(entity, values));
            values["Name"] = "  ";
            Throws(() => RecordValidation.Normalize(entity, values));
            values["Name"] = new string('x', entity.Fields.Single(f => f.Name == "Name").MaxLength + 1);
            Throws(() => RecordValidation.Normalize(entity, values));
        });
        Test("validation: text is trimmed and embedded controls rejected", () =>
        {
            EntitySpec entity = Catalog.Get("departments");
            Dictionary<string, object?> values = DepartmentValues();
            values["Name"] = "  Computing  ";
            Equal("Computing", RecordValidation.Normalize(entity, values)["Name"]);
            values["Name"] = "Computing\0hidden";
            Throws(() => RecordValidation.Normalize(entity, values));
        });
        Test("validation: monetary values stay decimal with two places", () =>
        {
            EntitySpec entity = Catalog.Get("fees");
            Dictionary<string, object?> values = FeeValues();
            values["Amount"] = "123.45";
            object? normalized = RecordValidation.Normalize(entity, values)["Amount"];
            True(normalized is decimal);
            Equal(123.45m, normalized);
            foreach (object bad in new object[] { 0m, -1m, 1.001m, 1_000_000_001m, "invalid" })
            {
                values["Amount"] = bad;
                Throws(() => RecordValidation.Normalize(entity, values));
            }
        });
        Test("validation: whole-number fields enforce integral bounds", () =>
        {
            EntitySpec entity = Catalog.Get("departments");
            Dictionary<string, object?> values = DepartmentValues();
            foreach (object bad in new object[] { 0, -1, 100_001, 1.5m, "two" })
            {
                values["Intake"] = bad;
                Throws(() => RecordValidation.Normalize(entity, values));
            }
            values["Intake"] = "200";
            Equal(200, RecordValidation.Normalize(entity, values)["Intake"]);
        });
        Test("validation: selectors, choices and historical dates validate", () =>
        {
            EntitySpec entity = Catalog.Get("fees");
            Dictionary<string, object?> values = FeeValues();
            Dictionary<string, object?> normalized = RecordValidation.Normalize(entity, values);
            Equal(1, normalized["StId"]);
            Equal("1", normalized["Semester"]);
            Equal(new DateTime(2025, 1, 15), normalized["PayDate"]);
            foreach ((string key, object bad) in new (string, object)[]
            {
                ("StId", 0), ("StId", -1), ("StId", 1.5m), ("Semester", "9"),
                ("PayDate", DateTime.Today.AddDays(1)), ("PayDate", new DateTime(1899, 12, 31)), ("PayDate", "not a date")
            })
            {
                Dictionary<string, object?> invalid = new(values) { [key] = bad };
                Throws(() => RecordValidation.Normalize(entity, invalid));
            }
        });
        Test("validation: forged table descriptions cannot reach stores", () =>
        {
            EntitySpec fake = Catalog.Get("departments") with { Table = "InjectedTable" };
            Throws(() => RecordValidation.Normalize(fake, DepartmentValues()));
        });

        foreach (string key in new[] { "fees", "salaries" })
        {
            string testKey = key;
            AsyncTest($"permissions: registrar direct {key} reads, saves and deletes fail", async () =>
            {
                DemoUniversityStore store = await DemoAsync();
                EntitySpec entity = Catalog.Get(testKey);
                Dictionary<string, object?> values = await ValidValuesAsync(store, entity);
                DataTable table = await store.LoadAsync(Admin, entity);
                int count = table.Rows.Count;
                int id = table.Rows[0].Field<int>(entity.IdColumn);
                True(!Registrar.CanAccess(entity));
                await ThrowsAsync(() => store.LoadAsync(Registrar, entity));
                await ThrowsAsync(() => store.SaveAsync(Registrar, entity, null, values));
                await ThrowsAsync(() => store.SaveAsync(Registrar, entity, id, values));
                await ThrowsAsync(() => store.DeleteAsync(Registrar, entity, id));
                Equal(count, (await store.LoadAsync(Admin, entity)).Rows.Count);
            });
        }
        AsyncTest("permissions: registrar academic records remain usable", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("campuses");
            True(Registrar.CanAccess(entity));
            int count = (await store.LoadAsync(Registrar, entity)).Rows.Count;
            int id = await store.SaveAsync(Registrar, entity, null, await ValidValuesAsync(store, entity));
            Equal(count + 1, (await store.LoadAsync(Registrar, entity)).Rows.Count);
            await store.DeleteAsync(Registrar, entity, id);
        });
        AsyncTest("accounts: initial setup requires administrator and a strong passphrase", async () =>
        {
            DemoUniversityStore store = await DemoAsync(false);
            True(!await store.HasAccountsAsync());
            await ThrowsAsync(() => store.CreateAccountAsync(null, "registrar", Passphrase, UserRole.Registrar));
            await ThrowsAsync(() => store.CreateAccountAsync(null, "admin", "short", UserRole.Administrator));
            True(!await store.HasAccountsAsync());
            await store.CreateAccountAsync(null, "first.admin", Passphrase, UserRole.Administrator);
            True(await store.HasAccountsAsync());
            UserSession? signedIn = await store.SignInAsync("FIRST.ADMIN", Passphrase);
            True(signedIn is not null);
            Equal(UserRole.Administrator, signedIn!.Role);
            True(await store.SignInAsync("first.admin", "incorrect-passphrase") is null);
            True(await store.SignInAsync("unknown", Passphrase) is null);
        });
        AsyncTest("accounts: subsequent users require an authenticated administrator", async () =>
        {
            DemoUniversityStore store = await DemoAsync(false);
            await store.CreateAccountAsync(null, "first.admin", Passphrase, UserRole.Administrator);
            await ThrowsAsync(() => store.CreateAccountAsync(null, "second.admin", Passphrase, UserRole.Administrator));
            await ThrowsAsync(() => store.CreateAccountAsync(Registrar, "new.user", Passphrase, UserRole.Registrar));
            await ThrowsAsync(() => store.CreateAccountAsync(Admin, "forged.admin", Passphrase, UserRole.Administrator));
            UserSession admin = (await store.SignInAsync("first.admin", Passphrase))!;
            await store.CreateAccountAsync(admin, "new.registrar", Passphrase, UserRole.Registrar);
            UserSession registrar = (await store.SignInAsync("new.registrar", Passphrase))!;
            Equal(UserRole.Registrar, registrar.Role);
            await ThrowsAsync(() => store.CreateAccountAsync(registrar, "another.user", Passphrase, UserRole.Registrar));
            await ThrowsAsync(() => store.CreateAccountAsync(admin, "FIRST.ADMIN", Passphrase, UserRole.Registrar));
        });
        Test("passwords: correct and incorrect passwords verify safely", () =>
        {
            string hash = PasswordHasher.Hash(Passphrase);
            True(!hash.Contains(Passphrase, StringComparison.Ordinal));
            True(PasswordHasher.Verify(Passphrase, hash));
            True(!PasswordHasher.Verify("incorrect-passphrase", hash));
        });
        Test("passwords: independent hashes have distinct salts", () =>
        {
            string first = PasswordHasher.Hash(Passphrase);
            string second = PasswordHasher.Hash(Passphrase);
            True(first != second);
            True(first.Split('$')[2] != second.Split('$')[2]);
            True(PasswordHasher.Verify(Passphrase, first) && PasswordHasher.Verify(Passphrase, second));
        });
        Test("passwords: malformed hashes cannot authenticate or throw", () =>
        {
            foreach (string? malformed in new string?[]
            {
                null, "", "plaintext", "pbkdf2-sha256$600000$not-base64$not-base64",
                "pbkdf2-sha256$1$AAAA$AAAA", "pbkdf2-sha256$2147483647$AAAA$AAAA", new string('x', 201)
            }) True(!PasswordHasher.Verify(Passphrase, malformed));
        });
        AsyncTest("UI: editors expose labels, typed inputs and keyboard dialog actions", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            foreach (EntitySpec entity in Catalog.Entities)
            {
                using RecordEditorDialog editor = new(store, Admin, entity);
                await editor.PrepareAsync();
                True(editor.AcceptButton is Button { Enabled: true });
                True(editor.CancelButton is not null);
                foreach (FieldSpec field in entity.Fields)
                {
                    Control input = editor.Controls.Find(field.Name, true).Single();
                    Equal(field.Label, input.AccessibleName);
                    True(!string.IsNullOrWhiteSpace(input.AccessibleDescription));
                    True(input.TabStop);
                    switch (field.Kind)
                    {
                        case FieldKind.Choice:
                        case FieldKind.Lookup:
                            ComboBox combo = (ComboBox)input;
                            Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle);
                            Equal(-1, combo.SelectedIndex);
                            True(combo.Items.Count > 0);
                            break;
                        case FieldKind.Number:
                        case FieldKind.Money:
                            Equal(field.Kind == FieldKind.Money ? 2 : 0, ((NumericUpDown)input).DecimalPlaces);
                            break;
                        case FieldKind.Date:
                            DateTimePicker date = (DateTimePicker)input;
                            True(date.MaxDate.Date <= DateTime.Today);
                            if (field.Name == "DOB") True(date.ShowCheckBox && !date.Checked);
                            break;
                        default:
                            Equal(field.MaxLength, ((TextBox)input).MaxLength);
                            break;
                    }
                }
            }
        });
        AsyncTest("UI: salary editing keeps amount and faculty in their own fields", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("salaries");
            DataRow row = (await store.LoadAsync(Admin, entity)).Rows[0];
            using RecordEditorDialog editor = new(store, Admin, entity, row);
            await editor.PrepareAsync();
            Equal(row.Field<decimal>("Salary"), ((NumericUpDown)editor.Controls.Find("Salary", true).Single()).Value);
            ComboBox faculty = (ComboBox)editor.Controls.Find("F_Id", true).Single();
            True(faculty.SelectedIndex >= 0);
            True(faculty.SelectedItem!.ToString()!.Contains(row.Field<string>("F_Name")!, StringComparison.Ordinal));
        });

        AsyncTest("UI: legacy numeric and date values are preserved when opening an editor", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("faculty");
            DataRow row = (await store.LoadAsync(Admin, entity)).Rows[0];
            row["Experience"] = 95;
            row["DOB"] = new DateTime(1890, 2, 3);
            using RecordEditorDialog editor = new(store, Admin, entity, row);
            await editor.PrepareAsync();
            Equal(95m, ((NumericUpDown)editor.Controls.Find("Experience", true).Single()).Value);
            Equal(new DateTime(1890, 2, 3), ((DateTimePicker)editor.Controls.Find("DOB", true).Single()).Value.Date);
            Throws(() => RecordValidation.Normalize(entity, RowValues(entity, row)));
        });
        AsyncTest("compatibility: campus rank preserves descriptive legacy text", async () =>
        {
            DemoUniversityStore store = await DemoAsync();
            EntitySpec entity = Catalog.Get("campuses");
            Dictionary<string, object?> values = await ValidValuesAsync(store, entity);
            values["Rank"] = "Category A";
            int id = await store.SaveAsync(Admin, entity, null, values);
            DataRow row = Find(await store.LoadAsync(Admin, entity), entity, id);
            using RecordEditorDialog editor = new(store, Admin, entity, row);
            await editor.PrepareAsync();
            Equal("Category A", editor.Controls.Find("Rank", true).Single().Text);
        });

        int passed = 0;
        foreach ((string name, Func<Task> run) in tests)
        {
            try { await run(); Console.WriteLine($"PASS {name}"); passed++; }
            catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Console.WriteLine($"{passed}/{tests.Count} regression checks passed.");
        Console.WriteLine("Scope: safe in-memory service and validation checks; no live SQL Server or stored user records touched.");
        return passed == tests.Count ? 0 : 1;
    }

    private static async Task<DemoUniversityStore> DemoAsync(bool seed = true)
    {
        DemoUniversityStore store = new(seed);
        await store.InitializeAsync();
        return store;
    }

    private static Dictionary<string, object?> DepartmentValues() => new()
    { ["Name"] = "Regression computing", ["Intake"] = 200, ["Fees"] = 12345.67m };
    private static Dictionary<string, object?> FeeValues() => new()
    { ["StId"] = 1, ["Amount"] = 123.45m, ["Semester"] = "1", ["PayDate"] = new DateTime(2025, 1, 15) };

    private static async Task<Dictionary<string, object?>> ValidValuesAsync(IUniversityStore store, EntitySpec entity)
    {
        Dictionary<string, object?> values = [];
        foreach (FieldSpec field in entity.Fields)
        {
            object value;
            if (field.Kind == FieldKind.Lookup)
            {
                EntitySpec target = Catalog.Get(field.LookupKey!);
                DataTable lookup = await store.LoadAsync(Admin, target);
                value = lookup.Rows[0].Field<int>(target.IdColumn);
            }
            else value = field.Kind switch
            {
                FieldKind.Number => (int)Math.Max(field.Min, 1), FieldKind.Money => 123.45m,
                FieldKind.Date => new DateTime(2025, 1, 15), FieldKind.Choice => field.Options![0],
                _ => "Regression record"
            };
            values[field.Name] = value;
        }
        return values;
    }

    private static Dictionary<string, object?> RowValues(EntitySpec entity, DataRow row) =>
        entity.Fields.ToDictionary(f => f.Name, f => row[f.Name] is DBNull ? null : row[f.Name]);
    private static DataRow Find(DataTable table, EntitySpec entity, int id) =>
        table.Rows.Cast<DataRow>().Single(r => r.Field<int>(entity.IdColumn) == id);
    private static void CompareValues(EntitySpec entity, IReadOnlyDictionary<string, object?> expected, DataRow row)
    {
        foreach (FieldSpec field in entity.Fields) Equal(expected[field.Name], row[field.Name]);
    }
    private static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected operation to be rejected.");
    }
    private static async Task ThrowsAsync(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected operation to be rejected.");
    }

    private static async Task RenderAsync(string requestedDirectory)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "University Management System 2.csproj")))
            directory = directory.Parent;
        string root = directory?.FullName ?? throw new InvalidOperationException("Cannot find project workspace.");
        string destination = Path.GetFullPath(requestedDirectory, root);
        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        True(destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase), "Render directory must be inside the project workspace.");
        Directory.CreateDirectory(destination);
        DemoUniversityStore store = await DemoAsync();
        using ShellForm form = new(store, Admin);
        form.ClientSize = new Size(1200, 820);
        PrepareRender(form);
        foreach (string key in new[] { "overview", "students" })
        {
            await form.ShowPageAsync(key);
            form.PerformLayout();
            CreateControls(form);
            using Bitmap bitmap = new(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            string path = Path.Combine(destination, $"{key}.png");
            bitmap.Save(path, ImageFormat.Png);
            Console.WriteLine($"Rendered {path}");
        }
        DataRow salary = (await store.LoadAsync(Admin, Catalog.Get("salaries"))).Rows[0];
        foreach ((string Name, string Key, DataRow? Row) specimen in new (string, string, DataRow?)[]
        {
            ("student-editor", "students", null), ("salary-add", "salaries", null), ("salary-edit", "salaries", salary)
        })
        {
            using RecordEditorDialog editor = new(store, Admin, Catalog.Get(specimen.Key), specimen.Row);
            await editor.PrepareAsync();
            PrepareRender(editor);
            CreateControls(editor);
            using Bitmap bitmap = new(editor.Width, editor.Height);
            editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            string path = Path.Combine(destination, $"{specimen.Name}.png");
            bitmap.Save(path, ImageFormat.Png);
            Console.WriteLine($"Rendered {path}");
        }
        Console.WriteLine("Native off-screen rendering only; interactive keyboard and screen-reader checks remain untested.");
    }
    private static void CreateControls(Control control)
    {
        control.CreateControl();
        foreach (Control child in control.Controls) CreateControls(child);
        control.PerformLayout();
    }

    private static void PrepareRender(Form form)
    {
        // WinForms does not create visible child handles under an unshown form.
        // Create the window outside the desktop at zero opacity for bitmap capture.
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.Opacity = 0;
        form.Show();
        Application.DoEvents();
        CreateControls(form);
    }
}
