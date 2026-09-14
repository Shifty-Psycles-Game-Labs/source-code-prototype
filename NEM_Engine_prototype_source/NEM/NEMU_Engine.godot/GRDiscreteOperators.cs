/// <summary>
/// Discrete operators for GR harmonic tensor fields on vertices.
///
/// Theory (NEM §9):
///   Δ_g T ≈ D*(GDT)       Laplace-Beltrami (p=2, standard GR)
///   Δ_p T = D*( |DT|^{p-2} DT )  p-Laplacian (nonlinear, Lim generalisation)
///
/// Both operators act on a scalar vertex field T of length |V|.
/// The metric weight matrix G is diagonal with per-edge values g_ij from MetricNode.
/// </summary>
public class GRDiscreteOperators
{
    private SparseMatrix _D  = SparseMatrix.Empty;  // |E|×|V|  gradient (D₁)
    private SparseMatrix _DT = SparseMatrix.Empty;  // |V|×|E|  divergence (D₁ᵀ)
    private SparseMatrix _G  = SparseMatrix.Empty;  // |E|×|E|  diagonal metric weights

    private bool _built;

    // ── build ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Construct operators from a SimplicialComplexNode and edge weights from MetricNode.
    /// Call once after topology / metric is ready; call again if either changes.
    /// </summary>
    public void Build(SimplicialComplexNode complex, MetricNode metric)
    {
        _D  = IncidenceBuilder.BuildD1(complex);
        _DT = _D.Transpose();

        // Diagonal metric weight matrix G: g[e] = MetricNode.GetEdgeWeight(e)
        int   nE  = _D.Rows;
        int[] rp  = new int[nE + 1];
        int[] ci  = new int[nE];
        float[] v = new float[nE];

        for (int e = 0; e < nE; e++)
        {
            rp[e] = e;
            ci[e] = e;
            v[e]  = metric != null ? metric.GetEdgeWeight(e) : 1f;
        }
        rp[nE] = nE;
        _G = new SparseMatrix(nE, nE, rp, ci, v);

        _built = true;
    }

    // ── Laplace-Beltrami: Δ_g T = Dᵀ(GDT)  ──────────────────────────────────
    /// <summary>
    /// Metric-weighted Laplace-Beltrami operator (p=2, standard GR).
    ///   grad  = D  · T        (|E| edge gradients)
    ///   wgrad = G  · grad     (|E| metric-weighted gradients)
    ///   result = Dᵀ · wgrad   (|V| divergence back to vertices)
    /// </summary>
    public float[] ApplyLaplaceBeltrami(float[] T)
    {
        if (!_built || T == null) return new float[T?.Length ?? 0];
        var grad   = _D.Multiply(T);
        var wgrad  = _G.Multiply(grad);
        return _DT.Multiply(wgrad);
    }

    // ── p-Laplacian: Δ_p T = Dᵀ( |DT|^{p-2} DT )  ──────────────────────────
    /// <summary>
    /// Nonlinear p-Laplacian (Lim generalisation, NEM §4 / §9.24).
    /// p=2 → same as Laplace-Beltrami with unit metric.
    /// p>2 → nonlinear diffusion / curvature-driven propagation.
    ///
    /// Algorithm:
    ///   grad     = D · T
    ///   weight_e = |grad_e|^{p-2}   (per edge, scalar)
    ///   weighted = weight · grad     (pointwise)
    ///   result   = Dᵀ · weighted
    /// </summary>
    public float[] ApplyPLaplacian(float[] T, float p)
    {
        if (!_built || T == null) return new float[T?.Length ?? 0];

        var grad     = _D.Multiply(T);
        int nE       = grad.Length;
        var weighted = new float[nE];
        float pm2    = p - 2f;

        for (int e = 0; e < nE; e++)
        {
            float absG   = System.Math.Abs(grad[e]);
            float weight = pm2 == 0f ? 1f : (float)System.Math.Pow(absG, pm2);
            weighted[e]  = weight * grad[e];
        }

        return _DT.Multiply(weighted);
    }

    // ── scalar curvature estimate: κ = diag(Δ_g) normalised ──────────────────
    /// <summary>
    /// Returns a per-vertex scalar curvature estimate from the diagonal of Δ_g applied to T.
    /// Used by UnifiedEngine to read local curvature for GR→EM coupling.
    /// </summary>
    public float[] ScalarCurvature(float[] T)
    {
        return ApplyLaplaceBeltrami(T);
    }
}
