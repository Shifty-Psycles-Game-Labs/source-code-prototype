using Godot;

/// <summary>
/// Entropic progression and collapse engine (NEM §"Predictions / Thermal Clock" and §"Main loop step 6").
///
/// Three responsibilities:
///
/// 1. Progression parameter λ
///    λ accumulates monotonically: dλ/dτ = dS/dτ ≥ 0.
///    Δλ = ∫ (dS/dτ) dτ  over the simulation.
///    Implemented as: λ += EntropyRate * delta  each tick.
///
/// 2. Entropy field S[v]
///    Per-vertex scalar encoding local entropy density.
///    Updated by diffusion on the graph Laplacian Δ₀:
///      S(n+1) = S(n) + α (−Δ₀ S(n))   (heat-equation diffusion, drives equilibration)
///    External injection (EM energy density, curvature) acts as an entropy source.
///
/// 3. Collapse operator O(x)
///    A roaming wave-sampler at position SamplerVertex.
///    At each collapse event (when local entropy exceeds CollapseThreshold):
///      — reads local field values at the nearest vertex
///      — smooths the field locally (Gaussian kernel over neighbours)
///      — emits a CollapseEvent signal for HUD / visualisers
///      — optionally advances SamplerVertex toward the lowest-entropy neighbour
///        (progression operator P(x) = f(∇S, H, C))
/// </summary>
public partial class EntropyCollapseEngine : Node
{
    [Export] public NodePath ComplexPath        = "";
    [Export] public NodePath MaxwellPath        = "";
    [Export] public NodePath GRPath             = "";

    [Export] public float EntropyRate           = 0.05f;   // dS/dτ base rate
    [Export] public float DiffusionAlpha        = 0.01f;   // heat-equation step size
    [Export] public float CollapseThreshold     = 2.0f;    // local S above this triggers collapse
    [Export] public float CollapseSmoothing     = 0.3f;    // fraction to smooth on collapse
    [Export] public float ProgressionWeight     = 0.4f;    // harmonic pull toward lower-S neighbour

    // ── state ─────────────────────────────────────────────────────────────────
    private SimplicialComplexNode _complex;
    private MaxwellEngine2Form    _maxwell;
    private GRHarmonicEngine      _gr;

    private float[]      _S;         // entropy field on vertices
    private float        _Lambda;    // global progression parameter
    private int          _Sampler;   // current sampler vertex index
    private SparseMatrix _Delta0;    // graph Laplacian |V|×|V|

    // ── signals ───────────────────────────────────────────────────────────────
    [Signal] public delegate void CollapseEventHandler(int vertex, float entropyValue, float emFlux, float grScalar);

    // ── lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _complex = GetNodeOrNull<SimplicialComplexNode>(ComplexPath);
        _maxwell = GetNodeOrNull<MaxwellEngine2Form>(MaxwellPath);
        _gr      = GetNodeOrNull<GRHarmonicEngine>(GRPath);

        if (_complex == null)
        {
            GD.PushWarning("EntropyCollapseEngine: SimplicialComplexNode not found at '" + ComplexPath + "'.");
            return;
        }

