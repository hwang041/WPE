using System.Text;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;

namespace Wpe.App;

/// <summary>
/// Hotseat GUI. Talks to the engine only through move.kind (movement / combat /
/// recover / pass), the reachable/targets seams and the cards seam — it never knows
/// concrete move ids, so it works for any table-driven game package.
/// </summary>
public sealed class MainForm : Form
{
    private readonly GameState _state;
    private readonly GameEngine _engine;
    private readonly BoardRenderer _renderer;
    private readonly SKControl _sk;
    private readonly ListBox _log;
    private readonly Label _status;
    private readonly Label _hexInfo;
    private readonly Button _endPhaseBtn;
    private readonly Button _consolidateBtn;
    private readonly Button _passBtn;
    private readonly Button _undoBtn;
    private readonly Button _resetBtn;
    private readonly CheckBox _hexGridChk;
    private readonly CheckBox _hexNumChk;
    private readonly Panel _handPanel;
    private readonly Label _handLabel;
    private readonly FlowLayoutPanel _hand;
    private readonly Panel _stackPanel;
    private readonly ListBox _stackList;

    private const float MinZoom = 0.001f;
    private const float MaxZoom = 5f;

    /// <summary>UI-only transient attribute marking the selected counter (not a game contract).</summary>
    public const string SelectedAttr = "selected";

    private string ActedAttr => _engine.Def.ActedAttr;

    private readonly bool _headless;
    private int _selectedId = -1;
    private bool _dragging;
    private Point _lastMouse;
    private readonly List<HexCoord> _moveTargets = new();
    private readonly List<int> _attackTargets = new();
    private readonly List<GameState> _undoStack = new();
    private readonly GameState _initial;

