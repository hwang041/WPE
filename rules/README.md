# rules/ — 规则库侧车元数据

每个变体一个目录：`rules/<子系统>/<变体>/`。代码在 `src/Wpe.Rules/`，这里是面向规则作者的元数据与测试夹具。

```
rules/<子系统>/<变体>/
  variant.json        元数据：id/版本/状态/描述/requires/provides/configSchema
  config.schema.json  配置表字段说明（人读的 schema）
  tests/              per-variant 测试夹具（迷你剧本 + 期望结果）
```

`wpe rule list` 直接读取本目录的 `variant.json`。平台启动时反射扫描 `Wpe.Rules` 里的变体实现，并与本目录做 **1:1 对账**：磁盘有、代码无 → 报错；代码有、磁盘无 → 报错。目标：每个变体 battle-tested（有夹具、有契约、verify 全绿）。

新增变体只需两步（**不改核心代码**）：
1. 在 `src/Wpe.Rules/<子系统>/` 写一个类继承 `RuleVariantBase`（+ 需要的接缝接口），在 `Info` 里声明 `Subsystem`/`Id`/`requires`/`provides`。
2. 在这里补 `rules/<子系统>/<变体>/variant.json`（+ `config.schema.json` + `tests/`）。

`rules/families/<名字>.json` 是家族预设（子系统 → 变体 id），`game.json` 的 `family` 字段选它，再在 `rules` 里逐项覆盖。
