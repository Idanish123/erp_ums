# Modernization and quality scorecard

Assessment date: 7 October 2026. Scope: the entire University Management System, comparing the original .NET Framework 4.7.2 application with the active `src/` implementation. This is an evidence-based engineering assessment, not a certification, measured user-study result, or evaluation of every theoretical computer-science discipline.

**Overall: 24/100 before; 63/100 after.** The score covers ten equally weighted applied software-quality dimensions. Scores are deliberately conservative because account migration, end-to-end SQL workflows, real users, screen readers and load testing have not been validated.

## Rubric and scores

Each dimension is scored out of 5: 0 absent; 1 serious known failures; 2 partial implementation; 3 sound implementation with focused checks; 4 broad automated and realistic runtime evidence; 5 strong independent validation under realistic conditions. Half points express mixed evidence. Overall score = sum / 50 x 100. The baseline is retrospective judgment from the original source audit; neither score is a benchmark.

| Dimension | Before /5 | After /5 | Evidence and remaining limits |
| --- | ---: | ---: | --- |
| Functional correctness | 2 | 3 | All seven entity types share named-field CRUD; selected IDs, stale operations and salary-field mapping have regression checks. Live SQL behavior remains untested. |
| Data integrity and database design | 1 | 3 | Typed values, decimal money, relationship checks, transactional SQL writes, preserved originals, current joined display names and legacy-value preservation. Migration has code review but no live database execution. |
| Security and privacy | 1 | 3 | Parameterized SQL values, allowlisted identifiers, salted password hashes, administrator account creation and payment-table authorization. Original seed remains sensitive; no rate limiting, field-level confidentiality or penetration test. |
| Reliability and recovery | 1 | 3 | Scoped connections, async operations, cancellation, actionable errors, stale-row detection, confirmation and transactional bootstrap. Network failures and concurrent SQL users need testing; no automatic backup or audit trail. |
| HCI and task design | 1.5 | 3.5 | Consistent navigation, readable hierarchy, visible status, contextual selection actions, explicit forms, shortcuts and error prevention. Native previews and control checks exist; task success/time with actual staff is unmeasured. |
| Accessibility | 1 | 3 | Native labeled controls, tab navigation, mnemonics, ErrorProvider text, high-contrast fallback and improved palette contrast. Narrator, keyboard-only end-to-end use and multiple DPI sizes are untested. |
| Architecture and maintainability | 1.5 | 3.5 | Catalog/model, validation, password helper, store interface, separate SQL/demo stores and reusable editors replace copied handlers. Obsolete source has been removed; metadata/UI coupling and limited dependency injection remain. |
| Performance and scalability | 2 | 2.5 | Async SQL keeps waits off UI handlers, bounded connection timeout and scoped resources. Full tables load into memory; no paging, benchmarks or large-record/concurrency results. |
| Verification and test quality | 0.5 | 3.5 | Release build has zero warnings/errors; 40 focused regression checks pass, including CRUD, validation, permissions, credentials and native controls. SQL integration, fault injection and interactive UI automation remain absent. |
| Reproducibility and deployment | 0.5 | 3.5 | SDK configuration, official NuGet source, dependency lock, separated artifacts and documented demo/database/test/publish commands. No installed-package, clean-machine or hosted CI run verified. |
| **Total** | **12/50** | **31.5/50** | **24/100 -> 63/100** |

## HCI principles applied

The interface adapts IBM/Carbon's restrained typography, spacing, contrast and task hierarchy to native WinForms controls. These are design choices, not a claim of official Carbon conformance. The design-reference-library skill and its IBM reference informed this work.

