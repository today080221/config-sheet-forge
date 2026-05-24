## 摘要

-

## 安全

- [ ] 未提交 secret、access token、私有团队链接、owner route 或私有表内容。
- [ ] 项目特定值来自 config、环境变量或 CI secrets。
- [ ] Root discovery 只推荐候选项。

## 验证

- [ ] `dotnet build ConfigSheetForge.sln`
- [ ] `dotnet run --project tests/ConfigSheetForge.Tests`
- [ ] `pwsh scripts/Validate-UnityPackage.ps1`
- [ ] `pwsh scripts/Check-NoPrivateContent.ps1`
