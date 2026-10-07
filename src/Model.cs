using System.Data;

namespace University_Management_System_2;

public enum UserRole { Administrator, Registrar }
public sealed record UserSession(string Username, UserRole Role)
{
    public bool CanAccess(EntitySpec entity) => Role == UserRole.Administrator || !entity.Finance;
}
public enum FieldKind { Text, Multiline, Number, Money, Date, Choice, Lookup }
public sealed record FieldSpec(string Name, string Label, FieldKind Kind = FieldKind.Text,
    int MaxLength = 100, decimal Min = 0, decimal Max = 1_000_000_000,
    string[]? Options = null, string? LookupKey = null);
public sealed record DerivedField(string Name, string ForeignKey, string LookupKey, string SourceColumn);
public sealed record EntitySpec(string Key, string Title, string Singular, string Table, string IdColumn,
    FieldSpec[] Fields, DerivedField[] Derived, bool Finance = false)
{
    public string DisplayColumn => Fields.First(f => f.Kind is FieldKind.Text or FieldKind.Lookup).Name;
}

public static class Catalog
{
    public static readonly EntitySpec[] Entities =
    [
        new("students", "Students", "student", "StudentTbl", "StId",
            [new("Name", "Full name"), new("DOB", "Date of birth", FieldKind.Date),
             new("Gender", "Gender", FieldKind.Choice, Options: ["Male", "Female", "Other"]),
             new("Address", "Address", FieldKind.Multiline, MaxLength: 200),
             new("DeptId", "Department", FieldKind.Lookup, LookupKey: "departments"),
             new("Phone", "Phone number", MaxLength: 30),
             new("Semester", "Semester", FieldKind.Choice, Options: ["1", "2", "3", "4", "5", "6", "7", "8"])],
            [new("Department", "DeptId", "departments", "Name")]),
        new("faculty", "Faculty", "faculty member", "FacultyTbl", "F_Id",
            [new("Name", "Full name"), new("DOB", "Date of birth", FieldKind.Date),
             new("Gender", "Gender", FieldKind.Choice, Options: ["Male", "Female", "Other"]),
             new("Address", "Address", FieldKind.Multiline, MaxLength: 200),
             new("Qualification", "Qualification", MaxLength: 60),
             new("Experience", "Years of experience", FieldKind.Number, Max: 80),
             new("DeptId", "Department", FieldKind.Lookup, LookupKey: "departments"),
             new("Salary", "Monthly salary (Rs.)", FieldKind.Money, Min: 0)],
            [new("Department", "DeptId", "departments", "Name")]),
        new("courses", "Courses", "course", "CourseTbl", "Course_Id",
            [new("Course_Name", "Course name"),
             new("Credit_Hrs", "Credit hours", FieldKind.Number, Min: 1, Max: 30),
             new("DeptId", "Department", FieldKind.Lookup, LookupKey: "departments"),
             new("Fac_Id", "Assigned faculty", FieldKind.Lookup, LookupKey: "faculty")],
            [new("Department", "DeptId", "departments", "Name"), new("Fac_Name", "Fac_Id", "faculty", "Name")]),
        new("departments", "Departments", "department", "DepartmentTbl", "DeptId",
            [new("Name", "Department name"), new("Intake", "Student intake", FieldKind.Number, Min: 1, Max: 100_000),
             new("Fees", "Semester fee (Rs.)", FieldKind.Money)], []),
        new("campuses", "Campuses", "campus", "CampusTbl", "Camp_Id",
            [new("Name", "Campus name"), new("City", "City", MaxLength: 80),
             new("Director", "Director"), new("Rank", "Rank", MaxLength: 50),
             new("Join_Date", "Joining date", FieldKind.Date)], []),
        new("fees", "Fee payments", "fee payment", "FeesTbl", "Fee_Id",
            [new("StId", "Student", FieldKind.Lookup, LookupKey: "students"),
             new("Amount", "Amount (Rs.)", FieldKind.Money, Min: .01m),
             new("Semester", "Semester", FieldKind.Choice, Options: ["1", "2", "3", "4", "5", "6", "7", "8"]),
             new("PayDate", "Payment date", FieldKind.Date)],
            [new("StName", "StId", "students", "Name"), new("StDept", "StId", "students", "Department")], true),
        new("salaries", "Salary payments", "salary payment", "SalaryTbl", "Salary_Id",
            [new("F_Id", "Faculty member", FieldKind.Lookup, LookupKey: "faculty"),
             new("Salary", "Amount (Rs.)", FieldKind.Money, Min: .01m),
             new("PayDate", "Payment date", FieldKind.Date)],
            [new("F_Name", "F_Id", "faculty", "Name"), new("Department", "F_Id", "faculty", "Department")], true)
    ];
    public static EntitySpec Get(string key) => Entities.Single(e => e.Key == key);
    public static string ColumnLabel(EntitySpec entity, string column) => column == entity.IdColumn ? "Record ID"
        : entity.Fields.FirstOrDefault(f => f.Name == column)?.Label
          ?? column switch { "Department" or "StDept" => "Department", "StName" => "Student", "F_Name" or "Fac_Name" => "Faculty", _ => column };
}

public interface IUniversityStore
{
    bool IsDemo { get; }
    Task InitializeAsync(CancellationToken ct = default);
    Task<bool> HasAccountsAsync(CancellationToken ct = default);
    Task<UserSession?> SignInAsync(string username, string password, CancellationToken ct = default);
    Task CreateAccountAsync(UserSession? actor, string username, string password, UserRole role, CancellationToken ct = default);
    Task<DataTable> LoadAsync(UserSession actor, EntitySpec entity, CancellationToken ct = default);
    Task<int> SaveAsync(UserSession actor, EntitySpec entity, int? id, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);
    Task DeleteAsync(UserSession actor, EntitySpec entity, int id, CancellationToken ct = default);
}
