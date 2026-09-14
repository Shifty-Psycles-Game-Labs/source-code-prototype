using Godot;

/// <summary>
/// GR_Operator — depth-throttled General Relativity harmonic operator.
///
/// Wraps the per-tick curvature sampling and harmonic-2-form smoothing that
/// GRHarmonicEngine applies to its scalar field.  ManifoldRegulator writes
/// GRDepth into this operator each frame via ApplyDepth(); the operator then
/// scales all curvature output and smooths out divergence at shallow depth.
///
/// Design (NEM §GR coupling):
///   • High tension  → GRDepth near 0.1 → geometry is nearly frozen
///   • Low tension   → GRDepth near 1.0 → full curvature expression
///
/// GRHarmonicEngine holds one instance as a field; it calls ApplyDepth() from
/// UnifiedEngine after ManifoldRegulator.Apply() and then uses this operator's
/// two public methods when sampling curvature for cognition injection.
/// </summary>
public class GR_Operator
{
    /// <summary>
    /// Current recursion-depth scalar ∈ [0.1, 1].
    /// Set by ManifoldRegulator via ApplyDepth() each physics tick.
    /// </summary>
    public float CurrentDepth { get; private set; } = 1f;

    /// <summary>
    /// Update the recursion depth from ManifoldRegulator.GRDepth.
    /// Called once per physics tick by GRHarmonicEngine before any sampling.
    /// </summary>
    public void ApplyDepth(float depth)
    {
        CurrentDepth = Mathf.Clamp(depth, 0.1f, 1f);
    }

    /// <summary>
    /// Sample curvature at a vertex position and scale by current depth.
    ///
    /// High depth (relaxed manifold) → full curvature amplitude.
    /// Low depth (tense manifold)    → amplitude suppressed toward zero.
    ///
    /// The base curvature is provided by the caller (GRHarmonicEngine passes
    /// the current T[v] scalar field value); this operator only applies depth.
    /// </summary>
    /// <param name="baseCurvature">Raw curvature scalar from GRHarmonicEngine._T[v].</param>
    public float ComputeCurvature(float baseCurvature)
    {
        return baseCurvature * CurrentDepth;
    }

    /// <summary>
    /// Apply harmonic smoothing to a curvature value.
    ///
    /// Lerps the curvature toward zero proportionally to (1 − CurrentDepth).
    ///   depth=1   → curvature unchanged (full expression)
    ///   depth=0.1 → curvature lerped 90% toward 0 (near-frozen geometry)
    ///
    /// Prevents blow-ups when the manifold is tense: shallow depth not only
    /// reduces amplitude but also damps residual fluctuations.
    /// </summary>
    /// <param name="curvature">Scaled curvature from ComputeCurvature().</param>
    public float ApplyHarmonic2Form(float curvature)
    {
        return Mathf.Lerp(curvature, 0f, 1f - CurrentDepth);
    }

    /// <summary>
    /// Convenience: compute and smooth in one call.
    /// Equivalent to ApplyHarmonic2Form(ComputeCurvature(baseCurvature)).
    /// </summary>
    public float Apply(float baseCurvature) =>
        ApplyHarmonic2Form(ComputeCurvature(baseCurvature));
}
