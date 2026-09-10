/// <summary>
/// HarmonicAttentionMatrix — the Rank-2 Tensor of Rank-2 Operators.
///
/// From NEM-U DeltaDelta (B.P.LAW):
///
///   M_(ij) = Δᵢ − δᵢⱼ
///
/// This is the 4×4 "matrix of matrices" where each cell contains both:
///   — a per-vertex torsion field  (float[], length |V|)
///   — a scalar attention weight   (float, = ‖M_ij‖)
///
/// The 4 cognitive axes are: Intent(0), Knowledge(1), Emotion(2), Action(3).
///
/// The scalar weight table W[i,j] is the Harmonic Attention Operator:
///   W[i,j] = ‖Δ · Tᵢ − δᵢⱼ · Tⱼ‖
///
/// This matrix encodes every dichotomy axis described in DeltaDelta.md:
///   global/local, harmonic/perturbative, stable/unstable,
///   equilibrium/deviation, resonance/discord, collapse/progression.
///
/// After each cognitive step (MagicFieldEngine), call:
///   ham.Recompute(laplacian, fields, couplings)
///
/// Then read:
///   ham.Weights[i, j]       — scalar attention for axis (i, j)
///   ham.TorsionField(i, j)  — per-vertex torsion vector M_ij
///   ham.DominantAxis()      — (i, j) pair with maximum torsion (hottest dichotomy)
///   ham.AttentionRow(i)     — softmax-normalised row i (distributes i's attention)
/// </summary>
public class HarmonicAttentionMatrix
{
    private const int N = DichotomyOperator.FIELD_COUNT;  // 4

    // ── state ─────────────────────────────────────────────────────────────────

    /// <summary>Scalar torsion norms W[i,j] = ‖M_ij‖. Updated by Recompute().</summary>
    public readonly float[,] Weights = new float[N, N];

    /// <summary>Per-vertex torsion fields M_ij[v]. Updated by Recompute().</summary>
    private readonly float[][,] _fields;   // [vertex][i, j] — indexed as _fieldGrid[i,j][v]
    private readonly float[][][] _fieldGrid; // _fieldGrid[i][j] = float[] length |V|

    // ── coupling weights δᵢⱼ ─────────────────────────────────────────────────
    // Initialized to 1.0 (full local perturbation). Tune these to weight axes.
    private readonly float[,] _couplings = new float[N, N];

    public HarmonicAttentionMatrix()
    {
        _fieldGrid = new float[N][][];
        for (int i = 0; i < N; i++)
        {
            _fieldGrid[i] = new float[N][];
            for (int j = 0; j < N; j++)
                _fieldGrid[i][j] = System.Array.Empty<float>();
        }

        // Default: all couplings = 1.0 (equal weight to all dichotomy axes)
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                _couplings[i, j] = 1.0f;
    }

    // ── coupling configuration ────────────────────────────────────────────────

    /// <summary>
    /// Set the coupling weight δᵢⱼ for axis (i,j).
    /// δ=0 → axis is muted (field i sees no torsion from j).
    /// δ=1 → full local perturbation (default).
    /// δ>1 → amplified (field i is hypersensitive to j's deviation).
    /// </summary>
    public void SetCoupling(int i, int j, float delta) => _couplings[i, j] = delta;

    /// <summary>Returns the current coupling weight δᵢⱼ.</summary>
    public float GetCoupling(int i, int j) => _couplings[i, j];

