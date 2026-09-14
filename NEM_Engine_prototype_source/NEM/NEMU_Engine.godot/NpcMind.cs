using Godot;

/// <summary>
/// NpcMind — cognitive H-element node for a single NPC on M₄.
///
/// Combines two layers:
///
/// Layer 1 — Regulator coupling (unchanged from original):
///   Applies AT (regulator operator) to Focus, Curiosity, Anxiety each frame.
///   When the manifold is saturated (AT → 0) the NPC narrows to Focus;
///   when calm (AT → 1) all three fields evolve freely.
///
/// Layer 2 — H-element K/E/I/A (new):
///   Carries its own KnowledgeGraph, EmotionState, IntentState, ActionAccumulator —
///   the same four sub-objects as PlayerHElement, but evolved under NPC logic
///   and boundary-conditioned by the player's manifold snapshot.
///
///   NPC fields are NOT independently ticked — they are driven by:
///     • EvolveSelf(dt)     — autonomous per-NPC dynamics (called by manager/AI)
///     • ReceivePlayerBoundary(snapshot, alpha, relationship) — gestalt coupling
///       (called by RelationshipCoupler.CoupleNpcToPlayer each frame)
///
/// Boundary-condition mechanism (NEM §NPC coupling):
///   NPC Emotion drifts toward player Emotion (affective mirroring, modulated by α)
///   NPC Intent  aligns or anti-aligns with player Intent (depends on relationship sign)
///   NPC Knowledge absorbs player-revealed facts (information diffusion)
///   NPC Action gate (Focus) clamps under high manifold torsion
///
/// Relationship sign (+1 / −1):
///   +1 = ally/neutral  → NPC intent aligns    with player intent
///   −1 = enemy/hostile → NPC intent anti-aligns (opposition)
/// </summary>
public partial class NpcMind : Node, IHElement
{
    // ── AT coupling (Layer 1 — unchanged) ─────────────────────────────────────
    /// <summary>
    /// Path to the shared Regulator node.  When set, AT is read and applied
    /// automatically in _Process each frame.  Leave empty to drive manually.
    /// </summary>
    [Export] public NodePath RegulatorPath { get; set; } = "";

    // ── cognitive state scalars (Layer 1) ─────────────────────────────────────

    /// <summary>
    /// Directed concentration on current goal.
    /// Increases under stress (low AT) as the mind narrows to survive.
    /// Clamp: [0, 10]
    /// </summary>
    [Export] public float Focus     { get; set; } = 1.0f;

    /// <summary>
    /// Exploratory drive — reduced when the manifold is saturated (low AT).
    /// Clamp: [0, 10]
    /// </summary>
    [Export] public float Curiosity { get; set; } = 1.0f;

    /// <summary>
    /// Threat-response arousal — suppressed by AT.
    /// High anxiety in a calm manifold is allowed; in a saturated manifold
    /// it is quenched along with all other unstable components.
    /// Clamp: [0, 10]
    /// </summary>
    [Export] public float Anxiety   { get; set; } = 0.0f;

    // ── tuning ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// How much Focus amplifies under stress.
    /// Focus *= 1 + (1 − AT) * FocusStressGain.
    /// Default 0.5 → Focus can grow up to 1.5× at full saturation.
    /// </summary>
    [Export] public float FocusStressGain { get; set; } = 0.5f;

    // ── H-element fields (Layer 2 — new) ──────────────────────────────────────

    /// <summary>
    /// Coupling strength for player boundary injection.
    ///   0 = fully independent NPC (no player influence)
    ///   1 = fully absorbed (NPC mirrors player)
    /// Default 0.15: NPC is lightly influenced but keeps its own trajectory.
    /// </summary>
    [Export] public float PlayerCouplingAlpha { get; set; } = 0.15f;

    /// <summary>
    /// Relationship polarity with the player.
    ///   +1 = ally / neutral  → intent aligns
    ///   −1 = hostile / enemy → intent anti-aligns
    /// </summary>
    [Export] public float RelationshipSign { get; set; } = 1.0f;

    /// <summary>NPC knowledge field.</summary>
    public KnowledgeGraph    T_K { get; private set; } = null!;
    /// <summary>NPC emotion field.</summary>
    public EmotionState      T_E { get; private set; } = null!;
    /// <summary>NPC intent field.</summary>
    public IntentState       T_I { get; private set; } = null!;
    /// <summary>NPC action accumulator (driven by AI, not raw input).</summary>
    public ActionAccumulator T_A { get; private set; } = null!;

    /// <summary>Scalar HAM over the NPC's four cognitive fields.</summary>
    public HarmonicAttentionMatrix Attention { get; private set; } = null!;

    // ── IHElement ──────────────────────────────────────────────────────────────
    /// <summary>Name of the dominant dichotomy axis after the last Evolve call.</summary>
    public string DominantAxis { get; private set; } = "n/a";
    /// <summary>Total HAM torsion scalar after the last Evolve call.</summary>
    public float  TotalTorsion { get; private set; } = 0f;

