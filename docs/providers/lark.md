# Lark Provider 说明

Lark provider 位于 `src/providers/lark`，底层调用 `lark-cli`。

## 调用的命令

- `lark-cli doctor`
- `lark-cli --version`
- `lark-cli auth status`
- `lark-cli docs +search --query <query> --format json`
- `lark-cli sheets +export ... --file-extension xlsx --output-path <relative-path>`
- `lark-cli sheets +read ...`

provider 使用进程参数数组调用命令，不拼接 shell 字符串。

## CLI 发现顺序

1. 本地 config 中的 `larkCliPath`。
2. 环境变量 `LARK_CLI_PATH`。
3. `PATH` 和 Windows `PATHEXT`，优先 npm-safe launcher，例如 `lark-cli.cmd`。
4. Windows npm 全局目录，例如 `%APPDATA%\npm`。
5. 缺少 shim 但已安装 npm 包时，fallback 到 `node <global @larksuite/cli>/scripts/run.js`。

`doctor --details` 会显示解析来源和路径，方便排查环境问题。

## 身份策略

默认 `larkCliIdentity = bot`，且是严格模式：provider 会用 `--as bot`，但 bot 失败时不会静默 fallback 到 user。需要临时允许 user fallback 时，显式传 `--allow-user-fallback`，或在本地 config 中设置 `providerSettings.larkAllowUserFallback = true`。

bot 身份适合已通过群组或知识库授权 bridge 授权给应用的资源；用户身份适合用户自己的云文档、电子表格和知识库。

strict bot 模式下，权限错误会提示补应用/bot 的 scope 或资源授权，避免 CI 和本地手动同步读到不同身份的数据。

## JSON 解析

v0.2.0 支持多种 `lark-cli` 输出形态：

- 直接二维数组。
- `data.values`、`values`、`items`、`records` 等 wrapper。
- 对象数组会转成字段名行加数据行。
- stdout/stderr 混合输出时，会优先提取可解析 JSON。

警告、notice、revision、raw provider 内容会保留在 details 中，面向人的主错误只说明怎么修。

## PowerShell 安全

复杂 JSON 不要通过 PowerShell 直接传 `--params`。需要内联 JSON 时，优先使用文件、shortcut 参数，或已验证能保留引号的 launcher。
