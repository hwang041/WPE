using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Wpe.Core.Definition;
using Wpe.Core.Engine;
using Wpe.Core.Model;

namespace Wpe.App;

/// <summary>
/// Hotseat GUI. Talks to the engine only through move.kind (movement / combat /
/// recover / pass) and the reachable/targets seams — it never knows concrete move ids,
/// so it works for any table-driven game package.
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

    private const float MinZoom = 0.001f;
    private const float MaxZoom = 5f;

    private int _selectedId = -1;
    private bool _dragging;
    private Point _lastMouse;
    private readonly List<HexCoord> _moveTargets = new();
    private readonly List<int> _attackTargets = new();
    private readonly List<GameState> _undoStack = new();
    private readonly GameState _initial;

    public MainForm(GameState state, GameEngine engine, string gameDir)
    {
        _state = state;
        _engine = engine;
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
        _status = new Label { AutoSize = false, Width = 360, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
        _hexInfo = new Label { AutoSize = false, Width = 220, TextAlign = ContentAlignment.MiddleLeft };
        _log = new ListBox { Dock = DockStyle.Bottom, Height = 180, HorizontalScrollbar = true };

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
        _hexInfo.Location = new Point(1018, 8);

        Controls.Add(_log);
        Controls.Add(_sk);
        Controls.Add(top);

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

    // ---------- UI refresh ----------

    private void RefreshUi()
    {
        var who = _state.ActivePlayer == 0 ? "玩家1" : "玩家2";
        _status.Text = $"回合 {_state.TurnNumber} | 阶段 {PhaseLabel()} | 先手 {who}";
        _endPhaseBtn.Enabled = _engine.IsTurn(_state.ActivePlayer) && !_engine.GameOver;
        _consolidateBtn.Enabled = _selectedId >= 0 &&
            _engine.LegalMovesForCounter(_state.Counters[_selectedId]).Any(m => m.Kind == "recover");
        _passBtn.Enabled = _selectedId >= 0 &&
            _engine.LegalMovesForCounter(_state.Counters[_selectedId]).Any(m => m.Kind == "pass");
        _hexInfo.Text = SelectedInfo();

        foreach (var c in _state.Counters)
            if (c.AttributeInt("acted", 0) == 0 && c.RotationDeg != 0)
                c.RotationDeg = 0;

        _sk.Invalidate();
    }

    private string PhaseLabel()
    {
        return _state.CurrentPhase switch
        {
            "action" => "行动",
            "turnEnd" => "回合结束",
            _ => _state.CurrentPhase
        };
    }

    private string SelectedInfo()
    {
        if (_selectedId < 0) return "";
        var c = _state.Counters[_selectedId];
        var s = $"[{c.Id}] {c.Name} 攻{c.AttributeFloat("strength")}/移{c.AttributeFloat("move")}";
        if (c.OnBoard) s += $" hex({c.Hex.Q},{c.Hex.R})";
        if (c.AttributeInt("acted") == 1) s += " 已行动";
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
        else
        {
            var b = _renderer.ScreenToBoard(e.X, e.Y);
            if (_state.Map != null)
            {
                var hex = _state.Map.PixelToAxial(b);
                _hexInfo.Text = _state.Map.InBounds(hex)
                    ? $"屏幕→hex({hex.Q},{hex.R}) {_state.Map.TerrainAt(hex)}"
                    : $"hex({hex.Q},{hex.R}) 界外";
            }
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

        if (_selectedId >= 0)
        {
            var unit = _state.Counters[_selectedId];
            var hitEnemy = PickUnit(boardPos);
            if (hitEnemy != null && _attackTargets.Contains(hitEnemy.Id))
            {
                DoAttack(unit, hitEnemy);
                return;
            }
            if (_state.Map != null)
            {
                var hex = _state.Map.PixelToAxial(boardPos);
                var move = _engine.LegalMovesForCounter(unit).FirstOrDefault(m => m.Kind == "movement");
                if (move != null && _engine.CanApply(move.Id, unit, hex, null, out _))
                {
                    DoMove(unit, hex);
                    return;
                }
            }
        }

        var hit = PickUnit(boardPos);
        if (hit != null)
        {
            SelectUnit(hit.Id);
            return;
        }

        if (_selectedId >= 0) Deselect();
    }

    private CounterState? PickUnit(PointF pos)
    {
        if (_state.Map == null) return null;
        var half = _renderer.CounterSize / 2f;
        foreach (var c in _state.CountersOnBoard())
        {
            var center = _state.Map.CenterOf(c.Hex);
            if (Math.Abs(pos.X - center.X) <= half && Math.Abs(pos.Y - center.Y) <= half)
                return c;
        }
        return null;
    }

    private void SelectUnit(int id)
    {
        Deselect();
        _selectedId = id;
        var unit = _state.Counters[id];
        unit.Attributes["selected"] = 1;
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
        if (_selectedId >= 0) _state.Counters[_selectedId].Attributes.Remove("selected");
        _selectedId = -1;
        _moveTargets.Clear();
        _attackTargets.Clear();
        RefreshUi();
    }

    // ---------- actions ----------

    private void DoMove(CounterState unit, HexCoord hex)
    {
        _undoStack.Add(_state.Clone());
        if (_engine.Apply("move", unit, hex, null))
        {
            if (unit.AttributeInt("acted") == 1)
            {
                unit.RotationDeg = 45f;
                _state.LogMessage($"{unit.Name} 移动力/攻击用尽，行动结束");
                Deselect();
            }
            else
            {
                _state.LogMessage($"{unit.Name} 移动，剩余移动力 {unit.AttributeFloat("moveLeft")}");
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
            unit.Attributes["acted"] = 1;
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
            unit.Attributes["acted"] = 1;
            unit.RotationDeg = 45f;
            Deselect();
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    private void DoAttack(CounterState attacker, CounterState target)
    {
        _undoStack.Add(_state.Clone());
        if (_engine.Apply("attack", attacker, null, target))
        {
            attacker.Attributes["acted"] = 1;
            attacker.RotationDeg = 45f;
            _state.LogMessage($"{attacker.Name} 攻击 {target.Name}，骰子 {string.Join(",", _state.LastDice.Select(d => d.Value))}");
            Deselect();
        }
        PushLog();
        RefreshUi();
        CheckGameOver();
    }

    private void DoEndPhase()
    {
        _undoStack.Add(_state.Clone());
        if (_engine.Apply("endphase", null, null, null))
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
        _state.Log.Clear();
        _state.Log.AddRange(snap.Log);
        _state.LastDice.Clear();
        _state.LastDice.AddRange(snap.LastDice);
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
            MessageBox.Show(_engine.ResultMessage, "游戏结束", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        var R = map?.HexRadius ?? 80f;

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
            if (!c.OnBoard) continue;
            var (sx, sy) = _renderer.BoardToScreen(map!.CenterOf(c.Hex).X, map.CenterOf(c.Hex).Y);
            canvas.DrawCircle(sx, sy, R * _renderer.Scale, attackPaint);
        }
    }
}
