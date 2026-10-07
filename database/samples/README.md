# KIET Karachi sample dataset

`KIET.SampleData.sql` adds persistent demonstration records to an initialized University Management System database. It uses KIET Karachi's published academic context, reviewed on 7 October 2026.

**These are fictional records, not official KIET student, employee or financial records.** Names carry `[Sample]`, phone fields contain non-dialable `DEMO-ST-...` identifiers, and addresses are placeholders. Intake, fees, salaries, qualifications, experience and all dates are illustrative. Campus coordinator names and rank labels are placeholders; campus dates do not represent inauguration dates.

| Section | Records added |
| --- | ---: |
| Academic groups, represented as departments | 2 |
| Campuses | 3 |
| Faculty | 12 |
| Students | 40 |
| Courses | 24 |
| Fee payments | 80 |
| Salary payments | 36 |
| **Total** | **197** |

The two academic groups represent Computing & Information Sciences and Management Sciences. The campuses represent Ahsanabad, the City Campus at Shahrah-e-Faisal and the City Campus at North Nazimabad. The application has no separate degree-program entity; this dataset does not model every KIET degree, course, enrollment or campus affiliation.

Selected BSCS and BBA course titles and integer credit hours follow the published curricula. Computing laboratory courses and fractional-credit activities are not included. Each student has two illustrative semester-fee installments dated August and September 2026. Each faculty member has three illustrative salary payments dated July through September 2026. All amounts use the application's local PKR context and are **not KIET's published rates**.

## Sources

- [KIET overview and campuses](https://kiet.edu.pk/about/)
- [KIET academic programs](https://kiet.edu.pk/programs/)
- [BS Computer Science curriculum](https://cocis.kiet.edu.pk/bs-computer-science/)
- [BBA curriculum](https://coms.kiet.edu.pk/bba-business-administrator/)

## Installation

1. Select the intended database and initialize its schema using the application.
2. Take a SQL Server backup and verify it before applying the seed. Avoid copying live MDF/LDF files.
3. Execute the SQL file using SQL Server tooling with an identity authorized to insert records. For example, with Microsoft `sqlcmd` and Windows authentication:

```powershell
sqlcmd -S '<server-or-LocalDB-named-pipe>' -E -d '<database-name>' -b -i 'database\samples\KIET.SampleData.sql'
```

Configure transport encryption for the selected environment. LocalDB database names may be the full attached MDF path; use the actual database name rather than assuming a short name. Run from the repository root.

The script inserts records in one serializable transaction, checks row-count deltas, course/faculty relationships and payment totals, and rolls back on errors. It captures generated identities rather than assuming existing IDs. Existing rows and authentication accounts are not updated or deleted.

The `KIETSampleData.v1` entry in `UmsSchemaMetadata` prevents duplicate installation, including duplicate payments. A second execution makes no changes. Deleting individual sample records does not reset that marker; do not remove it merely to replenish deleted records, since that would duplicate the remaining dataset.

After installation, press `F5` in each application workspace to refresh persistent records. In-memory `--demo` mode uses its own data and does not display this SQL dataset.
