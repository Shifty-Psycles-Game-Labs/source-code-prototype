using Godot;

/// <summary>
/// UniverseRoot — top-level Godot node for the NEM-U Engine.
///
/// Owns and orchestrates all simulation subsystems:
///   • Physical physics engines: MaxwellEngine2Form, GRHarmonicEngine, UnifiedEngine
///   • Cognitive field layers (OM4): KnowledgeGraphNode, EmotionFieldNode,
///     IntentFieldNode, ActionLayerNode, RelationshipCoupler
///   • Topology: SimplicialComplexNode
///
/// Simulation loop (per physics tick):
///   1. Cognitive layers step individually (K, E, I, A).
///   2. RelationshipCoupler.Couple() binds all four — cross-field attention.
///   3. UnifiedEngine steps EM↔GR↔Magic pipeline.
///   4. DebugLabel updated with live torsion/axis/arousal/urgency state.
/// </summary>
public partial class UniverseRoot : Node3D
{
    [Export] public bool  AutoStart      = true;
    [Export] public bool  RunSimulation  = true;
    [Export] public float TimeScale      = 1.0f;

    // ── physical engine paths ──────────────────────────────────────────────────
    [Export] public NodePath SimplicialComplexPath = "World/SimplicialComplex";
    [Export] public NodePath MaxwellPath           = "World/MaxwellEngine";
    [Export] public NodePath GrPath                = "World/GRHarmonicEngine";
    [Export] public NodePath UnifiedEnginePath     = "World/UnifiedEngine";

    // ── cognitive layer paths (OM4) ───────────────────────────────────────────
    [Export] public NodePath KnowledgePath        = "World/KnowledgeGraph";
    [Export] public NodePath EmotionPath          = "World/EmotionField";
    [Export] public NodePath IntentPath           = "World/IntentField";
    [Export] public NodePath ActionPath           = "World/ActionLayer";
    [Export] public NodePath CouplerPath          = "World/RelationshipCoupler";
    [Export] public NodePath PlayerHElementPath   = "World/PlayerHElement";

    // ── debug HUD path ────────────────────────────────────────────────────────
    [Export] public NodePath DebugLabelPath = "DebugHUD/DebugLabel";

    // ── typed node references ─────────────────────────────────────────────────
    private Node?               _simplicialComplex;
    private Node?               _maxwell;
    private Node?               _gr;
    private Node?               _unifiedEngine;

    private KnowledgeGraphNode?  _knowledge;
    private EmotionFieldNode?    _emotion;
    private IntentFieldNode?     _intent;
    private ActionLayerNode?     _action;
    private RelationshipCoupler? _coupler;

    private Label? _debugLabel;

    public override void _Ready()
    {
        BindChildren();

        if (AutoStart)
            StartSimulation();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!RunSimulation) return;

        // ── OM4 cognitive layer tick ───────────────────────────────────────────
        _knowledge?.Step(delta);
        _emotion?.Step(delta);
        _intent?.Step(delta);
        _action?.Step(delta);

        _coupler?.Couple(_knowledge, _emotion, _intent, _action);

        // ── physical engine tick ───────────────────────────────────────────────
        if (_unifiedEngine != null)
        {
            InvokeNodeMethod(_unifiedEngine, "_PhysicsProcess", delta);
        }
        else
        {
            InvokeNodeMethod(_maxwell, "_PhysicsProcess", delta);
            InvokeNodeMethod(_gr,      "_PhysicsProcess", delta);
        }

