# Lark Provider

The Lark provider is implemented in `src/providers/lark` and is backed by `lark-cli`.

## Commands Used

- `lark-cli --version`
- `lark-cli doctor`
- `lark-cli auth status`
- `lark-cli docs +search --query <query> --format json`
- `lark-cli sheets +export ... --output <path>`
- `lark-cli sheets +read ... --format json`

Provider commands are executed with process argument arrays, not shell-joined strings.

## CLI Discovery

The provider resolves `lark-cli` in this order:

1. An explicit `larkCliPath` in local config.
2. `LARK_CLI_PATH`.
3. `PATH` plus Windows `PATHEXT`, preferring npm-safe launchers such as `lark-cli.cmd`.
4. Known Windows npm global locations such as `%APPDATA%\npm`.
5. A `node <global @larksuite/cli>/scripts/run.js` fallback when the shim is missing but the package is installed.

`doctor --details` prints the resolved source and path so environment issues are visible without exposing tokens.

## Auth Model

The provider does not store secrets. It expects `lark-cli` to manage app config and user OAuth locally.

Use user identity for user-owned docs and sheets. Use bot identity only for resources the bot can actually access.

## Wiki And Sheet Roots

A wiki URL may point to a sheet, doc, bitable, or file. Discovery shows candidates and their type when available, but a human must confirm the root.

## PowerShell Safety

Long JSON payloads are avoided in v0.1. If future commands need inline JSON, pass it through a file or a launcher that preserves argv boundaries.
