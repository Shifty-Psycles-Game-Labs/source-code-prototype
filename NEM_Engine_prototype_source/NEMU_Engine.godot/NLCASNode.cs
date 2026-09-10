using Godot;
using System;

/// <summary>
/// NLCASNode — Neural Lattice Cognitive Attention System
///
/// Implements INLCASAdapter with the full transformer-style pipeline over the
/// NEM manifold, as specified in README_XD_NEM-U_OM4_001.md §"NLCAS" and
/// §"NLCAS Positional Encoding Spec" (lines 5689–5912):
///
///   0. Core principle: operate on structured embeddings, not raw fields.
///
///   Pipeline (per tick):
///     1. Field Encoding    — F_v = [K_v, E_v, I_v, A_v]  (4-vector cognitive state)
///     2. Positional Encoding — P_v = [x, y, z, curvature, flux, tension, dichotomy]
///                              (7-vector; updated live via UpdatePositionalEncoding)
///     3. Token Assembly    — T_v = W_f · F_v + W_p · P_v  (d-vector embedding)
///     4. Q/K/V Projection  — per-head linear projections of each token
///     5. Attention         — scaled dot-product softmax, per head
///     6. Value Aggregation — O_u^(h) = Σ_v A_uv^(h) V_v^(h); concat heads
///     7. Write-back        — W_o · O_u → [ΔK, ΔE, ΔI, ΔA] blended into fields
///
///   UpdatePositionalEncoding — called by UnifiedEngine before PushCognitiveState,
///   takes live per-vertex physics scalars and rebuilds _P[] with exponential smoothing.
///
///   PullAttentionWeights — writes the most recent per-head-mean attention matrix
///   into the 2×2 OM4 slice that RelationshipCoupler reads.
///
/// Tunable parameters (all Godot [Export]):
///   EmbeddingDim         (d)   — token embedding width              default 8
///   NumHeads             (H)   — number of attention heads          default 2
///   WriteBackAlpha             — field blend factor                 default 0.1
///   PositionalUpdateAlpha      — EMA smoothing for positional vec   default 0.1
///
/// Weights (W_f d×4, W_p d×7, W_Q/K/V per head, W_o) are He-uniform initialised,
/// seeded with "NEMU", and remain fixed until a training adapter is attached.
/// </summary>
public partial class NLCASNode : Node, INLCASAdapter
{
    // ── tunable exports ───────────────────────────────────────────────────────
    [Export] public int   EmbeddingDim          { get; set; } = 8;
    [Export] public int   NumHeads              { get; set; } = 2;
    [Export] public float WriteBackAlpha        { get; set; } = 0.1f;
    [Export] public float PositionalUpdateAlpha { get; set; } = 0.1f;

    // ── HAM tension gate ──────────────────────────────────────────────────────
    /// <summary>
    /// Set by UnifiedEngine each tick (before PushCognitiveState) to the
    /// HAM-tension-gated effective write-back alpha:
    ///   effectiveAlpha = WriteBackAlpha * (tension / (1 + tension))
    ///
    /// When &gt; 0, PushCognitiveState uses this value instead of WriteBackAlpha.
    /// Resets to -1 after each forward pass so a missed gate never persists.
    /// UnifiedEngine sets it again the following tick.
    /// </summary>
    public float WriteBackAlphaOverride { get; set; } = -1f;

    // ── internal weight matrices ──────────────────────────────────────────────
    // W_f  : EmbeddingDim × 4   (cognitive field projection)
    // W_p  : EmbeddingDim × 7   (positional encoding projection)
    // W_Q/K/V per head: HeadDim × EmbeddingDim
    // W_o  : 4 × (NumHeads * HeadDim)
    private float[,] _Wf = null!;
    private float[,] _Wp = null!;
    private float[][,] _WQ = null!;
    private float[][,] _WK = null!;
    private float[][,] _WV = null!;
    private float[,] _Wo = null!;

    // ── positional state ──────────────────────────────────────────────────────
    // _P[v] — 7-vector: [x*0.1, y*0.1, z*0.1, curvatureNorm, fluxNorm, tensionNorm, dichotomyNorm]
    // _Tokens[v] — d-vector token embedding assembled from F_v + P_v each tick
    private float[][] _P      = Array.Empty<float[]>();
    private float[][] _Tokens = Array.Empty<float[]>();

