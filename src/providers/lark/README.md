# Lark Provider

The Lark provider is a .NET implementation of the core provider abstraction.

It shells out to `lark-cli` with argument arrays. It does not store app secrets, OAuth tokens, or tenant-specific resources.

Provider-specific values come from `.config-sheet-forge/config.json` and `.config-sheet-forge/registry.json`.
