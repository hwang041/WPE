using System.Text.Json;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Expressions;
using Wpe.Core.Loading;
using Wpe.Core.Model;
using Wpe.Core.Modules;

namespace Wpe.Rules.Cards;

/// <summary>
/// cards / standard — the generic card framework: card definitions + decks from
/// cards.json, a flat card-instance list, deck/hand/discard/played zones, an optional
/// hand limit, and the data-driven effects `draw` / `replenish` / `shuffle` / `discard`.
/// Card-driven turn rules are authored in game.json (a `needsCard` move + triggers).
/// </summary>
public sealed class StandardDeck : RuleVariantBase
{
    private readonly Dictionary<string, CardDef> _cards = new();
    private readonly List<DeckDef> _decks = new();
    private int _handLimit; // 0 = unlimited

    public override VariantInfo Info => new()
    {
        Subsystem = "cards",
        Id = "standard",
        Description = "通用卡牌框架：cards.json 牌定义+牌库，抽/补/洗/弃 + 手牌上限",
        Provides = new[] { "draw", "replenish", "shuffle", "discard", "handsize", "handlimit", "cardZones" },
        Status = "stable"
    };

    public override void Load(string? configJson, GameDefinition def)
    {
        if (configJson == null)
            throw new InvalidDataException("[cards] 需要 cards.json 配置文件");
        using var doc = JsonDocument.Parse(configJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("handLimit", out var hl) && hl.ValueKind == JsonValueKind.Number)
            _handLimit = Math.Max(0, hl.GetInt32());

        if (root.TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Object)
            foreach (var p in cards.EnumerateObject())
            {
                var c = p.Value.Deserialize<CardDef>(JsonUtil.Opts);
                if (c == null) continue;
                c.Id = p.Name;
                _cards[p.Name] = c;
            }

        if (root.TryGetProperty("decks", out var decks) && decks.ValueKind == JsonValueKind.Object)
            foreach (var p in decks.EnumerateObject())
            {
                var d = p.Value.Deserialize<DeckDef>(JsonUtil.Opts);
                if (d == null) continue;
                d.Id = p.Name;
                _decks.Add(d);
            }
    }

    public override void Register(ModuleHost host)
    {
        foreach (var (k, v) in _cards) host.Cards[k] = v;
        host.Decks.AddRange(_decks);

        host.AddEffect("draw", DrawEffect);
        host.AddEffect("replenish", ReplenishEffect);
        host.AddEffect("shuffle", ShuffleEffect);
        host.AddEffect("discard", DiscardEffect);

        host.AddFunction("handsize", (ctx, a) =>
        {
            var p = a.Length > 0 ? (int)ValueAccessor.AsNumber(a[0]) : ctx.State.ActivePlayer;
            return (double)ctx.State.HandOf(p).Count();
        });
        host.AddFunction("handlimit", (ctx, a) => (double)_handLimit);
    }

    public override void Apply(GameState state, GameEngine? engine)
    {
        state.Cards.Clear();
        foreach (var deck in _decks)
            foreach (var entry in deck.Entries)
                for (int i = 0; i < Math.Max(0, entry.Count); i++)
                    state.Cards.Add(new CardState
                    {
                        Id = state.Cards.Count,
                        DefId = entry.Card,
                        Deck = deck.Id,
                        Owner = -1,
                        Zone = CardZone.Deck
                    });
    }

    public override void Validate(GameDefinition def, List<string> issues)
    {
        if (_cards.Count == 0)
            issues.Add("[cards] cards.json 未定义任何卡牌");
        foreach (var deck in _decks)
        {
            if (deck.Entries.Count == 0) issues.Add($"[cards] 牌库 '{deck.Id}' 没有任何卡牌");
            foreach (var entry in deck.Entries)
                if (!_cards.ContainsKey(entry.Card))
                    issues.Add($"[cards] 牌库 '{deck.Id}' 引用了未知卡牌 '{entry.Card}'");
        }
    }

    // ---- effects ----

    private void DrawEffect(RuleContext ctx, EffectDef e)
    {
        var player = ResolvePlayer(ctx, e.Player);
        DrawCards(ctx, e.Deck, player, Math.Max(1, e.Count));
    }

    /// <summary>Draw up to the hand limit for a player (per-turn card replenishment).</summary>
    private void ReplenishEffect(RuleContext ctx, EffectDef e)
    {
        if (_handLimit <= 0) return;
        var player = ResolvePlayer(ctx, e.Player);
        var need = _handLimit - ctx.State.HandOf(player).Count();
        if (need > 0) DrawCards(ctx, e.Deck, player, need);
    }

    private void ShuffleEffect(RuleContext ctx, EffectDef e)
    {
        foreach (var c in ctx.State.Cards)
            if ((string.IsNullOrEmpty(e.Deck) || c.Deck == e.Deck) &&
                (c.Zone == CardZone.Played || c.Zone == CardZone.Discard))
                c.Zone = CardZone.Deck;
        ctx.State.LogMessage(string.IsNullOrEmpty(e.Deck) ? "洗牌" : $"洗牌 [{e.Deck}]");
    }

    private void DiscardEffect(RuleContext ctx, EffectDef e)
    {
        if (ctx.Vars.TryGetValue("card", out var v) && v is CardState card)
            card.Zone = CardZone.Discard;
    }

    private static int ResolvePlayer(RuleContext ctx, string expr)
        => string.IsNullOrEmpty(expr)
            ? ctx.State.ActivePlayer
            : (int)ctx.Engine.Compile(expr).EvalNumber(ctx);

    private void DrawCards(RuleContext ctx, string deck, int player, int count)
    {
        var state = ctx.State;
        for (int n = 0; n < count; n++)
        {
            if (_handLimit > 0 && state.HandOf(player).Count() >= _handLimit) return;

            List<CardState> Pool() => state.Cards
                .Where(c => c.Zone == CardZone.Deck && (string.IsNullOrEmpty(deck) || c.Deck == deck))
                .ToList();

            var pool = Pool();
            if (pool.Count == 0)
            {
                foreach (var c in state.Cards)
                    if ((string.IsNullOrEmpty(deck) || c.Deck == deck) &&
                        (c.Zone == CardZone.Played || c.Zone == CardZone.Discard))
                        c.Zone = CardZone.Deck;
                pool = Pool();
                if (pool.Count == 0) return;
            }

            var pick = pool[ctx.Host.Rng.Next(0, pool.Count)];
            pick.Zone = CardZone.Hand;
            pick.Owner = player;
        }
    }
}
