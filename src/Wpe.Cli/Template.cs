namespace Wpe.Cli;

/// <summary>Scaffolding templates for `wpe new` — fill in the tables to make a game.</summary>
public static class Template
{
    public static string GameJson(string name) => $$"""
{
  "id": "{{name}}",
  "name": "{{name}}",
  "playerCount": 2,
  "family": "default",
  "rules": {
    "map":      { "variant": "hexGrid",    "file": "map.json" },
    "counter":  { "variant": "generic",    "file": "units.json" },
    "scenario": { "variant": "generic",    "file": "scenario.json" },
    "movement": { "variant": "movePoints", "file": "movement.json" },
    "combat":   { "variant": "crTable",    "file": "combat.json" },
    "dice":     { "variant": "d6" },
    "turn":     { "variant": "phases" },
    "victory":  { "variant": "vpAndSudden" }
  },
  "phaseOrder": [ "action", "turnEnd" ],
  "phaseLabels": { "action": "行动", "turnEnd": "回合结束" },
  "damage": { "strengthPenalty": 1, "movePenalty": 1 },
  "autoActWhenExhausted": true,
  "turnReset": [
    { "attr": "acted", "value": "0" },
    { "attr": "moveLeft", "value": "expr:effMove(counter)" }
  ],
  "moves": {
    "endphase": {
      "id": "endphase", "label": "结束阶段",
      "effects": [ { "effect": "endphase" } ]
    },
    "move": {
      "id": "move", "kind": "movement", "label": "移动",
      "phase": "action", "needsCounter": true, "needsPosition": true,
      "counterFilter": "counter.owner == me",
      "validators": [
        "notacted(counter)", "isfront(counter)", "inBounds(pos)",
        "dist(counter, pos) == 1",
        "moveCost(counter, pos) <= counter.moveLeft",
        "not(occupied(pos))"
      ],
      "effects": [
        { "effect": "move", "counter": "counter", "to": "pos" },
        { "effect": "setattr", "counter": "counter", "key": "moveLeft",
          "value": "counter.moveLeft - moveCost(counter, pos)" }
      ]
    },
    "attack": {
      "id": "attack", "kind": "combat", "label": "攻击",
      "phase": "action", "needsCounter": true, "needsTargetCounter": true,
      "counterFilter": "counter.owner == me",
      "targetFilter": "target.owner != me",
      "validators": [ "notacted(counter)", "isfront(counter)", "dist(counter, target) == 1" ],
      "roll": { "var": "roll", "count": 1, "sides": 6, "hand": 0 },
      "combat": "combat",
      "effects": [
        { "effect": "setattr", "counter": "counter", "key": "acted", "value": "1" }
      ],
      "resultEffects": {
        "Dd": [
          { "effect": "flip", "counter": "target", "when": "isfront(target)" },
          { "effect": "remove", "counter": "target", "when": "isback(target)" }
        ],
        "Ddr": [
          { "effect": "flip", "counter": "target", "when": "isfront(target)" },
          { "effect": "remove", "counter": "target", "when": "isback(target)" },
          { "effect": "retreat", "counter": "target", "hexes": 1, "onFail": "flip" }
        ]
      }
    },
    "pass": {
      "id": "pass", "kind": "pass", "label": "结束行动",
      "isEndAction": true, "phase": "action", "needsCounter": true,
      "counterFilter": "counter.owner == me",
      "validators": [ "notacted(counter)" ],
      "effects": [
        { "effect": "setattr", "counter": "counter", "key": "acted", "value": "1" }
      ]
    }
  },
  "endConditions": [
    { "when": "enemycount(me) == 0", "message": "敌军被全歼，你获得了胜利！" }
  ]
}
""";

    public const string MapJson = """
{
  "name": "map",
  "type": "grid",
  "hexRadius": 80,
  "pointyTop": true,
  "columns": 6,
  "rows": 6,
  "terrain": [
    "PPPPPP",
    "PPPPPP",
    "PPPPPP",
    "PPPPPP",
    "PPPPPP",
    "PPPPPP"
  ],
  "victoryHexes": [ { "q": 5, "r": 5 } ]
}
""";

    public const string MovementJson = """
{
  "terrainCost": { "P": 1 },
  "terrainDefenseBonus": {},
  "riverCrossCost": 2
}
""";

    public const string CombatJson = """
{
  "combat": {
    "rowExpr": "effStr(counter) / effStr(target)",
    "colExpr": "roll",
    "resultVar": "result",
    "table": {
      "id": "combat",
      "rows": [
        { "min": 2, "max": 999, "columns": [
          { "min": 1, "max": 3, "result": "Dd" },
          { "min": 4, "max": 6, "result": "Ddr" } ] },
        { "min": 0, "max": 1.99, "columns": [
          { "min": 1, "max": 6, "result": "0" } ] }
      ]
    }
  }
}
""";

    public const string UnitsJson = """
{
  "a1": { "name": "甲 1", "type": "infantry", "strength": 3, "move": 4 },
  "a2": { "name": "甲 2", "type": "infantry", "strength": 3, "move": 4 },
  "b1": { "name": "乙 1", "type": "infantry", "strength": 3, "move": 4 },
  "b2": { "name": "乙 2", "type": "infantry", "strength": 3, "move": 4 }
}
""";

    public const string ScenarioJson = """
{
  "id": "template",
  "name": "Template",
  "description": "填表模板：改 units.json（算子目录）、scenario.json（剧本部署）、map/movement/combat（表）即可。",
  "units": [
    { "counter": "a1", "owner": 0, "hex": [1, 3] },
    { "counter": "a2", "owner": 0, "hex": [2, 4] },
    { "counter": "b1", "owner": 1, "hex": [4, 6] },
    { "counter": "b2", "owner": 1, "hex": [3, 7] }
  ]
}
""";
}
