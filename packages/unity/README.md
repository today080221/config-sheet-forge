# Config Sheet Forge Unity Package

This package adds a Unity Editor window for running Config Sheet Forge commands from a project that uses config sheets as a source of truth.

Open it from `Tools > Config Sheet Forge`.

The Editor code references `ConfigSheetForge.Core`, the same semantic workbook core compiled by the CLI. Provider work remains outside Unity and is delegated to the installed `config-sheet-forge` command.
