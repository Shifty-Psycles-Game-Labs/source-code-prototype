/// <summary>
/// DichotomyOperator — the Δ/δ gap operator for a single field pair (i, j).
///
/// From NEM-U DeltaDelta (B.P.LAW):
///
///   Δ  = global Hodge Laplacian  (harmonic structure of the whole manifold)
///   δᵢ = local gradient operator  (perturbation at field i's axis)
///
///   M_ij = Δ · Tᵢ  −  δᵢⱼ · Tⱼ
///
/// Interpretation:
///   • Δ · Tᵢ         — global harmonic pull on field i (where i wants to be)
///   • δᵢⱼ · Tⱼ       — local gradient of field j weighted by coupling δᵢⱼ
///   • M_ij            — the conceptual tension between the two:
///                       positive → i is above j's influence (dominant)
///                       negative → j's gradient is pulling i away from harmony
///                       zero     → resonance / equilibrium on this axis
///
/// Dichotomy axes encoded (as named constants for readability):
///   INTENT    = 0
///   KNOWLEDGE = 1
///   EMOTION   = 2
///   ACTION    = 3
///
/// This is the cell-level building block of the HarmonicAttentionMatrix.
/// Torsion replaces Tension as the global field: geometric (rotational), bounded, oscillatory.
/// </summary>
public static class DichotomyOperator
{
    // ── Field index constants ─────────────────────────────────────────────────
    public const int INTENT    = 0;
    public const int KNOWLEDGE = 1;
    public const int EMOTION   = 2;
    public const int ACTION    = 3;
    public const int FIELD_COUNT = 4;

    /// <summary>
    /// Named dichotomy axes produced by specific (i,j) pairs.
    /// These are the conceptual directions in the manifold described in DeltaDelta.md.
    /// </summary>
    public static string DichotomyName(int i, int j) => (i, j) switch
    {
        (INTENT,    KNOWLEDGE) => "global/local",
        (KNOWLEDGE, INTENT)    => "local/global",
        (INTENT,    EMOTION)   => "harmonic/perturbative",
        (EMOTION,   INTENT)    => "perturbative/harmonic",
        (INTENT,    ACTION)    => "stable/unstable",
        (ACTION,    INTENT)    => "unstable/stable",
        (KNOWLEDGE, EMOTION)   => "equilibrium/deviation",
        (EMOTION,   KNOWLEDGE) => "deviation/equilibrium",
        (KNOWLEDGE, ACTION)    => "resonance/discord",
        (ACTION,    KNOWLEDGE) => "discord/resonance",
        (EMOTION,   ACTION)    => "collapse/progression",
        (ACTION,    EMOTION)   => "progression/collapse",
        _ when i == j          => "identity (no torsion)",
        _                      => $"field{i}/field{j}"
    };

    // ── Core operator ─────────────────────────────────────────────────────────

    /// <summary>
    /// Computes M_ij = Δ · T_i  −  δ_ij · T_j  on all vertices.
    ///
    /// Parameters:
    ///   laplacian  — the global Hodge Laplacian Δ (|V|×|V| SparseMatrix)
    ///   T_i        — vertex field for the "source" cognitive axis i
    ///   T_j        — vertex field for the "target" cognitive axis j
    ///   delta_ij   — scalar coupling weight for the local gradient term
    ///                (0 = pure harmonicity; 1 = full local perturbation)
    ///
    /// Returns a float[] of length |V| — the per-vertex dichotomy tension.
    /// </summary>
    public static float[] Compute(
        SparseMatrix laplacian,
        float[]      T_i,
        float[]      T_j,
        float        delta_ij)
    {
        if (laplacian.Rows == 0 || T_i == null || T_j == null)
            return System.Array.Empty<float>();

        // Sanitize inputs before the matrix multiply — NaN/Inf in T_i would
        // propagate through the Laplacian and corrupt the HAM weight table,
        // which then infects step sizes on the next tick.
        var safeI = new float[T_i.Length];
        for (int v = 0; v < T_i.Length; v++)
            safeI[v] = float.IsFinite(T_i[v]) ? T_i[v] : 0f;

        // Δ · T_i  — global harmonic residual of field i
        float[] globalPull = laplacian.Multiply(safeI);

        // δ_ij · T_j  — local gradient: the raw field value of j, scaled by coupling
        //   (δ acts as a perturbation: how much j's local state pulls against i's harmony)
        int n      = globalPull.Length;
        int nJ     = System.Math.Min(T_j.Length, n);
        var result = new float[n];

        for (int v = 0; v < n; v++)
        {
            float tj  = v < nJ ? T_j[v] : 0f;
            float loc = float.IsFinite(tj) ? delta_ij * tj : 0f;
            result[v] = globalPull[v] - loc;
        }

        return result;
    }

    // ── Torsion scalar ────────────────────────────────────────────────────────

    /// <summary>
    /// Collapses M_ij to a single scalar: the L2 norm of the torsion field.
    /// A large value means field i is far from harmony under j's perturbation.
    /// Used by HarmonicAttentionMatrix to build the 4×4 attention weight table.
    /// </summary>
    public static float TorsionNorm(float[] M_ij)
    {
        float sum = 0f;
        foreach (float v in M_ij) if (float.IsFinite(v)) sum += v * v;
        return sum > 0f ? (float)System.Math.Sqrt(sum) : 0f;
    }
}
