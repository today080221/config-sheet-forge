# Core

`src/core/ConfigSheetForge.Core` 通过链接方式编译 `packages/unity/Runtime/Core` 中的 canonical source。

Core 负责：

- 语义工作簿模型。
- 类型行优先的矩阵导入。
- 便携子集 validation。
- 语义 hash。
- 三方合并。
- Schema review。
- Provider contract。

Core 不能依赖 provider SDK，也不能包含项目特定配置。
