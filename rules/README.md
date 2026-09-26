# rules/ — 规则库侧车元数据

每个变体一个目录：`rules/<分类>/<变体>/`。分类只是命名空间/显示分组，**不参与互斥**；组合由能力（`provides`/`requires`）决定。代码在 `src/Wpe.Rules/`，这里是面向规则作者的元数据与测试夹具。

```
rules/<分类>/<变体>/
  variant.json        元数据：id/版本/状态/描述/requires/provides/overrides/configSchema
  config.schema.json  配置表字段说明（人读的 schema）
  tests/              per-variant 测试夹具（迷你剧本 + 期望结果）
```

`wpe rule list` 直接读取本目录的 `variant.json`。平台启动时反射扫描 `Wpe.Rules` 里的变体实现，并与本目录做 **1:1 对账**（含 `requires/provides/overrides` 集合一致）：磁盘有、代码无 → 报错；代码有、磁盘无 → 报错。目标：每个变体 battle-tested（有夹具、有契约、verify 全绿）。

## 能力模型（正交组合的关键）

- **独占能力**（至多一个提供者）：`map` `counter` `movement` `combat` `dice` `turn` `stacking` `scenario` `cards` `zoc` `territory` `supply`。
- **贡献**（函数/效果名）：按名唯一，跨规则重名报错，除非在 `overrides` 里声明。
- **终局条件**：由内核统一判定 `endConditions`，胜利规则只贡献函数，可叠加。
- `requires` 写**能力名**（不是分类名），`requiresVariants` 写具体 pin（如 `map:hexGrid`）。

分类下可多选，只要提供不同的独占能力——例如 `zoc/zoc` 与 `territory/controlPoints` 分属不同能力，可同时启用。

新增变体只需两步（**不改核心代码**）：

1. 在 `src/Wpe.Rules/<分类>/` 写一个类继承 `RuleVariantBase`（+ 需要的接口），在 `Info` 里声明 `Subsystem`(分类)/`Id`/`requires`/`provides`/`overrides`。
2. 在这里补 `rules/<分类>/<变体>/variant.json`（+ `config.schema.json` + `tests/`）。

`rules/families/<名字>.json` 是家族预设（分类 → 变体 id，值可为数组），`game.json` 的 `family` 字段选它，再在 `rules` 里逐分类覆盖（值可为对象或数组）。