        // ── debug HUD ─────────────────────────────────────────────────────────
        UpdateDebugHUD();
    }

    public void BindChildren()
    {
        _simplicialComplex = GetNodeOrNull<Node>(SimplicialComplexPath);
        _maxwell           = GetNodeOrNull<Node>(MaxwellPath);
        _gr                = GetNodeOrNull<Node>(GrPath);
        _unifiedEngine     = GetNodeOrNull<Node>(UnifiedEnginePath);

        _knowledge = GetNodeOrNull<KnowledgeGraphNode>(KnowledgePath);
        _emotion   = GetNodeOrNull<EmotionFieldNode>(EmotionPath);
        _intent    = GetNodeOrNull<IntentFieldNode>(IntentPath);
        _action    = GetNodeOrNull<ActionLayerNode>(ActionPath);
        _coupler   = GetNodeOrNull<RelationshipCoupler>(CouplerPath);

        _debugLabel = GetNodeOrNull<Label>(DebugLabelPath);

        if (_simplicialComplex == null)
            GD.PushWarning("UniverseRoot: SimplicialComplex not found at '" + SimplicialComplexPath + "'.");
        if (_maxwell == null)
            GD.PushWarning("UniverseRoot: Maxwell node not found at '" + MaxwellPath + "'.");
        if (_gr == null)
            GD.PushWarning("UniverseRoot: GR engine not found at '" + GrPath + "'.");
        if (_unifiedEngine == null)
            GD.PushWarning("UniverseRoot: UnifiedEngine not found at '" + UnifiedEnginePath + "'.");
        if (_knowledge == null)
            GD.PushWarning("UniverseRoot: KnowledgeGraphNode not found at '" + KnowledgePath + "'.");
        if (_emotion == null)
            GD.PushWarning("UniverseRoot: EmotionFieldNode not found at '" + EmotionPath + "'.");
        if (_intent == null)
            GD.PushWarning("UniverseRoot: IntentFieldNode not found at '" + IntentPath + "'.");
        if (_action == null)
            GD.PushWarning("UniverseRoot: ActionLayerNode not found at '" + ActionPath + "'.");
        if (_coupler == null)
            GD.PushWarning("UniverseRoot: RelationshipCoupler not found at '" + CouplerPath + "'.");
    }

    public void StartSimulation()
    {
        RunSimulation = true;

        if (_simplicialComplex != null)
            InvokeNodeMethodNoArgs(_simplicialComplex, "Initialize");

        // Seed KnowledgeGraph field with the hand-crafted pattern sized to the mesh
        var sc = GetNodeOrNull<SimplicialComplexNode>(SimplicialComplexPath);
        if (sc != null && _knowledge != null)
            _knowledge.Resize(sc.VertexCount);

        // Initialise cognitive layers
        _knowledge?.Initialize();
        _emotion?.Initialize();
        _intent?.Initialize();
        _action?.Initialize();

        if (_maxwell != null)
            InvokeNodeMethod(_maxwell, "Start", 0.0d);
        if (_gr != null)
            InvokeNodeMethod(_gr, "Start", 0.0d);
        if (_unifiedEngine != null)
            InvokeNodeMethod(_unifiedEngine, "Start", 0.0d);
    }

    public void PauseSimulation()  => RunSimulation = false;
    public void ResumeSimulation() => RunSimulation = true;

    private static void InvokeNodeMethod(Node? node, string methodName, double delta)
    {
        if (node == null || !node.HasMethod(methodName)) return;
        node.Call(methodName, delta);
    }

    private static void InvokeNodeMethodNoArgs(Node? node, string methodName)
    {
        if (node == null || !node.HasMethod(methodName)) return;
        node.Call(methodName);
    }

    // ── debug HUD ─────────────────────────────────────────────────────────────

    private MagicFieldEngine? _magic;      // cached on first use
    private NLCASNode?        _nlcasHUD;   // cached on first use
    private UnifiedEngine?    _unifiedEngineHUD;   // cached for HUD diagnostics
    private PlayerHElement?   _playerHUD;  // cached on first use

    private void UpdateDebugHUD()
    {
        if (_debugLabel == null) return;

        // Cache references once
        _magic            ??= GetNodeOrNull<MagicFieldEngine>("World/MagicFieldEngine");
        _nlcasHUD         ??= GetNodeOrNull<NLCASNode>("World/NLCASNode");
        _unifiedEngineHUD ??= GetNodeOrNull<UnifiedEngine>("World/UnifiedEngine");
        _playerHUD        ??= GetNodeOrNull<PlayerHElement>(PlayerHElementPath);

        float  torsion   = _magic  != null ? _magic.TotalTorsion()     : 0f;
        string axis      = _magic  != null ? _magic.DominantDichotomy(): "n/a";
        float  potential = _magic  != null ? _magic.ComputePotential()  : 0f;
        float  arousal   = _emotion != null ? _emotion.Arousal          : 0f;
        float  urgency   = _intent  != null ? _intent.Urgency           : 0f;
        int    actions   = _action  != null ? _action.Inputs.Count      : 0;
        float  avgNorm   = _nlcasHUD != null ? _nlcasHUD.GetAvgTokenNorm() : 0f;

        // HAM gate diagnostics — shows how much of the NLCAS write-back the manifold let through
        float  hamGate        = _unifiedEngineHUD != null ? _unifiedEngineHUD.LastHAMGate       : 0f;
        float  effectiveAlpha = _unifiedEngineHUD != null ? _unifiedEngineHUD.LastEffectiveAlpha : 0f;

        // Player manifold diagnostics — live player cognitive state + throttle gate
        float  playerTorsion  = _playerHUD != null ? _playerHUD.TotalTorsion  : 0f;
        float  playerGate     = _unifiedEngineHUD != null ? _unifiedEngineHUD.LastPlayerGate    : 1f;
        string playerAxis     = _playerHUD != null ? _playerHUD.DominantAxis  : "n/a";

        // Stability indicator — if torsion or potential spike, manifold is not ready for NLCAS
        string stability = (System.Math.Abs(torsion) < 1f && potential < 10f) ? "STABLE" : "UNSTABLE";

        _debugLabel.Text =
            $"[MANIFOLD {stability}]\n"                     +
            $"Torsion:      {torsion:F4}\n"                 +
            $"Potential:    {potential:F4}\n"               +
            $"Axis:         {axis}\n"                       +
            $"Arousal:      {arousal:F4}\n"                 +
            $"Urgency:      {urgency:F4}\n"                 +
            $"Actions:      {actions}\n"                    +
            $"AvgTokenNorm: {avgNorm:F3}\n"                 +
            $"HAMGate:      {hamGate:F3}\n"                 +
            $"EffAlpha:     {effectiveAlpha:F4}\n"          +
            $"[PLAYER]\n"                                   +
            $"P.Torsion:    {playerTorsion:F4}\n"           +
            $"P.Axis:       {playerAxis}\n"                 +
            $"P.Gate:       {playerGate:F3}";
    }
}
