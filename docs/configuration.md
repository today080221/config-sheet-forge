# 配置说明

Config Sheet Forge 有两个本地文件：

- `.config-sheet-forge/config.json`
- `.config-sheet-forge/registry.json`

它们由 `config-sheet-forge init` 生成，并被 git 忽略。

## config.json

```json
{
  "schemaVersion": "1",
  "provider": "lark",
  "rootUrl": "",
  "rootToken": "",
  "rootObjectType": "",
  "registryPath": ".config-sheet-forge/registry.json",
  "cacheDirectory": ".config-sheet-forge/cache",
  "providerSettings": {
    "larkCliPath": "lark-cli",
    "larkCliIdentity": "bot"
  }
}
```

`rootUrl` 适合保存人能核对的 URL。`rootToken` 只在 provider 无法通过 URL 工作时使用。真实租户资源不要提交。

`larkCliIdentity` 支持：

- `bot`：默认值，先用机器人身份，失败后 provider 会 fallback 到 user。
- `user`：直接使用当前用户 OAuth 身份。
- `default`：不向 `lark-cli` 传 `--as`。

## registry.json

```json
{
  "schemaVersion": "1",
  "tables": [
    {
      "id": "items",
      "name": "Items",
      "provider": "lark",
      "spreadsheet": "",
      "sheetId": "",
      "range": "A1:Z500",
      "fieldRow": 0,
      "typeRow": 1,
      "descriptionRow": 2,
      "dataStartRow": 3,
      "treatUnknownTypesAsEnum": false,
      "localSourcePath": ""
    }
  ]
}
```

`fieldRow`、`typeRow`、`descriptionRow`、`dataStartRow` 都是从 0 开始的行号。`typeRow = -1` 表示没有显式类型行，此时会保守自动推断。

## lark-cli 路径

默认使用环境中的 `lark-cli`。如果安装在非标准位置，可设置：

- `providerSettings.larkCliPath`
- `LARK_CLI_PATH`

Windows 下会显式解析 `lark-cli.cmd` 等 npm shim，避免依赖 shell 行为。

## secret

不要把 app secret、access token、refresh token、真实业务表 URL 写入这些文件。Lark provider 只依赖 `lark-cli` 的本地认证状态。