    // ── recompute ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes the full 4×4 matrix M_ij and the scalar weight table W.
    ///
    /// Call once per physics tick after MagicFieldEngine has stepped its fields.
    ///
    /// Parameters:
    ///   laplacian — shared global Hodge Laplacian Δ₀ (|V|×|V|)
    ///   fields    — array of 4 vertex fields: [Intent, Knowledge, Emotion, Action]
    /// </summary>
    public void Recompute(SparseMatrix laplacian, float[][] fields)
    {
        if (laplacian.Rows == 0 || fields == null || fields.Length < N) return;

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                float[] mij = DichotomyOperator.Compute(
                    laplacian,
                    fields[i],
                    fields[j],
                    _couplings[i, j]);

                _fieldGrid[i][j] = mij;
                Weights[i, j]    = DichotomyOperator.TorsionNorm(mij);
            }
        }
    }

    /// <summary>
    /// Scalar overload of Recompute — no Laplacian required.
    ///
    /// Used by PlayerHElement where each cognitive field is represented by a
    /// single (magnitude, phase) pair rather than a full vertex array.
    ///
    /// W[i,j] = δᵢⱼ · magnitudes[i] · |sin(phases[i] − phases[j])|
    ///
    /// Interpretation:
    ///   • magnitudes[i]  — field amplitude (how "active" axis i is)
    ///   • phase difference — how out-of-phase field i and j are (0 = resonance,
    ///                        π/2 = maximum torsion, π = antiphase)
    ///   • δᵢⱼ coupling   — same per-axis weight as the full overload
    ///
    /// The _fieldGrid is left as empty arrays because there are no vertices;
    /// only Weights and the derived TotalTorsion / DominantAxis are meaningful.
    /// </summary>
    public void RecomputeScalar(float[] magnitudes, float[] phases)
    {
        if (magnitudes == null || magnitudes.Length < N ||
            phases     == null || phases.Length     < N) return;

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                if (i == j)
                {
                    // Self-tension: pure magnitude (no phase cross-term)
                    Weights[i, j] = _couplings[i, j] * magnitudes[i];
                }
                else
                {
                    float phaseDiff = phases[i] - phases[j];
                    Weights[i, j]  = _couplings[i, j]
                                   * magnitudes[i]
                                   * System.Math.Abs((float)System.Math.Sin(phaseDiff));
                }
            }
        }
    }

    // ── accessors ─────────────────────────────────────────────────────────────

    /// <summary>Returns the per-vertex torsion field M_ij (read-only).</summary>
    public float[] TorsionField(int i, int j) => _fieldGrid[i][j];

    /// <summary>
    /// Returns the (i,j) pair with the maximum scalar torsion — the hottest
    /// dichotomy axis in the current cognitive state.
    /// If all torsions are zero (perfect harmony), returns (-1, -1).
    /// </summary>
    public (int i, int j) DominantAxis()
    {
        float max = 0f;
        int bi = -1, bj = -1;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                if (Weights[i, j] > max) { max = Weights[i, j]; bi = i; bj = j; }
        return (bi, bj);
    }

    /// <summary>
    /// Returns the name of the dominant dichotomy axis.
    /// Convenience wrapper for HUD / debug output.
    /// </summary>
    public string DominantAxisName()
    {
        var (i, j) = DominantAxis();
        return i < 0 ? "harmony (no torsion)" : DichotomyOperator.DichotomyName(i, j);
    }

    /// <summary>
    /// Softmax-normalised attention row for field i.
    /// Treats W[i,0..3] as logits and returns a probability distribution
    /// over the 4 target axes — i.e. how field i distributes its attention
    /// across the manifold.
    ///
    /// This is the discrete analogue of the attention mechanism described in
    /// RelationshipCoupler.Couple(): A_ij = softmax(Q_i K_j^T / √d_k).
    /// </summary>
    public float[] AttentionRow(int i)
    {
        var row = new float[N];
        float maxW = 0f;
        for (int j = 0; j < N; j++) if (Weights[i, j] > maxW) maxW = Weights[i, j];

        float sum = 0f;
        for (int j = 0; j < N; j++) { row[j] = (float)System.Math.Exp(Weights[i, j] - maxW); sum += row[j]; }
        if (sum > 0f) for (int j = 0; j < N; j++) row[j] /= sum;
        return row;
    }

    /// <summary>
    /// Returns the full 4×4 softmax attention matrix A[i,j].
    /// Each row i is the attention distribution of field i over target fields j.
    /// This is the Harmonic Attention Operator described in DeltaDelta.md.
    /// </summary>
    public float[,] AttentionMatrix()
    {
        var A = new float[N, N];
        for (int i = 0; i < N; i++)
        {
            var row = AttentionRow(i);
            for (int j = 0; j < N; j++) A[i, j] = row[j];
        }
        return A;
    }

    /// <summary>
    /// Returns a scalar measuring total deviation from harmony:
    ///   Φ = Σᵢⱼ W[i,j]²
    /// Decreases toward zero as all fields approach Δ = 0.
    /// Analogous to MagicFieldEngine.ComputePotential() but over all cross-field axes.
    /// </summary>
    public float TotalTorsion()
    {
        float sum = 0f;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                sum += Weights[i, j] * Weights[i, j];
        return sum;
    }
}
