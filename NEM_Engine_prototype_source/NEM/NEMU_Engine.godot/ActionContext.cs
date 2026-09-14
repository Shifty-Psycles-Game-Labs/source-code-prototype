using Godot;

// ── ActionContext ─────────────────────────────────────────────────────────────
/// <summary>
/// ActionContext — the H4 player-action binding struct.
///
/// Maps the NEMU metaphysics chain directly to per-frame gameplay state:
///
///   Belief( Emotion ^ (Intent * ActionMeta) )
///       Knowledge → output
///       Insanity  → consequence of sustained casting
///
/// Layout mirrors the frC₄ consequence bundle described in H4.nmd:
///   Emotion    — T_E valence/arousal proxy (debuff axis)
///   Intent     — T_I goal-drive pressure
///   ActionMeta — discrete action mode: NONE=66, MOVE=67, CAST=68, MOVE+CAST=69
///   Belief     — derived: Emotion^(Intent*ActionMeta), clipped to [0,1]
///   Knowledge  — T_K semantic coherence passthrough
///   Insanity   — accumulated magic hold-time consequence
///   Location   — WorldSpace origin this tick (primary context axis)
///
/// ActionMeta uses the four-mode dichotomy from H4.nmd §"NONE, MOVE, INTR, LOOK"
/// so Action is always a clean orthogonal classification, never a raw scalar.
/// </summary>
public struct ActionContext
{
    // ── OM4 cognitive proxies ─────────────────────────────────────────────────
    /// <summary>Affective load proxy: T_E.ValenceDebuff() mirror.</summary>
    public float Emotion;

    /// <summary>Goal-drive pressure: T_I.IntentBoost() mirror.</summary>
    public float Intent;

    // ── Action classification (discrete, H4 §A_t^T) ──────────────────────────
    /// <summary>NONE (no input, the void) = 66.</summary>
    public const int ACTION_NONE = 66;
    /// <summary>MOVE (spatial displacement, two consecutive frames).</summary>
    public const int ACTION_MOVE = 67;
    /// <summary>CAST / INTERACT (magic / interact action held).</summary>
    public const int ACTION_CAST = 68;
    /// <summary>MOVE + CAST simultaneously (compound action).</summary>
    public const int ACTION_MOVE_CAST = 69;

    /// <summary>
    /// Discrete action classification this tick.
    /// One of ACTION_NONE / ACTION_MOVE / ACTION_CAST / ACTION_MOVE_CAST.
    /// </summary>
    public int ActionMeta;

    // ── Consequence fields (frC₄) ─────────────────────────────────────────────
    /// <summary>
    /// Belief — emergent conviction derived from the metaphysics chain:
    ///   Belief = clamp( Emotion ^ (Intent * ActionMeta_norm), 0, 1 )
    /// where ActionMeta_norm maps the four discrete modes to [0, 1].
    /// </summary>
    public float Belief;

    /// <summary>Semantic coherence (T_K.Phase passthrough).</summary>
    public float Knowledge;

    /// <summary>
    /// Insanity — accumulated magic hold-time consequence.
    /// Grows at InsanityRate while casting; decays when idle.
    /// Triggers warning at &gt;= InsanityWarningThreshold.
    /// </summary>
    public float Insanity;

    // ── Primary context axis ──────────────────────────────────────────────────
    /// <summary>World-space position this tick. Everything else derives from here.</summary>
    public Vector3 Location;

    // ── Derived ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Recompute Belief from the current Emotion / Intent / ActionMeta values.
    /// Called by PlayerController after each field update.
    ///
    ///   ActionMeta_norm: NONE=0.00, MOVE=0.33, CAST=0.67, MOVE+CAST=1.00
    ///   base  = Intent * ActionMeta_norm
    ///   Belief = base^Emotion   (Emotion acts as the exponent — the debuff/amplifier)
    ///   Clamped to [0, 1].
    /// </summary>
    public void RecomputeBelief()
    {
        float actionNorm = ActionMeta switch
        {
            ACTION_NONE      => 0.00f,
            ACTION_MOVE      => 0.33f,
            ACTION_CAST      => 0.67f,
            ACTION_MOVE_CAST => 1.00f,
            _                => 0.00f
        };

        float baseVal = Intent * actionNorm;
        // Emotion as exponent: positive emotion amplifies, negative debuffs via reciprocal.
        float exponent = Emotion >= 0f
            ? 1f + Emotion         // joy/approach → amplifier > 1
            : 1f / (1f - Emotion); // fear/aversion → exponent < 1, squash
        Belief = Mathf.Clamp(Mathf.Pow(Mathf.Max(0f, baseVal), exponent), 0f, 1f);
    }
}
