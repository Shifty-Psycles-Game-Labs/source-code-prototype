using Godot;
using System.Collections.Generic;

/// <summary>
/// HUDManifoldDisplay — live manifold heartbeat monitor.
///
/// Receives the ManifoldDiagnostics signal emitted by UnifiedEngine once per
/// physics tick and renders three panels:
///
///   [MANIFOLD]  — total torsion + dominant dichotomy axis name
///   [REGULATOR] — GRDepth / MagicDepth / CognitiveGain from ManifoldRegulator
///   [NPC n]     — per-NPC attention weight row (4 floats, one per cognitive axis)
///
/// Wiring (Inspector):
///   1. Add HUDManifoldDisplay as a child of a CanvasLayer in your scene.
///   2. Assign TorsionLabel, AxisLabel, RegulatorLabel, NPCWeightsContainer exports.
///   3. Connect UnifiedEngine.ManifoldDiagnostics → OnManifoldDiagnostics().
///   4. Optionally connect UnifiedEngine.ManifoldDiagnostics → OnRegulatorUpdate()
///      after wiring the ManifoldRegulator export (if you want separate panels).
///
/// The node self-manages NPC weight labels: it creates and recycles Label children
/// inside NPCWeightsContainer so scene setup is minimal.
/// </summary>
public partial class HUDManifoldDisplay : Control
{
    // ── exports ───────────────────────────────────────────────────────────────
    /// <summary>Label showing total manifold torsion.</summary>
    [Export] public Label? TorsionLabel         { get; set; }

    /// <summary>Label showing the dominant dichotomy axis name.</summary>
    [Export] public Label? AxisLabel            { get; set; }

    /// <summary>
    /// Label showing GRDepth / MagicDepth / CognitiveGain.
    /// Updated by OnRegulatorUpdate() — wire separately or leave null.
    /// If null, regulator info is appended to TorsionLabel.
    /// </summary>
    [Export] public Label? RegulatorLabel       { get; set; }

    /// <summary>
    /// Label showing live MagicFieldEngine health: max/min field value,
    /// NaN count, Infinity count.  Leave null to skip.
    /// </summary>
    [Export] public Label? FieldHealthLabel     { get; set; }

    /// <summary>VBoxContainer that receives one Label per NPC.</summary>
    [Export] public VBoxContainer? NPCWeightsContainer { get; set; }

    /// <summary>
    /// Optional reference to the ManifoldRegulator source node (UnifiedEngine).
    /// When set, OnRegulatorUpdate() reads GRDepth/MagicDepth/CognitiveGain
    /// directly from the UnifiedEngine each tick.
    /// </summary>
    [Export] public NodePath UnifiedEnginePath  { get; set; } = "";

    // ── city stats ────────────────────────────────────────────────────────────
    /// <summary>
    /// Label showing the current city block / building count.
    /// Assign in the Inspector; leave null to skip city stat display.
    /// </summary>
    [Export] public Label? CityCountLabel       { get; set; }

    /// <summary>
    /// NodePath to a TheCityNode sibling/child in the scene.
    /// Used by _Process to read BlockCount each frame.
    /// </summary>
    [Export] public NodePath CityNodePath       { get; set; } = "";

    // ── H4 ActionContext display ───────────────────────────────────────────────
    /// <summary>
    /// Label showing the live H4 ActionContext each frame.
    /// Assign in the Inspector to a Label inside your HUD CanvasLayer.
    /// Displays: IsCasting / IsMoving / ActionMeta / Insanity / Belief / Location.
    /// Leave null to skip.
    /// </summary>
    [Export] public Label? ActionContextLabel   { get; set; }

    /// <summary>
    /// NodePath to the PlayerController in the scene.
    /// Required for ActionContext display; leave blank to skip.
    /// </summary>
    [Export] public NodePath PlayerControllerPath { get; set; } = "";

    // ── internal ──────────────────────────────────────────────────────────────
    private UnifiedEngine?    _engine;
    private TheCityNode?      _city;
    private PlayerController? _player;
    private readonly List<Label> _npcLabels = new();

    public override void _Ready()
    {
        if (!UnifiedEnginePath.IsEmpty)
            _engine = GetNodeOrNull<UnifiedEngine>(UnifiedEnginePath);

        if (!CityNodePath.IsEmpty)
            _city = GetNodeOrNull<TheCityNode>(CityNodePath);

        if (!PlayerControllerPath.IsEmpty)
            _player = GetNodeOrNull<PlayerController>(PlayerControllerPath);
    }

    // ── per-frame update ──────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (CityCountLabel != null && _city != null)
            CityCountLabel.Text = $"Blocks: {_city.BlockCount}";

