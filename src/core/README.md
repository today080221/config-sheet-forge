# Core

`src/core/ConfigSheetForge.Core` compiles the canonical source files from `packages/unity/Runtime/Core`.

The core owns:

- semantic workbook model
- portable subset validation
- semantic hashing
- three-way merge
- schema review
- provider contracts

It must not depend on provider SDKs or project-specific configuration.
