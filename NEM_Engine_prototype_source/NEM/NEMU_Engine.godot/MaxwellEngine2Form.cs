using Godot;

/// <summary>
/// Discrete Maxwell engine: harmonic 2-form on the faces of a simplicial complex.
///
/// Physics (NEM §8):
///   Δ₂ = D₁ᵀD₁ + D₂D₂ᵀ   (Hodge Laplacian on 2-forms)
///   ΔF = 0                 (vacuum Maxwell harmonicity condition)
///   Wave stepper: F_{n+1} = 2F_n − F_{n-1} − Δt² (Δ₂F_n − J)
///
/// Exposes GetField() / SetGeometryWeights() / AddSource() for UnifiedEngine coupling.
/// </summary>
public partial class MaxwellEngine2Form : Node
{
    [Export] public NodePath ComplexPath = "";
    [Export] public float    DeltaT      = 0.01f;

    // ── state ─────────────────────────────────────────────────────────────────
    private SimplicialComplexNode _complex;
    private SparseMatrix          _Delta2;
    private float[]               _FPrev;
    private float[]               _F;
    private float[]               _J;           // source term on faces
    private float[]               _GeoWeights;  // GR→EM geometry modulation per face
    private double                _accum;

    // ── lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _complex = GetNodeOrNull<SimplicialComplexNode>(ComplexPath);
        if (_complex == null)
        {
            GD.PushWarning("MaxwellEngine2Form: SimplicialComplexNode not found at '" + ComplexPath + "'.");
            return;
        }

        RebuildOperators();
        AllocateFields();
        InitializePulse();
    }

    /// <summary>Rebuild Δ₂ when topology changes at runtime.</summary>
    public void RebuildOperators()
    {
        if (_complex == null) return;
        _Delta2 = HodgeLaplacian.BuildDelta2(_complex);
    }

    private void AllocateFields()
    {
        int n = _complex.FaceCount;
        _FPrev      = new float[n];
        _F          = new float[n];
        _J          = new float[n];
        _GeoWeights = new float[n];
        // Default geometry weight = 1 (no GR distortion)
        for (int i = 0; i < n; i++) _GeoWeights[i] = 1f;
    }

    /// <summary>Seed a localised Gaussian pulse on the field at initialisation.</summary>
    public void InitializePulse(int centreface = 0, float amplitude = 1.0f)
    {
        if (_F == null || _F.Length == 0) return;
        int n     = _F.Length;
        int pivot = Mathf.Clamp(centreface, 0, n - 1);
        for (int i = 0; i < n; i++)
        {
            float d = Mathf.Abs(i - pivot);
            _F[i]     = amplitude * Mathf.Exp(-d * d / 4f);
            _FPrev[i] = _F[i];
        }
    }

    // ── physics tick ──────────────────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        if (_complex == null || _Delta2 == null) return;
        _accum += delta;
        if (_accum < DeltaT) return;
        _accum = 0.0;
        StepWave();
    }

    private void StepWave()
    {
        float[] lapF = _Delta2.Multiply(_F);
        float   dt2  = DeltaT * DeltaT;
        int     n    = _F.Length;
        float[] next = new float[n];

        for (int i = 0; i < n; i++)
        {
            // GR→EM: geometry weight scales effective Laplacian per face
            float rhs = _GeoWeights[i] * lapF[i] - _J[i];
            next[i] = 2f * _F[i] - _FPrev[i] - dt2 * rhs;
        }

        _FPrev = _F;
        _F     = next;
    }

    // ── coupling API (used by UnifiedEngine) ──────────────────────────────────

    /// <summary>Returns the current EM 2-form field F on faces (read-only view).</summary>
    public float[] GetField() => _F;

    /// <summary>
    /// Returns a per-vertex EM flux array (length = vertex count) by averaging the
    /// face field over each vertex's incident faces.  Used by NLCASNode positional encoding.
    /// Returns an empty array if the complex or field is not yet initialised.
    /// </summary>
    public float[] GetFluxAtVertices()
    {
        if (_complex == null || _F == null || _complex.VertexCount == 0) return System.Array.Empty<float>();

        int nV    = _complex.VertexCount;
        int nF    = _complex.FaceCount;
        float[] fluxV = new float[nV];
        int[]   count = new int[nV];

        for (int fi = 0; fi < nF && fi < _F.Length; fi++)
        {
            var face = _complex.Faces[fi];
            foreach (int v in face)
            {
                if (v >= 0 && v < nV)
                {
                    fluxV[v] += _F[fi];
                    count[v]++;
                }
            }
        }

        for (int v = 0; v < nV; v++)
            if (count[v] > 0) fluxV[v] /= count[v];

        return fluxV;
    }

    /// <summary>
    /// GR→EM: set per-face geometry weights W(g).
    /// In StepWave: effective Δ₂ is scaled by w[i] per face.
    /// strength=1 → full effect, strength=0 → no coupling.
    /// </summary>
    public void SetGeometryWeights(float[] weights, float strength)
    {
        if (weights == null || _GeoWeights == null) return;
        int n = Mathf.Min(weights.Length, _GeoWeights.Length);
        for (int i = 0; i < n; i++)
            _GeoWeights[i] = 1f + strength * (weights[i] - 1f);
    }

    /// <summary>EM→GR feedback source: add directly to J source term.</summary>
    public void AddSource(float[] S, float strength)
    {
        if (S == null || _J == null) return;
        int n = Mathf.Min(S.Length, _J.Length);
        for (int i = 0; i < n; i++)
            _J[i] += strength * S[i];
    }

    /// <summary>Called by UniverseRoot.StartSimulation.</summary>
    public void Start(double delta) => _PhysicsProcess(delta);
}