        RebuildOperators();
        AllocateFields();
    }

    public void RebuildOperators()
    {
        if (_complex == null) return;
        _Delta0 = HodgeLaplacian.BuildDelta0(_complex);
    }

    private void AllocateFields()
    {
        int n = _complex.VertexCount;
        _S       = new float[n];
        _Lambda  = 0f;
        _Sampler = 0;

        // Seed small uniform entropy
        for (int i = 0; i < n; i++) _S[i] = 0.1f;
    }

    // ── physics tick ──────────────────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        if (_complex == null || _Delta0 == null) return;

        float dt = (float)delta;

        // 1. Advance global progression λ
        _Lambda += EntropyRate * dt;

        // 2. Inject entropy sources from field engines
        InjectFieldSources();

        // 3. Diffuse entropy field: S += α(−Δ₀S)
        DiffuseEntropy();

        // 4. Run collapse operator at sampler vertex
        CheckCollapse();

        // 5. Advance sampler toward lower-entropy neighbours (progression operator)
        AdvanceSampler();
    }

    // ── entropy injection ─────────────────────────────────────────────────────
    private void InjectFieldSources()
    {
        if (_S == null) return;

        // EM energy density (|F|²) projected to vertices via face→vertex averaging
        if (_maxwell != null)
        {
            float[] F = _maxwell.GetField();
            if (F != null)
            {
                var emOnVerts = ProjectFacesToVertices(F);
                for (int i = 0; i < Mathf.Min(emOnVerts.Length, _S.Length); i++)
                    _S[i] += EntropyRate * emOnVerts[i] * emOnVerts[i];  // |F|²
            }
        }

        // GR curvature as entropy source
        if (_gr != null)
        {
            float[] T = _gr.GetScalarField();
            if (T != null)
            {
                for (int i = 0; i < Mathf.Min(T.Length, _S.Length); i++)
                    _S[i] += EntropyRate * Mathf.Abs(T[i]);
            }
        }
    }

    // ── entropy diffusion ─────────────────────────────────────────────────────
    private void DiffuseEntropy()
    {
        if (_Delta0 == null || _S == null) return;

        // Heat equation: S_new = S − α Δ₀ S
        float[] lapS = _Delta0.Multiply(_S);
        for (int i = 0; i < _S.Length; i++)
            _S[i] -= DiffusionAlpha * lapS[i];

        // Clamp: entropy is non-negative
        for (int i = 0; i < _S.Length; i++)
            if (_S[i] < 0f) _S[i] = 0f;
    }

    // ── collapse operator ─────────────────────────────────────────────────────
    private void CheckCollapse()
    {
        if (_S == null || _S.Length == 0) return;
        int v = _Sampler;
        if (v >= _S.Length) return;

        if (_S[v] < CollapseThreshold) return;

        // Read local field values at this vertex
        float emFlux   = ReadEMAtVertex(v);
        float grScalar = ReadGRAtVertex(v);

        // Local smoothing: blend S[v] toward neighbourhood average
        float neighbourAvg = ComputeNeighbourAverage(_S, v);
        _S[v] = Godot.Mathf.Lerp(_S[v], neighbourAvg, CollapseSmoothing);

        // Emit collapse event for HUD / visualisers
        EmitSignal("CollapseEvent", v, _S[v], emFlux, grScalar);
    }

    // ── progression operator P(x) = f(∇S, H, C) ─────────────────────────────
    private void AdvanceSampler()
    {
        if (_complex == null || _S == null || _complex.EdgeCount == 0) return;

        int current    = _Sampler;
        int bestVertex = current;
        float bestS    = _S.Length > current ? _S[current] : float.MaxValue;

        // Find the adjacent vertex with the lowest entropy (progression = entropy descent)
        foreach (var (v0, v1) in _complex.Edges)
        {
            int neighbour = -1;
            if (v0 == current) neighbour = v1;
            else if (v1 == current) neighbour = v0;

            if (neighbour < 0 || neighbour >= _S.Length) continue;

            float sN = _S[neighbour];
            if (sN < bestS)
            {
                bestS    = sN;
                bestVertex = neighbour;
            }
        }

        // Probabilistic progression: move toward best only if Δλ step agrees
        if (bestVertex != current)
        {
            float delta = _S[current] - bestS;
            if (delta > ProgressionWeight * EntropyRate)
                _Sampler = bestVertex;
        }
    }

    // ── field reading helpers ─────────────────────────────────────────────────
    private float ReadEMAtVertex(int v)
    {
        if (_maxwell == null) return 0f;
        float[] F = _maxwell.GetField();
        if (F == null || F.Length == 0) return 0f;
        // Project face values to this vertex
        var verts = ProjectFacesToVertices(F);
        return v < verts.Length ? verts[v] : 0f;
    }

    private float ReadGRAtVertex(int v)
    {
        if (_gr == null) return 0f;
        float[] T = _gr.GetScalarField();
        return (T != null && v < T.Length) ? T[v] : 0f;
    }

    // ── face→vertex projection (average over incident faces) ─────────────────
    private float[] ProjectFacesToVertices(float[] faceValues)
    {
        int nV  = _complex.VertexCount;
        var res = new float[nV];
        var cnt = new int[nV];

        for (int f = 0; f < Mathf.Min(faceValues.Length, _complex.FaceCount); f++)
        {
            var face = _complex.Faces[f];
            float val = Mathf.Abs(faceValues[f]);
            foreach (int v in face)
            {
                if (v < nV) { res[v] += val; cnt[v]++; }
            }
        }
        for (int v = 0; v < nV; v++)
            if (cnt[v] > 0) res[v] /= cnt[v];
        return res;
    }

    private float ComputeNeighbourAverage(float[] field, int vertex)
    {
        float sum   = 0f;
        int   count = 0;
        foreach (var (v0, v1) in _complex.Edges)
        {
            int n = -1;
            if (v0 == vertex) n = v1;
            else if (v1 == vertex) n = v0;
            if (n >= 0 && n < field.Length) { sum += field[n]; count++; }
        }
        return count > 0 ? sum / count : field[vertex];
    }

    // ── public accessors ──────────────────────────────────────────────────────
    public float[] GetEntropyField()   => _S;
    public float   GetLambda()         => _Lambda;
    public int     GetSamplerVertex()  => _Sampler;
}
