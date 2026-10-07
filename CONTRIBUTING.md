# Contributing

Thank you for contributing to University Management System. Discuss substantial behavior or architecture changes with the maintainer before implementation.

## Development workflow

1. Fork and clone the repository after it is published, then create a focused feature or fix branch.
2. Follow the setup instructions in [README.md](README.md). Use demo mode or a disposable SQL database for development.
3. Keep active application changes under `src/`, schema changes under `database/` and meaningful regression checks under `tests/`.
4. Build in Release configuration and run the regression executable. Report any environment restriction that prevents a check.
5. Submit a pull request describing the problem, resulting behavior, validation and any compatibility or migration impact.

Preserve data-loss protection, validation, accessible labels and role checks. Avoid unrelated refactors and new dependencies without a concrete need. Database migrations must account for existing records and failure recovery.

Do not commit personal records, passwords, connection secrets, MDF/LDF files, backups, IDE settings, package caches or generated artifacts. Report potential security issues privately to the maintainer before posting sensitive technical details publicly.

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
