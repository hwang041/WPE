# rules/ — 规则库侧车元数据

每个变体一个目录：`rules/<子系统>/<变体>/`。代码在 `src/Wpe.Rules/`，这里是面向规则作者的元数据与测试夹具。

```
rules/<子系统>/<变体>/
  variant.json        元数据：id/版本/状态/描述/requires/provides/configSchema
  config.schema.json  配置表字段说明（人读的 schema）
  tests/              per-variant 测试夹具（迷你剧本 + 期望结果）
```

`wpe rule list` 列出的是代码目录里的变体；本目录与之一一对应，供文档/工具/CI 消费。目标：每个变体 battle-tested（有夹具、有契约、verify 全绿）。

新增变体请同时：1) 在 `src/Wpe.Rules` 写实现并注册到 `DefaultFamilies`；2) 在这里补 `variant.json` + `config.schema.json` + `tests/`。
