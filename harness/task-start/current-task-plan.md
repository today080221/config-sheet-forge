# Task Start

## Task

- Title: Bootstrap Config Sheet Forge
- Date: 2026-05-24
- Branch: feature/repo-bootstrap

## Scope

- In: local open source repository scaffold, shared core, Lark provider abstraction, CLI, Unity UPM package, docs, examples, CI, and harness templates.
- Out: publishing to GitHub, real Feishu/Lark tenant integration, private project data migration.

## Safety Checks

- [x] No secrets, access tokens, private team links, owner routes, or private table contents will be committed.
- [x] Provider-specific values stay in local config or CI secrets.
- [x] Root discovery will recommend candidates only.

## Plan

1. Initialize git and feature branch.
2. Create .NET solution and shared core.
3. Add Lark provider and CLI commands.
4. Add Unity package and examples.
5. Add docs, harness, CI, and private-content scan.
6. Build, test, and commit.

## Validation

- [x] Build
- [x] Tests
- [x] CLI smoke
- [x] Docs/harness updated
