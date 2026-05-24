# Lark Provider 说明

Lark provider 是 core provider 抽象的 .NET 实现。

它通过参数数组调用 `lark-cli`，不保存 app secret、OAuth token 或租户资源。项目特定值来自 `.config-sheet-forge/config.json` 和 `.config-sheet-forge/registry.json`。

默认身份策略是 bot 优先，失败后 fallback 到 user。
