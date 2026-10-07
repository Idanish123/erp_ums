# University ERP Management System

A modular university management application with an Enterprise Resource Planning (ERP) focus on academic administration and financial record management. Built with C#, .NET 10 and native Windows Forms, it brings student records, faculty records, course catalogs, academic departments, campuses, fee payments and salary payments into a shared SQL Server database.

The current ERP scope covers seven record-management modules, role-based access control (RBAC), relational data validation and transactional database initialization. It supports create, read, update and delete (CRUD) operations. Admissions processing, attendance, examinations, timetabling, general-ledger accounting and payroll calculation are outside the current implementation; fee and salary modules record payments.

**Project maintainer:** Danish M. Khalid · [GitHub](https://github.com/Idanish123) · [LinkedIn](https://www.linkedin.com/in/danish-m-khalid/)

> Status: actively developed. Automated regression checks cover the in-memory store and native editor controls. Local SQL schema initialization and lossless monetary migration have been verified; account migration, end-to-end SQL application workflows and interactive accessibility testing remain outstanding. See the [quality assessment](MODERNIZATION_SCORECARD.md) for evidence and limitations.

## Contents

- [Features](#features)
- [Technology and architecture](#technology-and-architecture)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Database configuration](#database-configuration)
- [Accounts and permissions](#accounts-and-permissions)
- [Validation and testing](#validation-and-testing)
- [Build and deployment](#build-and-deployment)
- [Repository structure](#repository-structure)
- [Contributing](#contributing)
- [License](#license)
- [Contact](#contact)

## Features

### ERP modules

| Module | Implemented responsibilities |
| --- | --- |
| Student administration | Student profiles, department affiliation and semester records |
| Faculty administration | Faculty profiles, qualifications, experience and salary defaults |
| Course management | Course catalog, credit hours and faculty assignments |
| Department management | Academic groups, intake values and fee defaults |
| Campus management | Campus profiles, director fields and descriptive rank labels |
| Fee management | Student-linked fee-payment records with amounts and payment dates |
| Salary management | Faculty-linked salary-payment records with amounts and payment dates |

### Shared capabilities

- Academic record management for students, faculty, courses, departments and campuses.
- Separate fee-payment and salary-payment workspaces for administrators.
- Searchable record lists, explicit add/edit dialogs and confirmation before deletion.
- Required-field validation, typed dates and amounts, relationship checks and stale-record detection.
- Account creation policies, role checks and salted password hashing.
- In-memory demonstration mode with fictitious records and no SQL Server dependency.
- Consistent native controls, accessible labels, keyboard shortcuts, high-contrast fallback and per-monitor DPI configuration.

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+F` | Focus record search |
| `Ctrl+N` | Create a record in the current workspace |
| `Ctrl+S` | Edit the selected record |
| `F5` | Refresh the current workspace |
| `Enter` in an editor | Submit the record |
| `Escape` in an editor | Cancel, with an unsaved-change prompt when needed |

Reset affects editor fields only. It does not delete stored records.

## Technology and architecture

| Component | Implementation |
| --- | --- |
| Language | C# with nullable reference types |
| Runtime and desktop framework | .NET 10 LTS, Windows Forms (`net10.0-windows`) |
| SDK selection | `global.json`: SDK 10.0.401 with compatible 10.0 feature-band roll-forward |
| SQL client | Microsoft.Data.SqlClient 7.1.1 |
| Persistence | SQL Server or SQL Server LocalDB |
| Financial data types | Fixed-point `decimal(18,2)` monetary columns |
| Access control | Administrator and Registrar application roles (RBAC) |
| Demo persistence | In-memory tables, reset on exit |
| Credentials | PBKDF2-SHA256, 600,000 iterations, individual salts and fixed-time comparison |
| Build configuration | SDK-style projects, dependency lock file and separated build artifacts |

Entity metadata defines table identities, editable fields and readable relationship labels. `RecordValidation` validates values independently of the user interface. `IUniversityStore` separates record operations from presentation; SQL and demo implementations share that contract. Native forms handle navigation, account workflows and reusable record editors. SQL values are parameterized, and identifiers come from the supported entity catalog.

The application uses direct desktop-to-database access. Database permissions remain part of the deployment's trust model; application roles do not replace SQL Server authorization.

## Requirements

- Windows with the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) for development.
- Internet access for the initial NuGet restore.
- SQL Server LocalDB (`MSSQLLocalDB`) or a managed SQL Server database for persistent mode.
- The .NET 10 Desktop Runtime for a framework-dependent deployment.

Demo mode requires no database installation. The CLI commands below use PowerShell from the repository root. An IDE is optional; use one that supports the configured .NET SDK.

## Quick start

Restore the locked application dependencies and build:

```powershell
dotnet restore 'University Management System 2.csproj' --locked-mode
dotnet build 'University Management System 2.csproj' --configuration Release --no-restore
```

Launch the demonstration workspace:

```powershell
dotnet run --project 'University Management System 2.csproj' --configuration Release --no-build -- --demo
```

Demo mode signs in as a demonstration administrator. Records are fictitious, changes disappear on exit, and account creation is hidden. It is suitable for exploring the interface without attaching a database.

## Database configuration

For a persistent demonstration dataset across all seven record sections, see the [KIET Karachi sample data guide](database/samples/README.md). It provides 197 clearly marked fictional records, published academic context, source links and transactional installation instructions. Back up the selected database before applying it.

### Managed SQL Server database

For a fresh checkout, configure an empty SQL Server database using the `UMS_CONNECTION_STRING` process environment variable. Supply the connection string through your organization's approved configuration mechanism; do not commit credentials or database files.

The connection must select the intended database and an identity permitted to initialize its schema. Use appropriate transport encryption outside local development. Launch persistent mode without the demo argument:

```powershell
dotnet run --project 'University Management System 2.csproj' --configuration Release --no-build
```

Initialization creates missing tables from `database/Schema.sql`. Application records then persist in the configured database.

### Existing LocalDB files

For SQL development in Cursor or VS Code, install the [Microsoft SQL Server extension](https://marketplace.visualstudio.com/items?itemName=ms-mssql.mssql). The workspace recommends `ms-mssql.mssql`. The extension provides query and object-explorer tools; the SQL Server/LocalDB runtime must be installed separately. Use server `(LocalDB)\MSSQLLocalDB` with Windows authentication for the local workspace. Machine-specific connection profiles belong in ignored `.vscode/settings.json` and should not contain passwords.

Existing local `UniversityDataBase.mdf` and `UniversityDataBase_log.ldf` files are private data, excluded by `.gitignore`. A build includes them as an optional seed only when both are present. They are not required to compile, run demo mode or use an explicitly configured SQL Server database.

Without `UMS_CONNECTION_STRING`, persistent mode requires those optional seed files and LocalDB. First startup copies the pair to `%LOCALAPPDATA%\UniversityManagement\data` and attaches the working copy. It never overwrites an existing working MDF or attaches the repository original. An orphaned working log requires operator recovery.

### Migration behavior

Back up an existing database before first use. Initialization changes the selected database: missing tables are created, supported money columns are upgraded transactionally to `decimal(18,2)`, and account import runs when an administrator has been established. Conversions that would round or lose values are refused. Existing constraints and shorter string-column limits remain and may require an operator-managed migration.

Existing record identities are retained. Campus rank remains descriptive text. Legacy numbers and dates outside supported input ranges are displayed without silent clipping and must be corrected explicitly before saving.

Do not publish original databases or historical backups: they may contain personal records and legacy plaintext credentials. New deployments should use a fresh configured database and omit sensitive seed files from distribution.

## Accounts and permissions

Create the first administrator through the initial sign-in workflow. New passphrases must contain 12-256 characters. There is no distributed default password. Subsequent account creation requires an authenticated administrator.

| Role | Academic records | Fee and salary payment tables | Account creation |
| --- | --- | --- | --- |
| Administrator | Read and maintain | Read and maintain | Allowed |
| Registrar | Read and maintain | Denied by navigation and store | Denied |

Faculty monthly-salary defaults and department fee defaults remain academic fields visible to Registrars. The current policy restricts payment tables; it does not implement field-level compensation confidentiality.

Legacy accounts import as Registrars. Duplicate or unsupported usernames receive mapped names in `UmsLegacyAccountMap`. Passwords are hashed before legacy password fields in the working database are replaced with a migration marker. Bootstrap and import share a transaction; empty or unsupported legacy passwords stop the import and require correction in the working copy before retrying.

## Validation and testing

Build and run the regression executable:

```powershell
dotnet restore tests\Ums.Tests.csproj
dotnet build tests\Ums.Tests.csproj --configuration Release --no-restore
dotnet run --project tests\Ums.Tests.csproj --configuration Release --no-build
```

Checks cover the seven CRUD workflows, selected and stale record identities, relationship integrity, bounded values, decimal money, role restrictions, account policies, password verification, native editor metadata and legacy-value preservation. They use in-memory records and do not modify a live SQL database.

Optional native UI captures:

```powershell
dotnet run --project tests\Ums.Tests.csproj --configuration Release --no-build -- --render .review\modern-ui
```

The harness creates transparent windows outside the desktop to capture child controls. It writes five local PNG files and is intended for developer inspection; captures are ignored by Git. It does not verify screen-reader behavior or replace a user study.

Recorded verification: 40 checks passed before the last cosmetic refinements; the final implementation compiles without warnings or errors. Windows Application Control subsequently blocked execution of the test assembly on the development workstation. Have the system administrator review such a block rather than disabling the policy. SQL Server 2025 LocalDB schema initialization and monetary migration were subsequently verified using Microsoft's SQL command-line tools with unchanged academic/payment row counts. Account migration, end-to-end SQL application workflows, concurrent users, keyboard-only workflows, Narrator and multiple DPI settings still require validation. The [scorecard](MODERNIZATION_SCORECARD.md) explains the assessment scope.

The persistent KIET sample dataset was installed after a checksum-verified backup: 197 rows were added, relationship and payment-total checks passed, and a second execution added no duplicates. These checks validate the seed operation; they do not certify the application against an international standard.

## Build and deployment

Create a framework-dependent release:

```powershell
dotnet publish 'University Management System 2.csproj' --configuration Release --no-restore --output artifacts\publish
```

The target Windows machine needs the .NET 10 Desktop Runtime and the configured database prerequisite. Configure the database connection for the target user or process. Inspect the output before distribution and exclude private seed files if they were present during publishing. Installer packaging, code signing and clean-machine deployment testing are not currently provided.

Generated outputs belong under `artifacts/`; NuGet packages restore under `.packages/`. Both directories are ignored and may be regenerated. Build the source to obtain the current executable.

## Repository structure

```text
src/                         Active application source
  Model.cs                   Entity metadata and store contract
  RecordValidation.cs        Shared value validation
  SqlUniversityStore.cs      SQL persistence and migrations
  DemoUniversityStore.cs     Fictitious in-memory workspace
  PasswordHasher.cs          Password hashing and verification
  ShellForm.cs               Main navigation and record lists
  RecordEditorDialog.cs      Reusable entity editor
  SignInForm.cs              Sign-in and initial setup
  AccountDialog.cs           Administrator account creation
  UiTheme.cs                 Native presentation tokens
  Program.cs                 Startup and database location
database/Schema.sql          Database initialization resource
database/samples/            KIET-context sample SQL and dataset documentation
tests/                       Regression executable
Directory.Build.props        Shared deterministic output configuration
global.json                  SDK selection
NuGet.Config                 Official package source and local cache
packages.lock.json           Application dependency lock
MODERNIZATION_SCORECARD.md    Quality evidence and remaining validation
```

The solution file contains the application and regression project. Source files, dependency lock files, SQL scripts and shared editor recommendations belong in version control. `.gitignore` excludes generated builds, package caches, test output, local IDE state, environment files, signing credentials and private SQL database/backup files. Ignore rules apply to untracked files; they do not remove files already committed to a repository.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) and follow the [Code of Conduct](CODE_OF_CONDUCT.md). Keep changes focused, explain the resulting behavior and include relevant validation. Do not submit credentials, personal records, database backups or generated build files.

## License

A project license has not yet been selected. A `LICENSE` file should be added before presenting this repository as a licensed open-source distribution. Dependency licenses remain those of their respective publishers.

## Contact

**Danish M. Khalid**

- GitHub: [@Idanish123](https://github.com/Idanish123)
- LinkedIn: [Danish M. Khalid](https://www.linkedin.com/in/danish-m-khalid/)
