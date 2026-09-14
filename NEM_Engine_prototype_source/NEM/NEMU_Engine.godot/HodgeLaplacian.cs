using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Single;

/// <summary>
/// Static factory for discrete Hodge Laplacians on a simplicial complex.
///
/// Theory (NEM §8.2 / §9):
///   Δ_k = D_{k-1}ᵀ D_{k-1} + D_k D_kᵀ
///
///   Δ₀  (|V|×|V|)  — graph Laplacian on vertices (diffusion / curvature)
///   Δ₁  (|E|×|E|)  — Laplacian on edges (connection / gauge field)
///   Δ₂  (|F|×|F|)  — Laplacian on faces (Maxwell 2-form / GR wave)
///
/// All matrices are built from the incidence matrices produced by IncidenceBuilder.
/// Δ₂ is the primary operator used by MaxwellEngine2Form and GRHarmonicEngine.
///
/// MathNet.Numerics eigensolver methods:
///   FindHarmonicBasis  — null-space vectors of Δ_k (true harmonic forms, ΔF=0)
///   FindLowestEigenmode — smallest non-zero eigenpair (slowest-decaying mode)
/// </summary>
public static class HodgeLaplacian
{
    // ── MathNet conversion helper ─────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="SparseMatrix"/> (CSR, float) into a MathNet dense
    /// single-precision matrix for use with eigensolvers.
    /// Only practical for complexes up to a few thousand simplices.
    /// For larger meshes, use the iterative methods below.
    /// </summary>
    public static Matrix<float> ToMathNetDense(SparseMatrix m)
    {
        var A = DenseMatrix.Create(m.Rows, m.Cols, 0f);
        for (int r = 0; r < m.Rows; r++)
            for (int k = m.RowPtr[r]; k < m.RowPtr[r + 1]; k++)
                A[r, m.ColIdx[k]] += m.Values[k];
        return A;
    }

    // ── Harmonic basis (null space of Δ_k) ───────────────────────────────────

    /// <summary>
    /// Computes an orthonormal basis for the harmonic k-forms on the complex.
    /// Harmonic forms satisfy ΔF = 0 exactly — these are the topological
    /// invariants predicted by the NEM theorem.
    ///
    /// Uses SVD: columns of V corresponding to singular values below
    /// <paramref name="tolerance"/> span the null space of Δ_k.
    ///
    /// Returns: array of harmonic basis vectors, each of length = dimension of
    ///          the k-chain space (vertices / edges / faces).
    ///          Empty array if no harmonic modes exist (trivial topology).
    /// </summary>
    public static float[][] FindHarmonicBasis(SparseMatrix laplacian, float tolerance = 1e-5f)
    {
        if (laplacian.Rows == 0) return System.Array.Empty<float[]>();

        var A   = ToMathNetDense(laplacian);
        var svd = A.Svd(computeVectors: true);

        var basis = new System.Collections.Generic.List<float[]>();
        var S     = svd.S;          // singular values, descending
        var VT    = svd.VT;         // rows of VT = right singular vectors

        // Null-space: singular values ≈ 0
        for (int i = S.Count - 1; i >= 0; i--)
        {
            if (S[i] > tolerance) break;
            // Row i of VT is the corresponding right singular vector
            var vec = new float[VT.ColumnCount];
            for (int j = 0; j < vec.Length; j++)
                vec[j] = VT[i, j];
            basis.Add(vec);
        }

        return basis.ToArray();
    }

    // ── Lowest eigenpair (slowest decaying mode) ──────────────────────────────

