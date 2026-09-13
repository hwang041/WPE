# 徐州之战 · WPE 游戏包手册

依托 default 规则构建的独立游戏包（表驱动，无引擎改动）。地图/算子/剧本均符合史料地理。

## 地图（24×15，比汉中之战 20×13 更大）

```
r 0  MMMMMMMMMMMMMMMMMMMMMMMM      ← 泰山北脉
r 1  MMPPMMMMMMMMPPPMMMMMMMMM      ← 山口：西(泗水入)、中(刘备进军路线 12-14)
r 2  MMPPPPPMMMMPPPPPPMMMMMMM
r 3  PPPPPTPPPPPPPPPPPPPPHHHH      ← 小沛(5,3)   ★渡口q5
r 4  PPPPPPPPPPPPPPPPPTPPHHHH      ← 郯城(17,4)★ 徐州刺史部治所（陶谦退保于此）
r 5  HHHPPPPPTFFFFFFFPPPPPPPP      ← 广戚(8,5)（于禁所拔）
r 6  HHHPPPPPPFFFFFFFPPPPPPPP
r 7  PPPPPPPPFFFFFFFPPPPPPPP       ← 泗水中游林带
r 8  PPPPPPPPPTPPPPPPPPPPPPPP      ← 彭城(9,8)★（今徐州，曹陶大战地）
r 9  PPPPPPPPPPPPPPPPPPPPPPPP
r10  PPPPTPPPPPPPPPPPPPPFFFFF      ← 萧县(4,10) ★渡口q4
r11  PPPPPPPPPPPPPPPPPPPFFFFF
r12  HHHHHHHHPPPPPPPPPPTFFFFF      ← 下邳(18,12)★（刘备后迁治所于此）
r13  HHHHHHHHHHHPPPPPPPPFFFFF
r14  HHHHHHHHHHHPPPPPPPPPPPPP      ← 萧县以南丘陵（吕梁/低丘）
```

**水系（六角格边，非格子）**：三条河均由 `wpe river` **按相邻格路径自动生成**（每相邻两条边共享同一顶点、逐段 120° 转向，成一条不间断的贴格河流，含渡口）：
- **泗水**：沿 8/9 行边界自西向东，穿彭城(9,8)（渡口近彭城）。
- **沂水**：郯城(17,4)起南北纵贯（渡口近郯城），南至下邳(18,12)（渡口近下邳），途中与泗水东端在 (18,8) 附近汇合。
- **汴水**：西部横流经萧县(4,10)（渡口近萧县）。

改河/加河：
```
wpe river games/xuzhou "q,r" "q,r" ... [--fords 边序号,边序号] [--append]   # 生成/追加河流边（自动校验相邻与 120° 转向）
wpe overlay games/xuzhou map-overlay.png                                    # 渲染核对（输出 riverChains 连通分量）
```

## 势力与算子（units.json，44 枚）

| 势力 | 颜色 | 名将 | 备注 |
|---|---|---|---|
| 曹操 | 蓝 | 曹仁/夏侯惇/夏侯渊/曹洪/于禁/乐进/典韦 + 虎豹骑×2 + 部曲×4 | 攻方 |
| 陶谦 | 绿 | 曹豹/糜竺/陈登 + 丹阳兵×2 + 部曲×6 | 守方 |
| 刘备 | 红 | 关羽/张飞/赵云/简雍 + 田楷 + 部曲×3 | 陶谦盟军（剧本一为援军） |
| 吕布 | 黄 | 陈宫/高顺/张辽 + 陷阵营 + 并州铁骑 + 部曲×3 | 预留（后续剧本：下邳之战） |

## 剧本一：曹操伐陶谦 · 刘备来救（scenario.json）

- **背景**：兴平元年(194)曹操为父报仇伐徐州，破彭城，陶谦退保郯县。刘备随田楷自青州来救。曹操攻郯不克，粮尽退兵。
- **部署**：曹操 14 枚自西（彭城以西泗水走廊）进攻；陶谦 12 枚分守小沛/萧县/彭城/郯城/下邳（陶谦亲守郯城）；**刘备 9 枚为援军**，自北山口 (13,1) 分批进场（关羽/张飞 T2，赵云/田楷 T3，部曲 T3~4）。
- **胜利**：
  - 曹操：占领 彭城/郯城/下邳 三处 ★ 之二 → 徐州易主；
  - 陶谦：坚守至第 10 回合 → 曹操粮尽退兵（守卫徐州胜利）；
  - 全歼敌军即胜（任意方）。

> 注：剧本一为双人（曹操 owner 0 vs 陶谦+刘备 owner 1）。吕布势力已在 units.json 备好，供后续"下邳之战"等剧本选用。

## 数据表

- `game.json`：主规则（复用 default 家族，行动含 move/attack/recover/pass/endphase）
- `combat.json`：战力比 CRT（与汉中之战相同）
- `movement.json`：地形费 P1/H2/F2/M3/T1，河费 2

## 运行

```
wpe verify  games/xuzhou     # 校验
wpe play    games/xuzhou     # 无头对局（回归）
wpe counters games/xuzhou    # 批量出白板算子（art/）
Wpe.App games/xuzhou         # 图形界面
```
