using Godot;

/// <summary>
/// Regulator — ψ-dissipation + AT-regulator + clamp stack for the NEM-U cognitive manifold.
///
/// Implements the Cybernetic 3.0 loop from WhyNEM:
///
///   1.  ψ-dissipation   — drain field magnitudes each frame.
///   2.  AT regulator    — harmonic transform that clamps recursive growth:
///                           AT = clamp(1 − torsion/maxTorsion, 0, 1)
///                           arousal  *= AT
///                           urgency  *= AT
///                           HAMGate  *= AT
///   3.  EffAlpha clamp  — learning rate stays within [0.01, 0.3].
///   4.  Idle flow gate  — after IdleTimeout with no player input, all fields
///                         decay toward zero (natural dissipation).
///   5.  Global clamps   — NaN guard + hard ceiling on torsion/potential.
///
/// Attach this node as a sibling/child of MagicFieldEngine and set the
/// MagicPath export so it can pull and push manifold scalars each frame.
/// Call RegisterInput() (or wire it via UnifiedEngine) whenever the player
/// produces input so the idle timer resets correctly.
/// </summary>
public partial class Regulator : Node
{
    // ── node wiring ────────────────────────────────────────────────────────────
    /// <summary>NodePath to the MagicFieldEngine this regulator governs.</summary>
    [Export] public NodePath MagicPath { get; set; } = "../MagicFieldEngine";

    // ── ψ dissipative field ────────────────────────────────────────────────────
    /// <summary>
    /// ψ — dissipative harmonic field strength. Drains field magnitudes
    /// each frame. Range [0.01, 0.2]: higher = calmer manifold, lower = chaotic.
    /// </summary>
    [Export] public float Psi { get; set; } = 0.05f;

    // ── AT regulator ───────────────────────────────────────────────────────────
    [Export] public float MaxTorsion  { get; set; } = 1000f;
    [Export] public float MaxPotential{ get; set; } = 1000f;

    // ── idle flow gate ─────────────────────────────────────────────────────────
    [Export] public float IdleTimeout         { get; set; } = 1.0f;
    [Export] public float IdleTorsionDecay    { get; set; } = 0.95f;
    [Export] public float IdlePotentialDecay  { get; set; } = 0.95f;
    [Export] public float IdleArousalDecay    { get; set; } = 0.9f;
    [Export] public float IdleUrgencyDecay    { get; set; } = 0.9f;

    // ── diagnostics (read from inspector / UniverseRoot HUD / PsiDebug) ───────
    public float LastAT          { get; private set; } = 1f;
    public float LastHAMGate     { get; private set; } = 0f;
    public float LastEffAlpha    { get; private set; } = 0f;
    public bool  IsIdle          { get; private set; } = false;

    /// <summary>Current manifold torsion scalar (TotalTorsion from HAM). Updated each _Process tick.</summary>
    public float Torsion         { get; private set; } = 0f;
    /// <summary>Current manifold potential scalar (ComputePotential from MagicFieldEngine). Updated each _Process tick.</summary>
    public float Potential       { get; private set; } = 0f;

    // ── internal state ─────────────────────────────────────────────────────────
    private MagicFieldEngine? _magic;
    private float             _idleTimer  = 0f;
    private bool              _hasInput   = false;

    // ── lifecycle ──────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _magic = GetNodeOrNull<MagicFieldEngine>(MagicPath);
        if (_magic == null)
            GD.PushWarning("Regulator: MagicFieldEngine not found at '" + MagicPath + "'.");
    }

    // ── input registration ─────────────────────────────────────────────────────

    /// <summary>
    /// Call once per frame whenever the player has produced any input.
    /// Resets the idle timer and suppresses the flow-gate decay for this tick.
    /// Intended to be called from UnifiedEngine._PhysicsProcess when the action
    /// vector is non-zero.
    /// </summary>
    public void RegisterInput()
    {
        _hasInput  = true;
        _idleTimer = 0f;
    }

    // ── main loop ──────────────────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        if (_magic == null) return;
        float dt = (float)delta;

        // Torsion is derived from field phases — read it first so AT is
        // computed from the state *before* this frame's dissipation alters it.
        float torsion   = _magic.TotalTorsion();
        float potential = _magic.ComputePotential();

        // Cache as public properties so PsiDebug / HUD can read them.
        Torsion   = torsion;
        Potential = potential;

        // 1+2. ψ-dissipation and AT-gating in a single pass over _H.
        //      Computing AT from the pre-dissipation torsion is intentional:
        //      the gate reflects the rotational phase entering this frame,
        //      not after it's been partially drained.
        float at = ComputeAT(torsion);
        LastAT   = at;
        _magic.ApplyPsiAndAT(Psi, at, dt);

        // 3. EffAlpha clamp (LambdaStep is the effective learning rate)
        float clamped = Mathf.Clamp(_magic.LambdaStep, 0.01f, 0.3f);
        _magic.LambdaStep = clamped;
        LastEffAlpha      = clamped;

        // 4. Idle flow gate
        UpdateIdle(dt);

        // 5. Global NaN guard
        _magic.SanitizeFields();

        // Reset flag for next frame
        _hasInput = false;
    }

    // ── private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// AT = clamp(1 − |torsion|/maxTorsion, 0, 1).
    /// AT near 1 → relaxed manifold, full recursion allowed.
    /// AT near 0 → manifold at peak rotational phase, recursion clamped out.
    ///
    /// |torsion| is used because torsion is signed (oscillatory); the gate
    /// responds to amplitude of rotation, not polarity.
    ///
    /// Public so NPC systems can query the current gate value without
    /// going through a full Regulator tick.
    /// </summary>
    public float ComputeAT() => ComputeAT(Torsion);

    private float ComputeAT(float torsion)
    {
        float norm = (MaxTorsion > 0f) ? System.Math.Abs(torsion) / MaxTorsion : 0f;
        return Mathf.Clamp(1.0f - norm, 0.0f, 1.0f);
    }

    // ── preset modes ──────────────────────────────────────────────────────────

    /// <summary>
    /// Calm mode: high ψ drains fields quickly, low ceiling keeps torsion bounded.
    /// Good for exploration, dialogue, idle environments.
    /// </summary>
    public void SetCalmMode()
    {
        Psi        = 0.15f;
        MaxTorsion = 300f;
    }

    /// <summary>
    /// Chaos mode: low ψ lets torsion build, high ceiling allows extreme dynamics.
    /// Good for combat, high-magic zones, stress-test scenarios.
    /// </summary>
    public void SetChaosMode()
    {
        Psi        = 0.02f;
        MaxTorsion = 2000f;
    }

    /// <summary>
    /// Idle flow gate: once the idle timer crosses IdleTimeout, all fields
    /// are gently decayed toward zero (multiplicative, so they asymptotically
    /// approach but never suddenly drop).
    /// </summary>
    private void UpdateIdle(float dt)
    {
        if (_hasInput)
        {
            IsIdle = false;
            return;
        }

        _idleTimer += dt;
        IsIdle = _idleTimer >= IdleTimeout;

        if (IsIdle)
            _magic!.ApplyIdleDecay(IdleTorsionDecay, IdlePotentialDecay,
                                   IdleArousalDecay, IdleUrgencyDecay);
    }
}
