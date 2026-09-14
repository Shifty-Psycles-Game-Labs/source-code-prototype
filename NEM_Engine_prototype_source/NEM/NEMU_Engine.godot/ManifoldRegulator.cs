using Godot;

/// <summary>
/// ManifoldRegulator — operator-level regulator for GR/Magic recursion depth
/// and NPC cognitive gain (OM4 §"Operations manifold acting on cognitive bundles").
///
/// This is distinct from Regulator.cs, which handles field-level ψ-dissipation
/// and AT clamping.  ManifoldRegulator operates one layer higher: it reads the
/// HarmonicAttentionMatrix and player torsion to set three *operator* scalars
/// that gate how deeply the physics engines and NPC minds recurse each tick.
///
/// Grounding (H4.md):
///   "Emotion functions as the Debuff… Intent to mitigate the effect of
///    Emotion on k." — the regulator expresses exactly this: emotional
///    (Emotion-dominant) torsion debuffs CognitiveGain; intent-dominant
///    torsion throttles GR depth; action-dominant torsion clamps both.
///
/// Output scalars (read by UnifiedEngine each tick):
///   GRDepth      ∈ [0.1, 1]  — scales GR recursion / source-term injection
///   MagicDepth   ∈ [0.2, 1]  — scales MagicFieldEngine LambdaStep
///   CognitiveGain∈ [0.1, 1]  — scales NPC PlayerCouplingAlpha (volatility)
///
/// All three are derived nonlinearly from TotalTorsion via 1/(1+k|τ|) so the
/// manifold is always recoverable (never fully zeroed).  Axis-specific biases
/// then shift the balance depending on which dichotomy dominates.
/// </summary>
public class ManifoldRegulator
{
    // ── outputs (read by UnifiedEngine after Apply()) ──────────────────────────

    /// <summary>
    /// GR recursion depth scalar ∈ [0.1, 1].
    /// UnifiedEngine multiplies GrToCogStrength and EmToGrStrength by this value.
    /// High torsion → low GRDepth → shallower curvature recursion.
    /// </summary>
    public float GRDepth       { get; private set; } = 1f;

    /// <summary>
    /// Magic gradient-flow depth scalar ∈ [0.2, 1].
    /// UnifiedEngine multiplies MagicFieldEngine.LambdaStep by this value.
    /// High torsion → small LambdaStep → slower field evolution.
    /// </summary>
    public float MagicDepth    { get; private set; } = 1f;

    /// <summary>
    /// NPC cognitive volatility scalar ∈ [0.1, 1].
    /// UnifiedEngine scales each NPC's effective PlayerCouplingAlpha by this.
    /// High torsion → low gain → NPCs resist player influence (calm preservation).
    /// </summary>
    public float CognitiveGain { get; private set; } = 1f;

    // ── tuning constants ───────────────────────────────────────────────────────
    // Denominator gain factors — higher = stronger sensitivity to torsion.
    // GR is most sensitive (physics), Magic less so, Cognition least.
    private const float KGR    = 0.10f;
    private const float KMagic = 0.05f;
    private const float KCog   = 0.02f;

    // ── Apply ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recompute all three operator scalars from the current HAM and player torsion.
    ///
    /// Called once per physics tick from UnifiedEngine step 6b, replacing the
    /// previous ApplyPlayerManifoldGate + ApplyHAMFeedback pair.
    ///
    /// Steps:
    ///   1. Base depth from total torsion via 1/(1+k|τ|) — smooth, always positive.
    ///   2. Axis-specific bias applied multiplicatively to the relevant scalar.
    ///   3. Hard clamp to output range.
    /// </summary>
    /// <param name="ham">The player's HarmonicAttentionMatrix (from PlayerHElement).</param>
    /// <param name="playerTorsion">TotalTorsion from the player's HAM (PlayerHElement.TotalTorsion).</param>
    public void Apply(HarmonicAttentionMatrix ham, float playerTorsion)
    {
        float T = System.Math.Abs(playerTorsion);  // torsion is signed; gate on amplitude

        // ── 1. Base scalars from total torsion ────────────────────────────────
        GRDepth       = Mathf.Clamp(1f / (1f + T * KGR),    0.1f, 1f);
        MagicDepth    = Mathf.Clamp(1f / (1f + T * KMagic), 0.2f, 1f);
        CognitiveGain = Mathf.Clamp(1f - T * KCog,          0.1f, 1f);

        // ── 2. Axis-specific bias ─────────────────────────────────────────────
        //    Read the dominant axis from the HAM using DichotomyOperator constants.
        var (di, _) = ham.DominantAxis();
        if (di >= 0)
        {
            switch (di)
                {
                    case DichotomyOperator.KNOWLEDGE:
                        // Knowledge-dominant: semantic overload → dampen magic (too much recursion
                        // over known structure destabilises the gradient flow).
                        MagicDepth *= 0.8f;
                        break;
    
                    case DichotomyOperator.EMOTION:
                        // Emotion-dominant (Debuff axis per H4.md): high affective torsion
                        // reduces NPC responsiveness — they need to process internally first.
                        CognitiveGain *= 0.7f;
                        break;
    
                    case DichotomyOperator.INTENT:
                        // Intent-dominant: strong goal pressure torsion stresses GR curvature field
                        // (intent is the "global pull" — over-tuning GR here causes runaway).
                        GRDepth *= 0.9f;
                        break;
    
                    case DichotomyOperator.ACTION:
                        // Action-dominant: high activity torsion stresses both physics operators.
                        MagicDepth *= 0.9f;
                        GRDepth    *= 0.9f;
                        break;
                }
        }

        // ── 3. Hard clamp (axis bias can push below base range) ───────────────
        GRDepth       = Mathf.Clamp(GRDepth,       0.1f, 1f);
        MagicDepth    = Mathf.Clamp(MagicDepth,    0.1f, 1f);
        CognitiveGain = Mathf.Clamp(CognitiveGain, 0.1f, 1f);
    }
}