    public MainForm(GameState state, GameEngine engine, string gameDir, bool headless = false)
    {
        _state = state;
        _engine = engine;
        _headless = headless;
        _initial = state.Clone();
        _renderer = new BoardRenderer(state);

        Text = $"{engine.Def.Name} — WPE 热座";
        ClientSize = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        _sk = new SKControl { Dock = DockStyle.Fill };
        _sk.PaintSurface += OnPaint;
        _sk.MouseDown += OnMouseDown;
        _sk.MouseMove += OnMouseMove;
        _sk.MouseUp += OnMouseUp;
        _sk.MouseWheel += OnMouseWheel;

        _endPhaseBtn = new Button { Text = "结束阶段 ▶", Width = 110 };
        _consolidateBtn = new Button { Text = "集结/恢复", Width = 90, Enabled = false };
        _passBtn = new Button { Text = "结束行动", Width = 90, Enabled = false };
        _undoBtn = new Button { Text = "悔棋 ↺", Width = 80 };
        _resetBtn = new Button { Text = "重置 ↺", Width = 80 };
        _hexGridChk = new CheckBox { Text = "六角格", Checked = true, AutoSize = true };
        _hexNumChk = new CheckBox { Text = "序号", Checked = false, AutoSize = true };
        _status = new Label { AutoSize = false, Width = 420, TextAlign = ContentAlignment.MiddleLeft };
        _hexInfo = new Label { AutoSize = false, Width = 240, TextAlign = ContentAlignment.MiddleLeft };
        _log = new ListBox { Dock = DockStyle.Bottom, Height = 150, HorizontalScrollbar = true };

        _handPanel = new Panel { Dock = DockStyle.Fill };
        _handLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "" };
        _hand = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        _handPanel.Controls.Add(_hand);
        _handPanel.Controls.Add(_handLabel);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 300 };
        bottom.Controls.Add(_handPanel);
        bottom.Controls.Add(_log);

        var top = new Panel { Dock = DockStyle.Top, Height = 38 };
        top.Controls.AddRange(new Control[] { _endPhaseBtn, _consolidateBtn, _passBtn, _undoBtn, _resetBtn, _hexGridChk, _hexNumChk, _status, _hexInfo });
        _endPhaseBtn.Location = new Point(8, 6);
        _consolidateBtn.Location = new Point(126, 6);
        _passBtn.Location = new Point(224, 6);
        _undoBtn.Location = new Point(322, 6);
        _resetBtn.Location = new Point(410, 6);
        _hexGridChk.Location = new Point(498, 10);
        _hexNumChk.Location = new Point(566, 10);
        _status.Location = new Point(628, 8);
        _hexInfo.Location = new Point(1058, 8);

        _stackPanel = new Panel { Left = 8, Top = 48, Width = 180, Height = 240, Visible = false, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(245, 245, 245) };
        var stackHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "堆叠 · 选择算子",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(222, 228, 222),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
        };
        _stackList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9.5f), IntegralHeight = false };
        _stackList.Click += (s, e) => { if (_stackList.SelectedIndex >= 0) SelectStackItem(_stackList.SelectedIndex); };
        _stackPanel.Controls.Add(_stackList);
        _stackPanel.Controls.Add(stackHeader);

        Controls.Add(_sk);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(_stackPanel);
        _stackPanel.BringToFront();

        _endPhaseBtn.Click += (s, e) => DoEndPhase();
        _consolidateBtn.Click += (s, e) => DoConsolidate();
        _passBtn.Click += (s, e) => DoPass();
        _undoBtn.Click += (s, e) => Undo();
        _resetBtn.Click += (s, e) => ResetGame();
        _hexGridChk.CheckedChanged += (s, e) => { _renderer.ShowHexGrid = _hexGridChk.Checked; _sk.Invalidate(); };
        _hexNumChk.CheckedChanged += (s, e) => { _renderer.ShowHexNumbers = _hexNumChk.Checked; _sk.Invalidate(); };

        _status.Click += (s, e) => CenterOnMap();

        RefreshUi();
        CenterOnMap();
        state.LogMessage("欢迎！点击己方算子行动：移动(绿格) / 攻击(红圈) / 恢复 / 结束行动。");
        PushLog();
    }

    private int Var(string key) => _state.Vars.TryGetValue(key, out var v) ? (int)Value(v) : 0;
    private static double Value(object? v) => v switch
    {
        double d => d, int i => i, float f => f, long l => l,
        string s => double.TryParse(s, out var d2) ? d2 : 0, bool b => b ? 1 : 0, _ => 0
    };

    // ---------- UI refresh ----------

    private void RefreshUi()
    {
        var who = _state.ActivePlayer == 0 ? "玩家1" : "玩家2";
        var extra = "";
        foreach (var key in _engine.Def.HudVars)
            extra += $" | {key} {Var(key)}";
        _status.Text = $"回合 {_state.TurnNumber} | 阶段 {PhaseLabel()} | 先手 {who}{extra}";
        _endPhaseBtn.Enabled = _engine.IsTurn(_state.ActivePlayer) && !_engine.GameOver && _engine.CanEndPhase(out _);
        _consolidateBtn.Enabled = _selectedId >= 0 &&
            _engine.LegalMovesForCounter(_state.Counters[_selectedId]).Any(m => m.Kind == "recover");
        _passBtn.Enabled = _selectedId >= 0 &&
            _engine.LegalMovesForCounter(_state.Counters[_selectedId]).Any(m => m.Kind == "pass");
        _hexInfo.Text = SelectedInfo();

        foreach (var c in _state.Counters)
            if (c.AttributeInt(ActedAttr, 0) == 0 && c.RotationDeg != 0)
                c.RotationDeg = 0;

        RebuildHand();
        _sk.Invalidate();
    }

    private string PhaseLabel()
        => _engine.Def.PhaseLabels.TryGetValue(_state.CurrentPhase, out var label) ? label : _state.CurrentPhase;

    private string SelectedInfo()
    {
        if (_selectedId < 0) return "";
        var c = _state.Counters[_selectedId];
        var s = $"[{c.Id}] {c.Name} 攻{c.AttributeFloat(ContractNames.Strength)}/移{c.AttributeFloat(ContractNames.Move)}";
        if (c.OnBoard)
        {
            if (_state.Map is SpaceMap space)
            {
                var node = space.NearestNode(space.CenterOf(c.Hex));
                if (node != null) s += $" {node.Name}·{node.AttrStr("type", "")} 城防{node.AttrInt("defense", 0)}";
            }
            else s += $" hex({c.Hex.Q},{c.Hex.R})";
        }
        if (c.AttributeInt(ActedAttr) == 1) s += " 已行动";
        return s;
    }

    private void PushLog()
    {
        _log.BeginUpdate();
        _log.Items.Clear();
        foreach (var line in _state.Log.TakeLast(200)) _log.Items.Add(line);
        _log.SelectedIndex = _log.Items.Count - 1;
        _log.EndUpdate();
    }

    // ---------- cards ----------

    private void RebuildHand()
    {
        _hand.Controls.Clear();
        if (_state.Cards.Count == 0) { _handLabel.Text = ""; return; }

        var player = _state.ActivePlayer;
        var cards = _engine.Hand(player);
        _handLabel.Text = $"玩家{player + 1} 手牌 {cards.Count}（{PhaseLabel()}）";
        foreach (var card in cards)
        {
            _engine.Host.Cards.TryGetValue(card.DefId, out var def);
            var kind = def?.Kind ?? "action";
            var header = kind == "action" ? $"{def?.Name} 行动 {def?.Value}" : $"{def?.Name} 事件";
            var btn = new Button
            {
                Width = 128,
                Height = 110,
                Margin = new Padding(4),
                Text = $"{header}\n\n{def?.Text}",
                TextAlign = ContentAlignment.TopCenter,
                BackColor = kind == "action" ? Color.FromArgb(255, 244, 214) : Color.FromArgb(222, 236, 255),
                Enabled = _engine.IsTurn(player) && _engine.CanPlayCard(card, out _)
            };
            var captured = card;
            btn.Click += (s, e) => DoPlayCard(captured);
            _hand.Controls.Add(btn);
        }
    }

    private void DoPlayCard(CardState card)
    {
        _undoStack.Add(_state.Clone());
        if (_engine.PlayCard(card))
        {
            _engine.Host.Cards.TryGetValue(card.DefId, out var def);
            _state.LogMessage($"打出 [{def?.Name}]");
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    // ---------- input ----------

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var p = _renderer.ScreenToBoard(e.X, e.Y);
            HandleClick(p);
        }
        else if (e.Button is MouseButtons.Middle or MouseButtons.Right)
        {
            _dragging = true;
            _lastMouse = e.Location;
        }
        _sk.Focus();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            _renderer.PanX -= (e.X - _lastMouse.X) / _renderer.Scale;
            _renderer.PanY -= (e.Y - _lastMouse.Y) / _renderer.Scale;
            _lastMouse = e.Location;
            _sk.Invalidate();
        }
        else if (_state.Map is SpaceMap space)
        {
            var b = _renderer.ScreenToBoard(e.X, e.Y);
            var node = space.NearestNode(b);
            _hexInfo.Text = node != null ? $"→ {node.Name}" : "";
        }
        else if (_state.Map != null)
        {
            var b = _renderer.ScreenToBoard(e.X, e.Y);
            var hex = _state.Map.CellAt(b);
            _hexInfo.Text = _state.Map.InBounds(hex)
                ? $"屏幕→hex({hex.Q},{hex.R}) {_state.Map.TerrainAt(hex)}"
                : $"hex({hex.Q},{hex.R}) 界外";
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e) => _dragging = false;

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.15f : 1 / 1.15f;
        _renderer.Scale = Math.Clamp(_renderer.Scale * factor, MinZoom, MaxZoom);
        _sk.Invalidate();
    }

    private void HandleClick(PointF boardPos)
    {
        if (_engine.GameOver) return;
        HideStackList();

        if (_selectedId >= 0)
        {
            var unit = _state.Counters[_selectedId];
            var hit = PickTop(boardPos);
            if (hit != null && _attackTargets.Contains(hit.Id))
            {
                DoAttack(unit, hit);
                return;
            }
            if (_state.Map != null)
            {
                var cell = _state.Map.CellAt(boardPos);
                var move = _engine.LegalMovesForCounter(unit).FirstOrDefault(m => m.Kind == "movement");
                if (move != null && _engine.CanApply(move.Id, unit, cell, null, out _))
                {
                    DoMove(unit, cell);
                    return;
                }
            }
        }

        var stack = StackAt(boardPos);
        if (stack.Count > 1)
        {
            ShowStackList(stack);
            return;
        }
        if (stack.Count == 1)
        {
            SelectUnit(stack[0].Id);
            return;
        }
        if (_selectedId >= 0) Deselect();
    }

    /// <summary>Counters under a board point, top-most first (fanned stacks included).</summary>
    private List<CounterState> StackAt(PointF pos)
    {
        var result = new List<CounterState>();
        if (_state.Map == null) return result;
        var size = _renderer.CounterSize;
        var half = size / 2f;
        foreach (var group in _state.CountersOnBoard().GroupBy(c => c.Hex))
        {
            var list = group.OrderBy(c => c.Id).ToList();
            var center = _state.Map.CenterOf(group.Key);
            bool hit = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                float ox = 0, oy = 0;
                if (_state.Map is SpaceMap) { ox = i * size * 0.20f; oy = i * size * 0.32f; }
                if (Math.Abs(pos.X - (center.X + ox)) <= half && Math.Abs(pos.Y - (center.Y + oy)) <= half)
                {
                    hit = true;
                    break;
                }
            }
            if (hit)
                for (int i = list.Count - 1; i >= 0; i--) result.Add(list[i]); // top-most first
        }
        return result;
    }

    private CounterState? PickTop(PointF pos) => StackAt(pos).FirstOrDefault();

    private void ShowStackList(List<CounterState> stack)
    {
        _stackList.Items.Clear();
        foreach (var c in stack)
            _stackList.Items.Add($"{c.Name} 攻{c.AttributeFloat(ContractNames.Strength)}{(c.AttributeInt(ActedAttr, 0) == 1 ? " (已行动)" : "")}");
        _stackList.Tag = stack;
        _stackList.SelectedIndex = 0;
        _stackPanel.Visible = true;
    }

    private void SelectStackItem(int index)
    {
        if (_stackList.Tag is List<CounterState> list && index >= 0 && index < list.Count)
        {
            SelectUnit(list[index].Id);
            HideStackList();
        }
    }

    private void HideStackList()
    {
        _stackPanel.Visible = false;
        _stackList.Tag = null;
    }

    private void SelectUnit(int id)
    {
        Deselect();
        _selectedId = id;
        var unit = _state.Counters[id];
        unit.Attributes[SelectedAttr] = 1;
        var moves = _engine.LegalMovesForCounter(unit);
        _state.LogMessage($"选中 [{id}] {unit.Name}：可用 {string.Join(",", moves.Select(m => m.Kind))}");

        _moveTargets.Clear();
        _moveTargets.AddRange(_engine.ReachablePositions(unit));
        _attackTargets.Clear();
        _attackTargets.AddRange(moves.Where(m => m.Kind == "combat")
            .SelectMany(m => _engine.TargetCountersForMove(m, unit)).Select(c => c.Id));
        RefreshUi();
    }

    private void Deselect()
    {
        if (_selectedId >= 0) _state.Counters[_selectedId].Attributes.Remove(SelectedAttr);
        _selectedId = -1;
        _moveTargets.Clear();
        _attackTargets.Clear();
        RefreshUi();
    }

    // ---------- actions ----------

    private void DoMove(CounterState unit, HexCoord cell)
    {
        _undoStack.Add(_state.Clone());
        var move = _engine.LegalMovesForCounter(unit).FirstOrDefault(m => m.Kind == "movement");
        if (move != null && _engine.Apply(move.Id, unit, cell, null))
        {
            if (unit.AttributeInt(ActedAttr, 0) == 1)
            {
                unit.RotationDeg = 45f;
                Deselect();
            }
            else
            {
                _moveTargets.Clear();
                _moveTargets.AddRange(_engine.ReachablePositions(unit));
                _attackTargets.Clear();
                _attackTargets.AddRange(_engine.LegalMovesForCounter(unit).Where(m => m.Kind == "combat")
                    .SelectMany(m => _engine.TargetCountersForMove(m, unit)).Select(c => c.Id));
            }
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    private void DoConsolidate()
    {
        if (_selectedId < 0) return;
        var unit = _state.Counters[_selectedId];
        var move = _engine.LegalMovesForCounter(unit).FirstOrDefault(m => m.Kind == "recover");
        if (move == null) return;
        _undoStack.Add(_state.Clone());
        if (_engine.Apply(move.Id, unit, null, null))
        {
            unit.Attributes[ActedAttr] = 1;
            unit.RotationDeg = 45f;
            Deselect();
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    private void DoPass()
    {
        if (_selectedId < 0) return;
        var unit = _state.Counters[_selectedId];
        var move = _engine.LegalMovesForCounter(unit).FirstOrDefault(m => m.Kind == "pass");
        if (move == null) return;
        _undoStack.Add(_state.Clone());
        if (_engine.Apply(move.Id, unit, null, null))
        {
            unit.Attributes[ActedAttr] = 1;
            unit.RotationDeg = 45f;
            Deselect();
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    private void DoAttack(CounterState attacker, CounterState target)
    {
        var move = _engine.LegalMovesForCounter(attacker).FirstOrDefault(m => m.Kind == "combat");
        if (move == null) return;
        _undoStack.Add(_state.Clone());
        if (_engine.Apply(move.Id, attacker, null, target))
        {
            attacker.Attributes[ActedAttr] = 1;
            attacker.RotationDeg = 45f;
            Deselect();
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    private void DoEndPhase()
    {
        var move = _engine.EndPhaseMove;
        if (move == null) return;
        _undoStack.Add(_state.Clone());
        if (_engine.Apply(move.Id, null, null, null))
        {
            Deselect();
            _state.LogMessage($"—— 进入 {_state.CurrentPhase} ——");
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        var snap = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        RestoreFrom(snap);
    }

    private void ResetGame()
    {
        _undoStack.Clear();
        RestoreFrom(_initial);
        _engine.ResetRuntime();
        _state.LogMessage("—— 已重置到初始部署 ——");
        Deselect();
        PushLog();
        RefreshUi();
    }

    private void RestoreFrom(GameState snap)
    {
        _state.ActivePlayer = snap.ActivePlayer;
        _state.TurnNumber = snap.TurnNumber;
        _state.CurrentPhase = snap.CurrentPhase;
        _state.GameOver = snap.GameOver;
        _state.ResultMessage = snap.ResultMessage;
        _state.Vars.Clear();
        foreach (var (k, v) in snap.Vars) _state.Vars[k] = v;
        _state.Control.Clear();
        foreach (var (k, v) in snap.Control) _state.Control[k] = v;
        _state.Log.Clear();
        _state.Log.AddRange(snap.Log);
        _state.LastDice.Clear();
        _state.LastDice.AddRange(snap.LastDice);
        for (int i = 0; i < _state.Cards.Count && i < snap.Cards.Count; i++)
        {
            var c = _state.Cards[i]; var s = snap.Cards[i];
            c.DefId = s.DefId; c.Deck = s.Deck; c.Owner = s.Owner; c.Zone = s.Zone;
        }
        for (int i = 0; i < _state.Counters.Count; i++)
        {
            var c = _state.Counters[i]; var s = snap.Counters[i];
            c.Position = s.Position; c.RotationDeg = s.RotationDeg; c.Side = s.Side;
            c.Attributes.Clear();
            foreach (var (k, v) in s.Attributes) c.Attributes[k] = v;
        }
        Deselect();
        PushLog();
        RefreshUi();
    }

    private void CheckGameOver()
    {
        if (_engine.GameOver)
        {
            _state.LogMessage($"===== 游戏结束：{_engine.ResultMessage} =====");
            PushLog();
            if (!_headless) MessageBox.Show(_engine.ResultMessage, "游戏结束", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void CenterOnMap()
    {
        if (_state.Map == null) return;
        var total = _state.Map.TotalArea;
        _renderer.PanX = total.Width / 2;
        _renderer.PanY = total.Height / 2;
        _sk.Invalidate();
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        _renderer.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        DrawHighlights(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }

    private void DrawHighlights(SKCanvas canvas, int w, int h)
    {
        using var movePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(80, 220, 80, 90) };
        using var attackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(240, 80, 80, 120) };
        var map = _state.Map;
        var R = map?.CellRadius ?? 80f;

        foreach (var hex in _moveTargets)
        {
            if (map == null || !map.InBounds(hex)) continue;
            var c = map.CenterOf(hex);
            var (sx, sy) = _renderer.BoardToScreen(c.X, c.Y);
            canvas.DrawCircle(sx, sy, R * _renderer.Scale, movePaint);
        }
        foreach (var id in _attackTargets)
        {
            var c = _state.Counters[id];
            if (!c.OnBoard || map == null) continue;
            var center = map.CenterOf(c.Hex);
            var (sx, sy) = _renderer.BoardToScreen(center.X, center.Y);
            canvas.DrawCircle(sx, sy, R * _renderer.Scale, attackPaint);
        }
    }

    // ---------- headless self-test (--uitest) ----------

    public string SmokeTest()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine($"game={_engine.Def.Name} phase={_state.CurrentPhase} cards={_state.Cards.Count}");
            if (_engine.Def.Moves.Values.Any(m => m.NeedsCard))
            {
                var card = _engine.Hand(_state.ActivePlayer).FirstOrDefault(c => _engine.CanPlayCard(c, out _));
                if (card != null)
                {
                    DoPlayCard(card);
                    var hud = string.Join(",", _engine.Def.HudVars.Select(k => $"{k}={Var(k)}"));
                    sb.AppendLine($"played zone={card.Zone} {hud}");
                    var second = _engine.Hand(_state.ActivePlayer).FirstOrDefault(c => _engine.CanPlayCard(c, out _));
                    sb.AppendLine($"secondPlayable={second != null}");
                }
                else sb.AppendLine("no playable card");
                if (_engine.CanEndPhase(out _)) DoEndPhase();
                sb.AppendLine($"afterEndPhase phase={_state.CurrentPhase}");
            }

            var unit = _state.CountersOnBoard()
                .FirstOrDefault(c => c.AttributeInt(ContractNames.Owner, -1) == _state.ActivePlayer && c.AttributeInt(ActedAttr, 0) == 0);
            if (unit != null)
            {
                SelectUnit(unit.Id);
                sb.AppendLine($"selected={unit.Name} moveTargets={_moveTargets.Count} attackTargets={_attackTargets.Count}");
                if (_moveTargets.Count > 0)
                {
                    DoMove(unit, _moveTargets[0]);
                    sb.AppendLine($"moved -> {unit.Hex.Q},{unit.Hex.R} onBoard={unit.OnBoard}");
                }
                if (_attackTargets.Count > 0)
                {
                    var target = _state.Counters[_attackTargets[0]];
                    DoAttack(unit, target);
                    sb.AppendLine($"attacked {target.Name} side={target.Side} onBoard={target.OnBoard}");
                }
            }

            // stack fan-out / selection on a node holding multiple counters
            var multi = _state.CountersOnBoard().GroupBy(c => c.Hex).FirstOrDefault(g => g.Count() > 1);
            if (multi != null)
            {
                var list = multi.OrderBy(c => c.Id).ToList();
                var center = _state.Map!.CenterOf(multi.Key);
                var size = _renderer.CounterSize;
                int i = list.Count - 1;
                var probe = new PointF(center.X + i * size * 0.20f, center.Y + i * size * 0.32f);
                var stack = StackAt(probe);
                ShowStackList(stack);
                sb.AppendLine($"stackAtNode count={stack.Count} panel={_stackPanel.Visible}");
                if (stack.Count > 0) { SelectStackItem(0); sb.AppendLine($"stackSelected={_state.Counters[_selectedId].Name}"); }
            }

            sb.AppendLine("UITEST OK");
        }
        catch (Exception ex)
        {
            sb.AppendLine("UITEST ERROR");
            sb.AppendLine(ex.ToString());
        }
        return sb.ToString();
    }
}
