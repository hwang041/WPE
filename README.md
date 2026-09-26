# WPE — Wargame Pipeline Engine

全数据驱动的通用兵棋引擎：**填表 → 校验 → 直接跑**。战斗表、移动表、地图、算子、剧本全部是 JSON；`game.json` 用"家族预设 + 变体选择"编排规则，无需写一行游戏代码。

> **声明**：本项目纯属个人业余兴趣爱好，全程 vibe coding，有空就做一点。它不是一个成熟产品或生产级项目——接口、数据格式、目录结构都可能随时变动；欢迎参考，但请自行承担使用风险。

## 快速开始

```bash
# 1. 构建
dotnet build Wpe.sln

# 2. 校验一个游戏包（报错带位置，中台读懂每张表）
dotnet run --project src/Wpe.Cli -- verify games/hanzhong

# 3. 无头自动跑一局（回归冒烟）
dotnet run --project src/Wpe.Cli -- play   games/hanzhong

# 4. 打印牌库/手牌（卡驱游戏）
dotnet run --project src/Wpe.Cli -- cards  games/sunce-jiangdong

# 5. 脚本化自检卡牌系统（行动牌/事件牌结算）
dotnet run --project src/Wpe.Cli -- demo   games/sunce-jiangdong

# 6. 批量生产白板算子（正面+受损面+总表 PNG）
dotnet run --project src/Wpe.Cli -- counters games/hanzhong

# 7. 渲染地图（六角格或点对点 → PNG，快速核对与演示）
dotnet run --project src/Wpe.Cli -- overlay games/xuzhou
dotnet run --project src/Wpe.Cli -- overlay games/sunce-jiangdong

# 8. 按六角格路径快速生成河流边（自动保证连通，verify 拦截坏路径）
dotnet run --project src/Wpe.Cli -- river games/xuzhou "4,3" "4,4" "5,4" ... --fords 0,9

# 9. 查看规则库
dotnet run --project src/Wpe.Cli -- rule list

# 10. 新建一个游戏包（生成可填写的模板）
dotnet run --project src/Wpe.Cli -- new mygame

# 11. 图形界面（双人热座）；无头钩子：--shot out.png / --uitest out.txt
dotnet run --project src/Wpe.App -- games/hanzhong     # 不带参数自动弹出游戏选择器
```

## 图形界面（Wpe.App）

SkiaSharp 渲染 + 双人热座：点选算子 → 绿色=可达格 / 红色=可攻击目标 → 移动/攻击 → 悔棋/重置/结束阶段。行动完全按 `move.kind` 驱动（movement/combat/recover/pass），不认行动 id，因此任何表驱动游戏包都能跑。算子用白板渲染（`wpe counters` 同一渲染器）。

## 一个游戏包 = 一堆表

```
games/<名字>/
  game.json      主规则：身份 + 家族预设(rules 变体选择) + 阶段/行动/触发/胜利
                 + factions(势力配色) / nodeTypes(点对点节点样式) / damage / seed
  map.json       地图：网格/地形字母/河流/胜利点        ← map/hexGrid 变体读
  movement.json  移动表：地形移动费 + 防御修正 + 河流费   ← movement/movePoints 变体读
  combat.json    战斗表：战力比行 × 骰子列 → 结果码       ← combat/crTable 变体读
  units.json     算子目录：键 → 基础属性                 ← counter/generic 变体读
  scenario.json  剧本：选算子/势力/部署/援军             ← scenario/generic 变体读
  spacemap.json  点对点地图：城镇节点/道路/河流          ← map/pointToPoint 变体读
  cards.json     卡牌：牌定义 + 牌库 + handLimit         ← cards/standard 变体读
```

## 可配置的规则（正交能力 × 变体）

所有玩法都是"选一组规则 + 填表"，组合即游戏。规则按**能力**组合：独占能力（map/combat/zoc/territory…）最多选一个，函数/效果贡献可叠加，分类（目录名）只作分组、不互斥。因此 `zoc`（控制区）与 `territory`（归属）可同用，`objectives` + `attrition` 也可同用。渲染层不含任何游戏专有代码。

| 分类 | 变体 | 配置文件 | 提供的能力 |
|---|---|---|---|
| map | `hexGrid` | `map.json` | 六角格地形/河流/胜利点 |
| map | `pointToPoint` | `spacemap.json` | 城镇节点 + 道路；节点城防/势力 |
| movement | `movePoints` | `movement.json` | 行动点移动：地形费 + 河流费，BFS 可达 |
| movement | `roadNetwork` | — | 沿道路逐段走，禁入敌占节点 |
| movement | `zoc` | `movement.json` | 行动点移动 + 进入敌控制区必停/离开加费 |
| combat | `crTable` | `combat.json` | 战力比行 × 骰子列 → 结果码 |
| combat | `oddsShift` | `combat.json` | 战力比 + 列移位（地形/协同/侧翼修正） |
| combat | `stepLoss` | `combat.json` | 双方步损（`攻/守` → attLoss/defLoss） |
| dice | `d6` | — | dN 骰（可复现随机） |
| turn | `phases` | `game.json` `turnReset` | IGO-UGO 阶段回合 |
| victory | `vpAndSudden` | `game.json` `endConditions` | 胜利点 + 突然死亡 |
| victory | `objectives` / `exit` / `attrition` | `game.json` `endConditions` | 目标点 / 退场 / 战损 |
| counter | `generic` | `units.json` | 算子目录（正背两态） |
| counter | `stepped` / `damageTrack` | `units.json` | 多步 / 损伤轨算子模型 |
| zoc | `off` / `zoc` | `game.json` `rules.zoc.config` | 控制区投影（进入敌 ZOC 必停/离开加费） |
| territory | `off` / `controlPoints` | `game.json` `rules.territory.config` | 格子/节点归属 + `control`/`capture` 效果 |
| supply | `off` / `traceLine` | `game.json` `rules.supply.config` | 补给线追溯（`inSupply`） |
| scenario | `generic` | `scenario.json` | 部署 / 援军 / 按节点部署 |
| cards | `standard` | `cards.json` | 牌库/手牌/抽洗弃/手牌上限/事件牌 |
| stacking | `unlimited` / `perHex` | `game.json` `rules.stacking.config` | 每格堆叠上限 |

