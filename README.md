# WPE — Wargame Pipeline Engine

全数据驱动的通用兵棋引擎：**填表 → 校验 → 直接跑**。战斗表、移动表、地图、算子、剧本全部是 JSON；`game.json` 用"家族预设 + 变体选择"编排规则，无需写一行游戏代码。

## 快速开始

```bash
# 1. 构建
dotnet build Wpe.sln

# 2. 校验一个游戏包（报错带位置，中台读懂每张表）
dotnet run --project src/Wpe.Cli -- verify games/hanzhong

# 3. 无头自动跑一局（回归冒烟）
dotnet run --project src/Wpe.Cli -- play   games/hanzhong

# 4. 无头自动跑一局（回归冒烟）
dotnet run --project src/Wpe.Cli -- play   games/hanzhong

# 5. 批量生产白板算子（正面+受损面+总表 PNG）
dotnet run --project src/Wpe.Cli -- counters games/hanzhong

# 6. 渲染地图（地形/河流/胜利点/算子 → PNG，快速核对与演示）
dotnet run --project src/Wpe.Cli -- overlay games/xuzhou

# 7. 按六角格路径快速生成河流边（自动保证连通，verify 拦截坏路径）
dotnet run --project src/Wpe.Cli -- river games/xuzhou "4,3" "4,4" "5,4" ... --fords 0,9

# 8. 查看规则库
dotnet run --project src/Wpe.Cli -- rule list

# 9. 新建一个游戏包（生成可填写的模板）
dotnet run --project src/Wpe.Cli -- new mygame

# 10. 图形界面（双人热座）
dotnet run --project src/Wpe.App -- games/hanzhong     # 不带参数自动弹出游戏选择器
```

## 图形界面（Wpe.App）

SkiaSharp 渲染 + 双人热座：点选算子 → 绿色=可达格 / 红色=可攻击目标 → 移动/攻击 → 悔棋/重置/结束阶段。行动完全按 `move.kind` 驱动（movement/combat/recover/pass），不认行动 id，因此任何表驱动游戏包都能跑。算子用白板渲染（`wpe counters` 同一渲染器）。

## 一个游戏包 = 一堆表

```
games/<名字>/
  game.json      主规则：身份 + 家族预设(rules 变体选择) + 阶段/行动/触发/胜利
  map.json       地图：网格/地形字母/河流/胜利点        ← map/hexGrid 变体读
  movement.json  移动表：地形移动费 + 防御修正 + 河流费   ← movement/movePoints 变体读
  combat.json    战斗表：战力比行 × 骰子列 → 结果码       ← combat/crTable 变体读
  units.json     算子目录：键 → 基础属性                 ← counter/generic 变体读
  scenario.json  剧本：选算子/势力/部署/援军             ← scenario/generic 变体读
```

## 中台（引擎）怎么读懂这些表

1. `game.json` 声明用哪些**规则变体**（`family: "default"` 自动装配 8 个常用子系统）。
2. 中台实例化变体 → 注册表达式函数/效果原语/算法接缝。
3. 校验：表结构、表达式语法与函数、变体依赖组合、剧本边界——全部在**加载时**拦下。
4. 固定管线跑规则：`校验 → 掷骰 → 结算(CRT) → 效果 → 触发 → 胜利`。

## 概念速览

- **子系统 × 变体**：`map`、`movement`、`combat`、`dice`、`turn`、`victory`、`counter`、`scenario`。变体 = 一个成熟算法实现 + JSON 配置，长期目标是每个子系统积累十几变体。
- **表达式语言**：`counter.owner == me`、`dist(counter,pos) == 1`、`moveCost(counter,pos) <= counter.moveLeft`、`effStr(counter)/effStr(target)`——小众规则就写在这里。
- **效果原语**：`move/flip/remove/retreat/setattr/setside/setvar/addvar/log/endphase/pass`。
- **逃生门**：`game.json` 的 `hookType` 可指向一个 C# `IHook` 实现，兜底 JSON 表达不了的规则。

详见 `docs/`。入门读 `docs/属性与数据契约.md` 和 `docs/规则库.md`。
