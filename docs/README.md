# Documentation index

These English manuals describe application version 0.18. Source XML comments explain individual members; algorithm comments explain meaningful intermediate variables and decisions. The UI still supports English and Spanish.

## For users

1. [User guide](USER_GUIDE.md): start, format, export and print.
2. [Troubleshooting](TROUBLESHOOTING.md): common failures and practical checks.
3. [Configuration](CONFIGURATION.md): presets, defaults and data locations.
4. [History and Dashboard](HISTORY.md): reports, recycle bin, CSV and backups.

## For software-development students

1. [Student guide](STUDENT_GUIDE.md): concepts, suggested reading order and worked data flow.
2. [Architecture](ARCHITECTURE.md): components and their responsibilities.
3. Open [Program.cs](../src/TagPrinter/Program.cs), then follow the source order in the student guide.
4. Read member XML comments for inputs, outputs, resource ownership and failure conditions. Use comments beside complex blocks for units and reasoning.
5. [Validation](VALIDATION.md): learn how the existing self-tests exercise real behavior without printing.

## For maintainers

- [Configuration contracts](CONFIGURATION.md) and [history schema/counting rules](HISTORY.md).
- [Current status](STATUS.md), [release history](CHANGELOG.md) and [roadmap](ROADMAP.md).
- Keep manuals current rather than appending contradictory version sections. Record historical differences in CHANGELOG.
- When changing a method, update its comments and relevant examples, especially units, stored defaults, side effects, counts and ownership.
- Run scripts/Test.ps1 and scripts/Publish.ps1 as required by the repository instructions. Never test printing against a real destination automatically.

- [Startup validation](STARTUP_VALIDATION.md): launcher architecture, file integrity, mutable-data checks and recovery.

- [Full configuration transfer and code size](CONFIGURATION_TRANSFER.md): complete settings, backup/rollback, local-resource constraints and symbol geometry.

- [Centered text, empty recycle bin and Windows printer status](TEXT_TRASH_PRINTER_STATUS.md).