> `family: "default"` 自动装配常用能力；只需在 `rules` 里**覆盖**想换的分类即可（值可为对象或数组；数组用于同分类多条规则，如两套胜利规则）。表达式函数、效果、独占接口都由规则注册——换规则时只要提供的函数名一致，其余规则无需改动。

## 配置 Demo：六角格 + 卡驱（零胶水）

同一套 `cards/standard` 既能配 `hexGrid`（如下），也能配 `pointToPoint`——只换 `map`/`movement` 两行：

```jsonc
{
  "id": "mygame", "playerCount": 2, "family": "default",
  "rules": {
    "map":      { "variant": "hexGrid",    "file": "map.json" },
    "movement": { "variant": "movePoints", "file": "movement.json" },
    "combat":   { "variant": "crTable",    "file": "combat.json" },
    "counter":  { "variant": "generic",    "file": "units.json" },
    "scenario": { "variant": "generic",    "file": "scenario.json" },
    "cards":    { "variant": "standard",   "file": "cards.json" },        // ← 卡驱，可选
    "stacking": { "variant": "perHex", "config": { "maxPerHex": 3 } }     // ← 堆叠，可选
  },
  "phaseOrder": ["card", "action"],                                       // 先出牌，再行动
  "setup": [ { "effect": "draw", "deck": "main", "count": 5, "player": "0" },
             { "effect": "draw", "deck": "main", "count": 5, "player": "1" } ],
  "moves": {
    "playcard": { "id": "playcard", "phase": "card", "needsCard": true,
                  "validators": [ "state.vars.cardPlayed != 1" ],
                  "effects": [ { "effect": "setvar", "key": "cardPlayed", "value": "1" } ] },
    "endphase": { "id": "endphase",
                  "validators": [ "phase != 'card' || state.vars.cardPlayed == 1" ],
                  "effects": [ { "effect": "endphase" } ] },
    "move":     { "id": "move", "kind": "movement", "phase": "action",
                  "needsCounter": true, "needsPosition": true,
                  "counterFilter": "counter.owner == me",
                  "validators": [ "notacted(counter)", "dist(counter,pos) == 1", "not(occupied(pos))" ],
                  "effects": [ { "effect": "move", "counter": "counter", "to": "pos" } ] }
  }
}
```

`cards.json` 里 `handLimit` 控制手牌上限、`kind:"event"` 的牌在打出时结算其 `effects`；`draw/replenish/shuffle/discard` 为通用效果。参考完整实现：`games/sunce-jiangdong/`（点对点 + 卡驱 + 势力参战）。

## 中台（引擎）怎么读懂这些表

1. 平台从 `rules/**/variant.json` 读规则库（`RuleCatalog`），并反射发现代码里的变体实现（`VariantRegistry`），二者 **1:1 对账**。
2. `game.json` 声明用哪些**规则**（`family` 选家族预设，可在 `rules` 里按分类覆盖，值可为数组）→ 实例化一组规则 → 注册表达式函数/效果/独占接口。
3. 校验：表结构、表达式语法与函数、**名字契约**（未知即告警）、**能力依赖与独占冲突**、重名函数/效果、剧本边界——全部在**加载时**拦下。
4. 固定管线跑规则：`校验 → 掷骰 → 结算(CRT) → 效果 → 触发 → 终局`。掷骰与抽牌走 `seed` 播种的确定性 RNG，可复现。

## 概念速览

- **正交能力 × 变体**：独占能力 `map`、`counter`、`movement`、`combat`、`dice`、`turn`、`scenario`、`cards`、`stacking`、`zoc`、`territory`、`supply` 各至多一个；函数/效果按名唯一；分类（目录）只作分组。变体 = 一个成熟算法实现 + JSON 配置，长期目标是每个能力积累十几变体。
- **表达式语言**：`counter.owner == me`、`dist(counter,pos) == 1`、`moveCost(counter,pos) <= counter.moveLeft`、`effStr(counter)/effStr(target)`——小众规则就写在这里。
- **效果原语**：`move/flip/remove/retreat/setattr/setside/setvar/addvar/log/endphase/pass/advance/eliminate`；子系统效果 `control`/`steploss`/`damage`/`exit`。
- **逃生门**：`game.json` 的 `hookType` 可指向一个 C# `IHook` 实现，兜底 JSON 表达不了的规则。

详见 `docs/`。入门读 `docs/属性与数据契约.md` 和 `docs/规则库.md`。
