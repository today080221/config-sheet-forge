# Runtime

`Runtime/Core` 是共享 core assembly 的 canonical C# 源码。

`src/core` 下的 .NET 项目会通过链接方式编译这些文件，确保 Unity 与 CLI 不分叉。
