using Godot;

/// <summary>
/// MagicFieldOperator — depth-throttled p-Laplacian magic operator.
///
/// Wraps the nonlinear flux computation inside MagicFieldEngine.  The operator
/// receives MagicDepth from ManifoldRegulator and adjusts two things:
///
///   1. Flux amplitude — scaled by CurrentDepth (same as GR_Operator).
///   2. p-Laplacian exponent — p interpolates from 2 (linear/safe, high tension)
///      to 6 (highly nonlinear/chaotic, low tension) as depth increases.
///
/// At p=2 the operator is the standard harmonic gradient flow (safe, stable).
/// At p=6 it becomes a strongly nonlinear p-Laplacian (expressive, chaotic).
/// This maps exactly onto the Emotion-as-Debuff / Intent-as-Boost semantics:
///   calm manifold → high p → magic is creative and powerful
///   stressed manifold → low p → magic collapses to linear safety.
///
/// MagicFieldEngine holds one instance as a field.  The p value is exposed
/// so GRHarmonicEngine.PLaplacianP can be synchronised from the same scalar.
/// </summary>
public class MagicFieldOperator
{
    /// <summary>
    /// Current recursion-depth scalar ∈ [0.1, 1].
    /// Set by ManifoldRegulator via ApplyDepth() each physics tick.
    /// </summary>
    public float CurrentDepth { get; private set; } = 1f;

    /// <summary>
    /// Current p-Laplacian exponent, interpolated from depth.
    ///   p = Lerp(2, 6, CurrentDepth)
    /// Updated on every ApplyDepth() call.
    /// Expose so GRHarmonicEngine can synchronise its own PLaplacianP.
    /// </summary>
    public float PLaplacianP  { get; private set; } = 2f;

    /// <summary>
    /// Update the recursion depth.  Also recomputes PLaplacianP.
    /// Called once per physics tick by MagicFieldEngine (via UnifiedEngine).
    /// </summary>
    public void ApplyDepth(float depth)
    {
        CurrentDepth = Mathf.Clamp(depth, 0.1f, 1f);
        PLaplacianP  = Mathf.Lerp(2f, 6f, CurrentDepth);
    }

    /// <summary>
    /// Scale a flux value by the current depth.
    /// High depth → full amplitude; low depth → near-zero (magic is suppressed).
    /// </summary>
    public float ComputeMagicFlux(float baseFlux)
    {
        return baseFlux * CurrentDepth;
    }

    /// <summary>
    /// Apply the p-Laplacian nonlinearity to a flux value.
    ///
    ///   f(x) = |x|^p · sign(x)
    ///
    /// At p=2: quadratic (close to linear, safe).
    /// At p=6: sixth-power (strongly nonlinear, expressive).
    ///
    /// Sign is preserved so the field keeps its directional character.
    /// Input is clamped to [−1, 1] before the power to prevent explosion.
    ///
    /// NOTE: Mathf.Clamp(NaN, -1, 1) returns NaN in C# — so we guard
    /// explicitly.  This method is total: it never throws for any float.
    /// </summary>
    public float ApplyNonlinearity(float flux)
    {
        // NaN / Infinity guard — must come BEFORE Clamp (Clamp passes NaN through).
        if (!float.IsFinite(flux))
        {
            if (float.IsNaN(flux))
                GD.PrintErr("[MagicFieldOperator] NaN input to ApplyNonlinearity — clamped to 0");
            return 0f;
        }

        float clamped = Mathf.Clamp(flux, -1f, 1f);
        return Mathf.Pow(Mathf.Abs(clamped), PLaplacianP) * Mathf.Sign(clamped);
    }

    /// <summary>
    /// Convenience: apply depth scaling then nonlinearity in one call.
    /// Equivalent to ApplyNonlinearity(ComputeMagicFlux(baseFlux)).
    /// </summary>
    public float Apply(float baseFlux) =>
        ApplyNonlinearity(ComputeMagicFlux(baseFlux));
}