        UpdateActionContextLabel();
        UpdateFieldHealthLabel();
    }

    // ── field health panel ────────────────────────────────────────────────────
    private void UpdateFieldHealthLabel()
    {
        if (FieldHealthLabel == null) return;
        if (_engine?.MagicField == null)
        {
            FieldHealthLabel.Text = "[FIELD HEALTH]\n  (no MagicFieldEngine)";
            return;
        }

        var (nanCount, infCount, maxVal, minVal) = _engine.MagicField.FieldHealth;

        // Highlight when the field is unhealthy.
        string nanStr = nanCount > 0 ? $"⚠ {nanCount}" : "0";
        string infStr = infCount > 0 ? $"⚠ {infCount}" : "0";

        FieldHealthLabel.Text =
            $"[FIELD HEALTH]\n"                    +
            $"  Max:  {maxVal:+0.0000;-0.0000}\n"  +
            $"  Min:  {minVal:+0.0000;-0.0000}\n"  +
            $"  NaN:  {nanStr}\n"                   +
            $"  Inf:  {infStr}";
    }

    // ── H4 ActionContext panel ─────────────────────────────────────────────────
    private void UpdateActionContextLabel()
    {
        if (ActionContextLabel == null || _player == null) return;

        var ctx = _player.Context;

        string actionName = ctx.ActionMeta switch
        {
            ActionContext.ACTION_NONE      => "NONE",
            ActionContext.ACTION_MOVE      => "MOVE",
            ActionContext.ACTION_CAST      => "CAST",
            ActionContext.ACTION_MOVE_CAST => "MOVE+CAST",
            _                              => "?"
        };

        string insanityBar = InsanityBar(ctx.Insanity, _player.InsanityWarningThreshold);

        ActionContextLabel.Text =
            $"[ACTION CONTEXT]\n"                                             +
            $"  Action:   {actionName}\n"                                     +
            $"  Insanity: {insanityBar}  {ctx.Insanity:F2}\n"                 +
            $"  Belief:   {ctx.Belief:F3}\n"                                  +
            $"  Emotion:  {ctx.Emotion:+0.000;-0.000}\n"                      +
            $"  Intent:   {ctx.Intent:F3}\n"                                  +
            $"  Knowledge:{ctx.Knowledge:F3}\n"                               +
            $"  Trail:    {_player.MovementTrail.Count} pts\n"                +
            $"  Pos:      {ctx.Location.X:F1},{ctx.Location.Y:F1},{ctx.Location.Z:F1}";
    }

    /// <summary>Compact ASCII insanity bar: ████░░░░ style, 8 chars wide.</summary>
    private static string InsanityBar(float insanity, float threshold)
    {
        const int width = 8;
        float pct   = threshold > 0f ? Mathf.Clamp(insanity / threshold, 0f, 1f) : 0f;
        int   filled = (int)(pct * width);
        return new string('█', filled) + new string('░', width - filled);
    }

    // ── signal receiver ───────────────────────────────────────────────────────

    /// <summary>
    /// Receives ManifoldDiagnostics(torsion, dominantAxis, npcWeights) from UnifiedEngine.
    ///
    /// npcWeights layout: flat float[] with stride 4.
    ///   npcWeights[i*4 + 0] = NPC i's weight on Intent axis
    ///   npcWeights[i*4 + 1] = NPC i's weight on Knowledge axis
    ///   npcWeights[i*4 + 2] = NPC i's weight on Emotion axis
    ///   npcWeights[i*4 + 3] = NPC i's weight on Action axis
    /// </summary>
    public void OnManifoldDiagnostics(float torsion, string dominantAxis, float[] npcWeights)
    {
        // ── Torsion + axis ─────────────────────────────────────────────────
        if (TorsionLabel != null)
            TorsionLabel.Text = $"Torsion:  {torsion:F3}";

        if (AxisLabel != null)
            AxisLabel.Text = $"Axis:     {dominantAxis}";

        // ── Regulator depths (read live from engine if wired) ──────────────
        if (_engine != null)
        {
            string regText =
                $"GRDepth:  {_engine.LastPlayerGate:F3}\n" +
                $"P.Axis:   {_engine.LastPlayerAxis}\n"    +
                $"HAMGate:  {_engine.LastHAMGate:F3}";

            if (RegulatorLabel != null)
                RegulatorLabel.Text = regText;
            else if (TorsionLabel != null)
                TorsionLabel.Text += "\n" + regText;
        }

        // ── Per-NPC weight rows ────────────────────────────────────────────
        if (NPCWeightsContainer == null) return;

        int npcCount = npcWeights.Length / 4;

        // Grow label pool if needed
        while (_npcLabels.Count < npcCount)
        {
            var lbl = new Label();
            NPCWeightsContainer.AddChild(lbl);
            _npcLabels.Add(lbl);
        }

        // Hide excess labels from a previous frame with more NPCs
        for (int i = npcCount; i < _npcLabels.Count; i++)
            _npcLabels[i].Visible = false;

        // Write current NPC rows
        for (int i = 0; i < npcCount; i++)
        {
            int    off = i * 4;
            float  wI  = npcWeights[off + 0];
            float  wK  = npcWeights[off + 1];
            float  wE  = npcWeights[off + 2];
            float  wA  = npcWeights[off + 3];

            _npcLabels[i].Visible = true;
            _npcLabels[i].Text    =
                $"NPC {i}: I={wI:F2} K={wK:F2} E={wE:F2} A={wA:F2}";
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Axis index → human-readable name.
    /// Mirrors DichotomyOperator constants so the HUD stays self-contained.
    /// </summary>
    public static string AxisName(int axis) => axis switch
    {
        DichotomyOperator.KNOWLEDGE => "Knowledge",
        DichotomyOperator.EMOTION   => "Emotion",
        DichotomyOperator.INTENT    => "Intent",
        DichotomyOperator.ACTION    => "Action",
        _                           => "Unknown"
    };
}