    // Last computed per-vertex outputs [v, 4] (K,E,I,A deltas)
    private float[,]? _lastOutput;

    // Last per-head attention matrices [numHeads][v,v] — mean exposed via PullAttentionWeights
    private float[][,]? _lastAttn;

    // Vertex count seen on the last PushCognitiveState call — used to detect resize
    private int _lastVertexCount = -1;

    // ── lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        InitWeights(EmbeddingDim, NumHeads);
    }

    // ── positional encoding ───────────────────────────────────────────────────

    /// <summary>
    /// Updates the per-vertex 7-vector positional encodings from live physics data.
    /// Called by UnifiedEngine each tick before PushCognitiveState.
    ///
    /// P_v = [x*0.1, y*0.1, z*0.1, clamp(curvature,-1,1),
    ///         clamp(flux,-1,1), clamp(tension,-1,1), clamp(dichotomy,-1,1)]
    ///
    /// Applied with exponential smoothing (PositionalUpdateAlpha ≈ 0.1).
    /// </summary>
    public void UpdatePositionalEncoding(
        Vector3[] vertices,
        float[]   curvature,
        float[]   flux,
        float[]   tension,
        float[]   dichotomy)
    {
        if (vertices == null || vertices.Length == 0) return;
        int n = vertices.Length;

        // (Re-)allocate on vertex count change
        if (_P.Length != n)
        {
            _P      = new float[n][];
            _Tokens = new float[n][];
            for (int v = 0; v < n; v++)
            {
                _P[v]      = new float[7];
                _Tokens[v] = new float[EmbeddingDim];
            }
        }

        float beta   = PositionalUpdateAlpha;
        float retain = 1f - beta;

        for (int v = 0; v < n; v++)
        {
            // Raw positional features
            float px = vertices[v].X * 0.1f;
            float py = vertices[v].Y * 0.1f;
            float pz = vertices[v].Z * 0.1f;
            float cr = v < curvature.Length  ? Mathf.Clamp(curvature[v],  -1f, 1f) : 0f;
            float fl = v < flux.Length       ? Mathf.Clamp(flux[v],       -1f, 1f) : 0f;
            float tn = v < tension.Length    ? Mathf.Clamp(tension[v],    -1f, 1f) : 0f;
            float dc = v < dichotomy.Length  ? Mathf.Clamp(dichotomy[v],  -1f, 1f) : 0f;

            // Exponential smoothing
            _P[v][0] = retain * _P[v][0] + beta * px;
            _P[v][1] = retain * _P[v][1] + beta * py;
            _P[v][2] = retain * _P[v][2] + beta * pz;
            _P[v][3] = retain * _P[v][3] + beta * cr;
            _P[v][4] = retain * _P[v][4] + beta * fl;
            _P[v][5] = retain * _P[v][5] + beta * tn;
            _P[v][6] = retain * _P[v][6] + beta * dc;
        }
    }

    // ── INLCASAdapter — push ──────────────────────────────────────────────────

    /// <summary>
    /// Receives the four cognitive vertex fields each physics tick, runs the
    /// full NLCAS forward pass, and stores the modulated field deltas internally.
    /// UnifiedEngine then calls PullAttentionWeights to retrieve the result.
    ///
    /// Token assembly uses the live 7-vector positional encoding last set by
    /// UpdatePositionalEncoding.  If positional state is not yet initialised
    /// (first tick before UpdatePositionalEncoding was called), _P defaults to
    /// all-zeros which is safe and equivalent to the old synthetic baseline.
    /// </summary>
    public void PushCognitiveState(float[] knowledge, float[] emotion, float[] intent, float[] action)
    {
        if (knowledge == null || knowledge.Length == 0) return;

        int nV = knowledge.Length;
        if (nV != emotion.Length || nV != intent.Length || nV != action.Length) return;

        // Re-initialise weights lazily if vertex count changed (e.g. complex was rebuilt)
        if (nV != _lastVertexCount)
        {
            _lastVertexCount = nV;
            InitWeights(EmbeddingDim, NumHeads);
            // Also reset positional state for the new size
            _P      = new float[nV][];
            _Tokens = new float[nV][];
            for (int v = 0; v < nV; v++)
            {
                _P[v]      = new float[7];
                _Tokens[v] = new float[EmbeddingDim];
            }
        }

        int d  = EmbeddingDim;
        int H  = NumHeads;
        int dh = d / H;
        if (dh < 1) dh = 1;

        // ── Step 1–3: Field Encoding + Positional Encoding + Token Assembly ──
        // F_v = [K_v, E_v, I_v, A_v]            (4-vector cognitive state)
        // P_v = _P[v]                             (7-vector positional state, pre-updated)
        // T_v = W_f · F_v + W_p · P_v            (d-vector token)
        float[,] tokens = new float[nV, d];

        for (int v = 0; v < nV; v++)
        {
            float[] F = { knowledge[v], emotion[v], intent[v], action[v] };
            float[] P = _P.Length > v ? _P[v] : new float[7];   // safe fallback

            for (int di = 0; di < d; di++)
            {
                float val = 0f;
                for (int fi = 0; fi < 4; fi++) val += _Wf[di, fi] * F[fi];
                for (int pi = 0; pi < 7; pi++) val += _Wp[di, pi] * P[pi];
                tokens[v, di] = val;
            }

            // Cache the assembled token (used for AvgTokenNorm debug readout)
            if (_Tokens.Length > v)
                for (int di = 0; di < d; di++) _Tokens[v][di] = tokens[v, di];
        }

        // ── Step 4+5+6: Multi-Head Attention ────────────────────────────────
        float[,]   concatOut = new float[nV, H * dh];
        float[][,] attnAll   = new float[H][,];

        float scaleDh = 1f / Mathf.Sqrt(dh);

        for (int h = 0; h < H; h++)
        {
            float[,] Q = ProjectTokens(tokens, _WQ[h], nV, d, dh);
            float[,] K = ProjectTokens(tokens, _WK[h], nV, d, dh);
            float[,] V = ProjectTokens(tokens, _WV[h], nV, d, dh);

            float[,] A = new float[nV, nV];
            for (int u = 0; u < nV; u++)
            {
                float maxScore = float.NegativeInfinity;
                for (int v = 0; v < nV; v++)
                {
                    float dot = 0f;
                    for (int di = 0; di < dh; di++) dot += Q[u, di] * K[v, di];
                    A[u, v] = dot * scaleDh;
                    if (A[u, v] > maxScore) maxScore = A[u, v];
                }
                float rowSum = 0f;
                for (int v = 0; v < nV; v++)
                {
                    A[u, v] = Mathf.Exp(A[u, v] - maxScore);
                    rowSum  += A[u, v];
                }
                if (rowSum > 0f)
                    for (int v = 0; v < nV; v++) A[u, v] /= rowSum;
            }
            attnAll[h] = A;

            int headOffset = h * dh;
            for (int u = 0; u < nV; u++)
                for (int di = 0; di < dh; di++)
                {
                    float agg = 0f;
                    for (int v = 0; v < nV; v++) agg += A[u, v] * V[v, di];
                    concatOut[u, headOffset + di] = agg;
                }
        }
        _lastAttn = attnAll;

        // ── Step 7: Write-back ───────────────────────────────────────────────
        int    woCols = H * dh;
        float[,] output = new float[nV, 4];
        for (int v = 0; v < nV; v++)
            for (int fi = 0; fi < 4; fi++)
            {
                float val = 0f;
                for (int ci = 0; ci < woCols; ci++) val += _Wo[fi, ci] * concatOut[v, ci];
                output[v, fi] = val;
            }
        _lastOutput = output;

        // Use HAM-gated alpha if UnifiedEngine set one this tick; fall back to static value.
        float alpha   = WriteBackAlphaOverride >= 0f ? WriteBackAlphaOverride : WriteBackAlpha;
        WriteBackAlphaOverride = -1f;   // consume the override — reset for next tick

        float retainA = 1f - alpha;
        for (int v = 0; v < nV; v++)
        {
            knowledge[v] = retainA * knowledge[v] + alpha * output[v, 0];
            emotion[v]   = retainA * emotion[v]   + alpha * output[v, 1];
            intent[v]    = retainA * intent[v]     + alpha * output[v, 2];
            action[v]    = retainA * action[v]     + alpha * output[v, 3];
        }
    }

    // ── INLCASAdapter — pull ──────────────────────────────────────────────────

    /// <summary>
    /// Writes the mean per-head attention matrix (averaged across heads) into the
    /// 2×2 OM4 slice A that RelationshipCoupler reads.
    /// </summary>
    public void PullAttentionWeights(float[,] A)
    {
        if (_lastAttn == null || A == null) return;
        if (A.GetLength(0) < 2 || A.GetLength(1) < 2) return;

        int H  = _lastAttn.Length;
        int nV = _lastAttn[0].GetLength(0);
        if (nV < 2) return;

        int rows = A.GetLength(0);
        int cols = A.GetLength(1);

        for (int u = 0; u < rows; u++)
        for (int v = 0; v < cols; v++)
        {
            if (u >= nV || v >= nV) continue;
            float mean = 0f;
            for (int h = 0; h < H; h++) mean += _lastAttn[h][u, v];
            A[u, v] = mean / H;
        }
    }

    // ── diagnostics ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the average L2 norm of the current token embeddings across all vertices.
    /// Used by UniverseRoot to populate the AvgTokenNorm HUD readout.
    /// Returns 0 if no tokens have been assembled yet.
    /// </summary>
    public float GetAvgTokenNorm()
    {
        if (_Tokens == null || _Tokens.Length == 0) return 0f;
        float total = 0f;
        int   d     = EmbeddingDim;
        foreach (var tok in _Tokens)
        {
            if (tok == null) continue;
            float norm = 0f;
            for (int di = 0; di < d && di < tok.Length; di++) norm += tok[di] * tok[di];
            total += Mathf.Sqrt(norm);
        }
        return total / _Tokens.Length;
    }

    // ── weight initialisation ─────────────────────────────────────────────────

    /// <summary>
    /// Initialises all weight matrices with He-style uniform random values.
    /// W_p is now d×7 to match the 7-vector positional encoding.
    /// Seed 0x4E454D55 = ASCII "NEMU" — deterministic, reproducible.
    /// </summary>
    private void InitWeights(int d, int H)
    {
        int dh     = d / H;
        if (dh < 1) dh = 1;
        int woCols = H * dh;

        var rng = new Random(0x4E454D55);   // "NEMU" seed

        _Wf = RandomMatrix(d, 4, rng, scale: MathF.Sqrt(2f / 4f));
        _Wp = RandomMatrix(d, 7, rng, scale: MathF.Sqrt(2f / 7f));   // 7 positional features

        _WQ = new float[H][,];
        _WK = new float[H][,];
        _WV = new float[H][,];
        for (int h = 0; h < H; h++)
        {
            _WQ[h] = RandomMatrix(dh, d, rng, scale: MathF.Sqrt(2f / d));
            _WK[h] = RandomMatrix(dh, d, rng, scale: MathF.Sqrt(2f / d));
            _WV[h] = RandomMatrix(dh, d, rng, scale: MathF.Sqrt(2f / d));
        }

        _Wo = RandomMatrix(4, woCols, rng, scale: MathF.Sqrt(2f / woCols));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Projects nV tokens (nV×d) through W (outDim×d) → result (nV×outDim).</summary>
    private static float[,] ProjectTokens(float[,] tokens, float[,] W, int nV, int d, int outDim)
    {
        float[,] result = new float[nV, outDim];
        for (int v = 0; v < nV; v++)
            for (int o = 0; o < outDim; o++)
            {
                float val = 0f;
                for (int di = 0; di < d; di++) val += W[o, di] * tokens[v, di];
                result[v, o] = val;
            }
        return result;
    }

    /// <summary>Creates a rows×cols matrix filled with He-uniform random values.</summary>
    private static float[,] RandomMatrix(int rows, int cols, Random rng, float scale)
    {
        float[,] m = new float[rows, cols];
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            m[r, c] = (float)(rng.NextDouble() * 2.0 - 1.0) * scale;
        return m;
    }
}