    /// <summary>
    /// Finds the smallest non-zero eigenvalue and its eigenvector for Δ_k.
    /// This is the Fiedler value for Δ₀ (graph connectivity / algebraic
    /// connectivity) and the fundamental mode frequency for Δ₂ (Maxwell wave).
    ///
    /// Returns (eigenvalue, eigenvector), or (0, empty) for degenerate inputs.
    ///
    /// Note: uses dense EVD — suitable for complexes up to ~1 000 simplices.
    /// For larger meshes, use a shift-invert Lanczos (future ILGPU path).
    /// </summary>
    public static (float eigenvalue, float[] eigenvector) FindLowestEigenmode(SparseMatrix laplacian)
    {
        if (laplacian.Rows == 0) return (0f, System.Array.Empty<float>());

        var A   = ToMathNetDense(laplacian);
        var evd = A.Evd(Symmetricity.Symmetric);

        var values  = evd.EigenValues;
        var vectors = evd.EigenVectors;

        float  bestVal = float.MaxValue;
        int    bestIdx = -1;
        float  threshold = 1e-8f;

        for (int i = 0; i < values.Count; i++)
        {
            float rv = (float)values[i].Real;
            if (rv > threshold && rv < bestVal)
            {
                bestVal = rv;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) return (0f, System.Array.Empty<float>());

        var vec = new float[vectors.RowCount];
        for (int r = 0; r < vec.Length; r++)
            vec[r] = vectors[r, bestIdx];

        return (bestVal, vec);
    }

    // ── Δ₀ = D₁ᵀ D₁  (|V|×|V| graph Laplacian) ─────────────────────────────
    /// <summary>
    /// Graph Laplacian on vertices.
    /// Δ₀ = D₁ᵀ D₁
    /// Diagonal = vertex degree; off-diagonal = −1 for adjacent vertices.
    /// Used for scalar diffusion (entropy field, curvature smoothing).
    /// </summary>
    public static SparseMatrix BuildDelta0(SimplicialComplexNode complex)
    {
        var D1  = IncidenceBuilder.BuildD1(complex);   // |E|×|V|
        var D1T = D1.Transpose();                       // |V|×|E|
        return SparseMatrix.Multiply(D1T, D1);          // |V|×|V|
    }

    // ── Δ₁ = D₁ D₁ᵀ + D₂ᵀ D₂  (|E|×|E| edge Laplacian) ───────────────────
    /// <summary>
    /// Hodge Laplacian on 1-forms (edges).
    /// Δ₁ = D₁D₁ᵀ + D₂ᵀD₂
    /// Used for connection / gauge-field evolution.
    /// </summary>
    public static SparseMatrix BuildDelta1(SimplicialComplexNode complex)
    {
        var D1  = IncidenceBuilder.BuildD1(complex);
        var D2  = IncidenceBuilder.BuildD2(complex);
        var D1T = D1.Transpose();
        var D2T = D2.Transpose();

        var A = SparseMatrix.Multiply(D1, D1T);   // |E|×|E|
        var B = SparseMatrix.Multiply(D2T, D2);   // |E|×|E|
        return SparseMatrix.Add(A, B);
    }

    // ── Δ₂ = D₁ᵀD₁ + D₂D₂ᵀ  (|F|×|F| face Laplacian) ─────────────────────
    /// <summary>
    /// Hodge Laplacian on 2-forms (faces).
    /// Δ₂ = D₁ᵀD₁ + D₂D₂ᵀ
    ///
    /// This is the "A*A + BB*" structure from §8.2.
    /// Primary operator for:
    ///   — Maxwell: ΔF = 0  (vacuum EM)
    ///   — GR:      Δh = 0  (linearised gravitational wave)
    /// </summary>
    public static SparseMatrix BuildDelta2(SimplicialComplexNode complex)
    {
        var D1  = IncidenceBuilder.BuildD1(complex);   // |E|×|V|
        var D2  = IncidenceBuilder.BuildD2(complex);   // |F|×|E|
        var D1T = D1.Transpose();                       // |V|×|E|  — note: D1T is |V|×|E|
        var D2T = D2.Transpose();                       // |E|×|F|

        // A = D1ᵀ * D1  would be |V|×|V| — wrong size for faces.
        // Correct Δ₂ needs the "lower" and "upper" pieces projected onto faces:
        //   lower: D₂ D₂ᵀ  (|F|×|F|)
        //   upper: D₁ᵀ D₁  projected — this is zero for the pure-face operator
        //          because D₁ acts on vertices, not faces.
        // Standard discrete Hodge on 2-chains:
        //   Δ₂ = (D₂)(D₂)ᵀ + (D₃)(D₃)ᵀ
        // With no 3-simplices (volumes) yet:
        //   Δ₂ = D₂ D₂ᵀ   (lower boundary only)
        // This matches NEM §8.2 "A*A + BB*" with D₂ in the role of B.

        return SparseMatrix.Multiply(D2, D2T);   // |F|×|F|
    }

    // ── Metric-weighted Laplace-Beltrami on vertices ──────────────────────────
    /// <summary>
    /// Δ_g = Dᵀ G D   where G is the diagonal metric weight matrix on edges.
    /// Used by GRDiscreteOperators for the Laplace-Beltrami operator Δ_g T ≈ D*(GDT).
    /// </summary>
    public static SparseMatrix BuildLaplaceBeltrami(SimplicialComplexNode complex, float[] edgeWeights)
    {
        var D  = IncidenceBuilder.BuildD1(complex);   // |E|×|V|
        var DT = D.Transpose();                        // |V|×|E|
        var G  = BuildDiagonalWeight(edgeWeights, D.Rows, D.Rows);
        var GD = SparseMatrix.Multiply(G, D);          // |E|×|V|
        return SparseMatrix.Multiply(DT, GD);          // |V|×|V|
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static SparseMatrix BuildDiagonalWeight(float[] weights, int rows, int cols)
    {
        int n      = System.Math.Min(weights.Length, rows);
        int[] rp   = new int[rows + 1];
        int[] ci   = new int[n];
        float[] v  = new float[n];

        for (int i = 0; i < n; i++)
        {
            rp[i] = i;
            ci[i] = i;
            v[i]  = weights[i];
        }
        // remaining rows (if weights shorter than rows) are empty
        for (int i = n; i <= rows; i++) rp[i] = n;

        return new SparseMatrix(rows, cols, rp, ci, v);
    }
}