    // ── diagnostics ────────────────────────────────────────────────────────────
    /// <summary>AT value used on the last ApplyRegulator() call.  Read-only.</summary>
    public float LastAT { get; private set; } = 1f;

    // ── internal ───────────────────────────────────────────────────────────────
    private Regulator? _regulator;

    // ── lifecycle ──────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        if (RegulatorPath != null && !RegulatorPath.IsEmpty)
            _regulator = GetNodeOrNull<Regulator>(RegulatorPath);

        T_K       = new KnowledgeGraph();
        T_E       = new EmotionState();
        T_I       = new IntentState();
        T_A       = new ActionAccumulator();
        Attention = new HarmonicAttentionMatrix();
    }

    public override void _Process(double delta)
    {
        // Layer 1: apply AT gate from shared Regulator (original behaviour unchanged)
        if (_regulator != null)
            ApplyRegulator(_regulator.ComputeAT());

        // Layer 2: tick autonomous NPC H-element dynamics
        EvolveSelf(delta);
    }

    // ── Layer 1 public API (unchanged) ─────────────────────────────────────────

    /// <summary>
    /// Apply the AT gate to this NPC's cognitive state.
    ///
    ///   Curiosity *= AT                         — exploratory drive contracts under saturation
    ///   Anxiety   *= AT                         — threat-response quenched under saturation
    ///   Focus     *= 1 + (1 − AT) * StressGain  — focus sharpens under stress
    ///
    /// Values are clamped to [0, 10] after update.
    /// </summary>
    public void ApplyRegulator(float at)
    {
        LastAT = at;

        Curiosity *= at;
        Anxiety   *= at;
        Focus     *= 1.0f + (1.0f - at) * FocusStressGain;

        Focus     = Mathf.Clamp(Focus,     0f, 10f);
        Curiosity = Mathf.Clamp(Curiosity, 0f, 10f);
        Anxiety   = Mathf.Clamp(Anxiety,   0f, 10f);
    }

    // ── Layer 2 public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Tick autonomous NPC H-element dynamics (internal).
    ///
    /// Called once per _Process frame — runs the same K/E/I pipeline as
    /// PlayerHElement but without player input.  T_A is not driven from input;
    /// it is driven externally by AI (see SetActivityRates()).
    ///
    /// After stepping, recomputes the HAM and caches DominantAxis / TotalTorsion.
    /// </summary>
    public void EvolveSelf(double delta)
    {
        // Intent evolves from accumulated NPC action (set by AI each frame)
        T_I.UpdateFromQuestsAndActions(T_A, delta);

        // Emotion evolves from NPC's own knowledge and intent state
        T_E.UpdateFromContext(T_K, T_I, delta);

        // Knowledge mastery gated by NPC's own E/I
        T_K.UpdateGated(T_E, T_I, delta);

        // Recompute HAM — order matches DichotomyOperator indices
        Attention.RecomputeScalar(
            new float[] { T_I.Magnitude, T_K.Magnitude, T_E.Magnitude, T_A.Magnitude },
            new float[] { T_I.Phase,     T_K.Phase,     T_E.Phase,     T_A.Phase     }
        );

        DominantAxis = Attention.DominantAxisName();
        TotalTorsion = Attention.TotalTorsion();
    }

    /// <summary>
    /// Inject player manifold boundary conditions into the NPC's K/E/I fields.
    ///
    /// Called by RelationshipCoupler.CoupleNpcToPlayer() each physics tick.
    ///
    /// Coupling rules (all lerp toward player boundary at strength α · RelationshipSign):
    ///
    ///   E.Arousal     → lerp toward player.EArousal      (affective mirroring)
    ///   E.Valence     → lerp toward player.EValence · sign  (ally resonates, enemy repels)
    ///   E.Uncertainty → lerp toward player.EUncertainty  (shared disorientation)
    ///   I.IntentDirection → lerp toward player.IIntentDirection · sign
    ///                        (ally aligns, enemy opposes)
    ///   I.Commitment  → nudged upward if player torsion is high (urgency contagion)
    ///   Focus (L1)    → scaled by manifold torsion gate
    ///
    /// Knowledge injection is done via AddKnowledge() — caller supplies
    /// the revealed fact ID; the NPC gains familiarity automatically.
    /// </summary>
    /// <param name="player">Snapshot of the player's manifold state this tick.</param>
    /// <param name="alpha">Coupling strength override — if ≤ 0 uses PlayerCouplingAlpha.</param>
    public void ReceivePlayerBoundary(in HManifoldSnapshot player, float alpha = -1f)
    {
        float a    = (alpha > 0f ? alpha : PlayerCouplingAlpha);
        float sign = Mathf.Sign(RelationshipSign);   // +1 or −1

        // ── Emotion boundary ─────────────────────────────────────────────────
        // Affective mirroring: NPC arousal and uncertainty follow the player
        // regardless of relationship; valence is flipped for enemies.
        T_E.Arousal      = Mathf.Lerp(T_E.Arousal,      player.EArousal,           a);
        T_E.Valence      = Mathf.Lerp(T_E.Valence,      player.EValence * sign,    a);
        T_E.Uncertainty  = Mathf.Lerp(T_E.Uncertainty,  player.EUncertainty,       a);

        // ── Intent boundary ──────────────────────────────────────────────────
        // Allies share intent direction; enemies oppose it.
        T_I.IntentDirection = Mathf.Lerp(
            T_I.IntentDirection,
            player.IIntentDirection * sign,
            a);

        // High player torsion makes even low-commitment NPCs more reactive
        float torsionUrgency = Mathf.Clamp(System.Math.Abs(player.TotalTorsion) * 0.05f, 0f, 0.1f);
        T_I.Commitment = Mathf.Clamp(T_I.Commitment + torsionUrgency * a, 0f, 1f);

        // ── Action gate (Layer 1 bridge) ─────────────────────────────────────
        // High manifold torsion from the player clamps NPC Focus (like AT does
        // from the Regulator, but driven by the *player's* cognitive load).
        float tensionGate = 1f / (1f + System.Math.Abs(player.TotalTorsion) * 0.1f);
        Focus = Mathf.Clamp(Focus * (1f + (1f - tensionGate) * FocusStressGain), 0f, 10f);
    }

    /// <summary>
    /// Apply manifold-driven behaviour modulation to this NPC's H-element.
    ///
    /// Called by UnifiedEngine (or an NPC manager) after ManifoldRegulator.Apply()
    /// and CoupleNpcToPlayer() so the NPC sees the fully-regulated manifold state.
    ///
    /// Modulation rules:
    ///   1. Identify the strongest attention channel from the NPC's own HAM.
    ///   2. Drift T_E.Valence toward the player using att × 0.1 × cognitiveGain.
    ///   3. On Intent-dominant axis: push T_I.IntentDirection by att × 0.05 × gain.
    ///   4. Scale T_A activity rates via T_A.ModulateAI(cognitiveGain, att).
    ///
    /// This is "true harmonic cognition" — NPC behaviour is a continuous function
    /// of manifold torsion, dominant axis, and attention weight, not a state machine.
    /// </summary>
    /// <param name="cognitiveGain">CognitiveGain from ManifoldRegulator (∈ [0.1,1]).</param>
    /// <param name="dominantAxisIndex">DichotomyOperator.INTENT/KNOWLEDGE/EMOTION/ACTION.</param>
    public void ApplyBehaviourModulation(float cognitiveGain, int dominantAxisIndex)
    {
        // Strongest attention channel from the NPC's own HAM
        var (di, _) = Attention.DominantAxis();
        float att   = 0f;
        if (di >= 0)
        {
            float[] row = Attention.AttentionRow(di);
            foreach (float w in row) if (w > att) att = w;
        }

        // 1. Emotional drift toward the player's influence (valence modulation)
        T_E.Valence = Mathf.Clamp(T_E.Valence + att * 0.1f * cognitiveGain, -1f, 1f);

        // 2. Intent alignment on Intent-dominant axis
        if (dominantAxisIndex == DichotomyOperator.INTENT)
            T_I.IntentDirection = Mathf.Clamp(
                T_I.IntentDirection + att * 0.05f * cognitiveGain * Mathf.Sign(RelationshipSign),
                -1f, 1f);

        // 3. Action modulation — scales rates and adds attention-driven reactivity
        T_A.ModulateAI(cognitiveGain, att);
    }

    /// <summary>
    /// Drive the NPC's ActionAccumulator rates from AI logic.
    /// Call this from your NPC AI or behaviour tree each frame before EvolveSelf.
    /// </summary>
    public void SetActivityRates(float move, float look, float interact, float idle)
    {
        T_A.MoveRate        = Mathf.Max(0f, move);
        T_A.LookRate        = Mathf.Max(0f, look);
        T_A.InteractionRate = Mathf.Max(0f, interact);
        T_A.IdleRate        = Mathf.Max(0f, idle);
    }

    // ── IHElement ──────────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot of the NPC's current manifold state.</summary>
    public HManifoldSnapshot GetSnapshot() => new HManifoldSnapshot
    {
        KMagnitude        = T_K.Magnitude,
        KPhase            = T_K.Phase,
        EMagnitude        = T_E.Magnitude,
        EPhase            = T_E.Phase,
        EValence          = T_E.Valence,
        EArousal          = T_E.Arousal,
        EUncertainty      = T_E.Uncertainty,
        IMagnitude        = T_I.Magnitude,
        IPhase            = T_I.Phase,
        IIntentDirection  = T_I.IntentDirection,
        ICommitment       = T_I.Commitment,
        AMagnitude        = T_A.Magnitude,
        APhase            = T_A.Phase,
        DominantAxis      = DominantAxis,
        TotalTorsion      = TotalTorsion,
    };
}