[Nielsen's ten usability heuristics](https://www.nngroup.com/articles/ten-usability-heuristics/) provide the assessment framework:

| Principle | Implemented behavior | Evidence / next check |
| --- | --- | --- |
| Visibility of system status | Page counts, selected-record actions, busy states, save feedback and persistent demo/database indicator | Check long SQL operations and uncertain commit outcomes interactively. |
| Match with the real world | Students, faculty, departments and payment terminology; readable lookup names; rupee labels | Validate terminology with actual administrative staff. |
| User control and freedom | Cancel, unsaved-change prompt, editor-only Reset and explicit sign-out | Keyboard walkthrough remains required. There is no post-delete undo. |
| Consistency and standards | Shared navigation, buttons, forms and validation across seven entities | Metadata/control regression checks cover all editors. |
| Error prevention | Required selections, typed dates/money, explicit selected IDs, blocked dependent deletes and confirmation defaulting to No | Store regression checks cover malformed values and relationships. |
| Recognition rather than recall | Visible field labels and parent names instead of memorized foreign-key IDs | Test lookup discoverability with larger lists. |
| Flexibility and efficiency | Ctrl+F, Ctrl+N, Ctrl+S and F5; list filtering; Enter/Escape dialog actions | Shortcut implementation exists; real task timing is unmeasured. |
| Aesthetic and minimalist design | Neutral canvas, strong hierarchy, one primary editor action and limited accent colors | Native previews support a layout review, not a usability study. |
| Recognize and recover from errors | Inline field errors, first-invalid focus, readable database messages and values retained after a failed save | SQL disconnect and deadlock paths need fault-injection checks. |
| Help and documentation | Accessible descriptions, tooltips, empty-state guidance, setup and migration documentation | No searchable in-application help or guided onboarding yet. |

Standard sRGB contrast calculations for the default palette: ink `#161616` on white approximately 18.10:1; muted `#525252` on white 7.81:1; white on primary `#0F62FE` 5.00:1; white on danger `#DA1E28` 5.00:1. These exceed the [WCAG normal-text contrast threshold](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) for those combinations. Contrast alone does not establish WCAG conformance; native state colors, system themes and assistive technologies still need testing.

## Framework and implementation changes

- .NET Framework 4.7.2 -> SDK-style `net10.0-windows`, WinForms and per-monitor DPI configuration. [.NET 10 is the current stable LTS line](https://dotnet.microsoft.com/en-us/download/dotnet); .NET 11 prereleases were excluded.
- Legacy Guna/Bunifu binary dependencies -> native WinForms controls in active source.
- System.Data.SqlClient -> [Microsoft.Data.SqlClient 7.1.1](https://www.nuget.org/packages/Microsoft.Data.SqlClient/7.1.1), with explicit parameter types and scoped connections.
- Copied synchronous form SQL -> shared validated stores with asynchronous SQL and transactional mutations.
- Plaintext account authentication -> versioned PBKDF2-SHA256 with 600,000 iterations, individual salts and fixed-time comparison. This iteration choice follows [OWASP's PBKDF2 guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html); it is not a claim of FIPS certification.
- Hardcoded personal database path -> per-user working copy or explicit operator configuration; original MDF/LDF preserved.

## Verification record

Commands executed on Windows using the configured .NET SDK:

```powershell
dotnet build tests\Ums.Tests.csproj --no-restore --configuration Release --verbosity minimal
dotnet run --project tests\Ums.Tests.csproj --configuration Release --no-build -- --render .review\modern-ui
```

Build: zero warnings and errors. Regression result: **40/40 passed before the final cosmetic refinements**. Coverage includes all seven CRUD paths, stale IDs, relationship blockers, department/faculty consistency, required and bounded values, two-place decimal money, forged catalog rejection, Registrar payment-table denial, first-administrator policy, account creation permissions, password checks, accessible native editor metadata, salary mapping and preservation of legacy out-of-range values and descriptive ranks.

The final refinements make navigation widths consistent, expand grid header minimum widths and neutralize disabled emphasized buttons. These compile successfully, including a clean `--no-incremental` rebuild. Subsequent runtime checks were blocked by Windows Application Control (`0x800711C7`) on the test assembly, even with approved execution. No security policy was changed. The final cosmetic changes therefore have compile evidence only; the earlier five previews and passing regression run cover the preceding implementation. Execution of the final build needs an environment where the operator's Application Control policy permits it.

The SQL-like text check exercises the in-memory store only; it is not an executed SQL-injection test. SQL parameterization and application-driven migration behavior are source-review evidence. The 40-check regression run did not connect to a live SQL instance or change stored records; the subsequent local database setup is recorded separately below.

Five native previews are generated under `.review/modern-ui`: `overview.png`, `students.png`, `student-editor.png`, `salary-add.png` and `salary-edit.png`. The harness creates transparent windows outside the desktop to obtain native child handles. Preview images are local ignored artifacts, not deployment assets.

## Subsequent local database setup: 7 October 2026

Installed Microsoft SQL Server extension `ms-mssql.mssql` 1.46.0 in Cursor, SQL Server 2025 LocalDB 17.0.4025.3 and Microsoft Sqlcmd Tools 1.10.0. The LocalDB installer was checked against Microsoft's stable installer catalog SHA-256 and Authenticode signature. A private workspace connection profile uses Windows authentication; its machine-specific settings are ignored by Git.

Backed up the working MDF/LDF before attachment. Microsoft's SQL command-line tool attached the working copy, upgraded its engine file format, applied `database/Schema.sql` and transactionally converted the four monetary columns with checks against rounding or data loss. Record counts before and after matched: students 2, faculty 2, courses 2, departments 2, campuses 2, fee payments 3 and salary payments 2. All four monetary columns were verified as `decimal(18,2)`. The repository originals were not attached or modified.

This establishes local schema and money-migration evidence on the bundled dataset. The first administrator must still be created by the user in the sign-in workflow. The application-driven initializer could not be executed in the temporary compiled checker because of Windows Application Control; a PowerShell script was also blocked by execution policy. Setup completed through Microsoft's installed SQL tool without changing either policy. Account import, full application SQL CRUD, concurrency and rollback fault injection remain unverified. The numeric score remains 63/100 pending that broader evidence.

## Highest-value next validation

1. Restore a protected disposable database backup into SQL Server/LocalDB; test schema/account import, duplicate names, constrained money columns, legacy shorter strings and rollback behavior. Verify migrated accounts and row counts.
2. Test concurrent edits, lost connections and uncertain commit outcomes. Introduce conflict detection or audit history if multiple staff share a database.
3. Run keyboard-only and Narrator workflows at 100%, 150% and 200% DPI and in Windows high contrast; inspect every field error and focus transition.
4. Observe representative staff adding a student and recording a payment; measure completion, errors and satisfaction before revising the HCI score.
5. Test thousands of rows and realistic lookup sizes; add server-side paging/search if necessary. Validate a clean-machine installation and distribution without sensitive seed records.
