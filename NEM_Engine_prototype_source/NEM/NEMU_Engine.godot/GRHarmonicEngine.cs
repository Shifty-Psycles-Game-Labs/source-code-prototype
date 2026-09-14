using Godot;

/// <summary>
/// Discrete GR engine: harmonic rank-2 tensor (scalar proxy) on vertices.
///
/// Physics (NEM §9):
///   Δ_g T = 0     (Laplace-Beltrami, p=2 — standard linearised GR)
///   Δ_p T = 0     (p-Laplacian, p≠2 — nonlinear curvature propagation)
///   Wave stepper: T_{n+1} = 2T_n − T_{n-1} − Δt² (Δ_p T_n − S)
///
/// Exposes GetScalarField() / AddSource() for UnifiedEngine coupling.
/// </summary>
public partial class GRHarmonicEngine : Node
{
    [Export] public NodePath ComplexPath   = "";
    [Export] public NodePath MetricPath    = "";
    [Export] public float    DeltaT        = 0.01f;
    [Export] public float    PLaplacianP   = 2.0f;  // p=2 → standard GR

    // ── depth-throttled GR operator ───────────────────────────────────────────
    /// <summary>
    /// Depth-throttled curvature operator.  ManifoldRegulator writes GRDepth
    /// into this each tick via SetDepth(); the operator scales all curvature
    /// sampling and smooths residuals at shallow depth.
    /// </summary>
    public readonly GR_Operator Operator = new GR_Operator();

    /// <summary>
    /// Set the GR recursion depth from ManifoldRegulator.GRDepth.
    /// Also synchronises PLaplacianP so the wave stepper uses the depth-aware p.
    /// Called by UnifiedEngine in step 6 after ManifoldRegulator.Apply().
    /// </summary>
    public void SetDepth(float depth)
    {
        Operator.ApplyDepth(depth);
        // Sync PLaplacianP: deep GR = more nonlinear (p→4), shallow = linear (p=2)
        PLaplacianP = Mathf.Lerp(2f, 4f, Operator.CurrentDepth);
    }

    // ── state ─────────────────────────────────────────────────────────────────
    private SimplicialComplexNode _complex;
    private MetricNode            _metric;
    private GRDiscreteOperators   _ops;

    private float[] _TPrev;
    private float[] _T;
    private float[] _Source;   // EM→GR source term on vertices

    private double _accum;

    // ── lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _complex = GetNodeOrNull<SimplicialComplexNode>(ComplexPath);
        _metric  = GetNodeOrNull<MetricNode>(MetricPath);

        if (_complex == null)
        {
            GD.PushWarning("GRHarmonicEngine: SimplicialComplexNode not found at '" + ComplexPath + "'.");
            return;
        }

        RebuildOperators();
        AllocateFields();
        InitializePulse();
    }

    /// <summary>Rebuild operators if topology or metric changes at runtime.</summary>
    public void RebuildOperators()
    {
        if (_complex == null) return;
        _ops = new GRDiscreteOperators();
        _ops.Build(_complex, _metric);
    }

    private void AllocateFields()
    {
        int n    = _complex.VertexCount;
        _TPrev   = new float[n];
        _T       = new float[n];
        _Source  = new float[n];
    }

    /// <summary>Seed a small initial curvature perturbation.</summary>
    public void InitializePulse(int centreVertex = 0, float amplitude = 0.1f)
    {
        if (_T == null || _T.Length == 0) return;
        int n     = _T.Length;
        int pivot = Mathf.Clamp(centreVertex, 0, n - 1);
        for (int i = 0; i < n; i++)
        {
            float d = Mathf.Abs(i - pivot);
            _T[i]     = amplitude * Mathf.Exp(-d * d / 4f);
            _TPrev[i] = _T[i];
        }
    }

    // ── physics tick ──────────────────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        if (_complex == null || _ops == null) return;
        _accum += delta;
        if (_accum < DeltaT) return;
        _accum = 0.0;
        Step();
    }

    private void Step()
    {
        // Choose operator: p≈2 → Laplace-Beltrami (metric aware), else p-Laplacian
        float[] lap = Mathf.Abs(PLaplacianP - 2.0f) < 0.001f
            ? _ops.ApplyLaplaceBeltrami(_T)
            : _ops.ApplyPLaplacian(_T, PLaplacianP);

        float dt2 = DeltaT * DeltaT;
        int   n   = _T.Length;
        float[] TNext = new float[n];

        for (int i = 0; i < n; i++)
            TNext[i] = 2f * _T[i] - _TPrev[i] - dt2 * (lap[i] - _Source[i]);

        _TPrev  = _T;
        _T      = TNext;

        // Decay source term each step (prevents unbounded accumulation)
        for (int i = 0; i < _Source.Length; i++)
            _Source[i] *= 0.9f;
    }

    // ── coupling API (used by UnifiedEngine) ──────────────────────────────────

    /// <summary>
    /// Returns the GR scalar field T on vertices, depth-scaled through GR_Operator.
    ///
    /// Each value is passed through Operator.Apply() so callers (UnifiedEngine
    /// curvature injection, NLCAS positional encoding) automatically see the
    /// depth-throttled signal — no changes needed in the coupling code.
    ///
    /// If the Operator is at full depth (1.0) the output is identical to the raw field.
    /// </summary>
    public float[] GetScalarField()
    {
        if (_T == null) return System.Array.Empty<float>();
        var result = new float[_T.Length];
        for (int i = 0; i < _T.Length; i++)
            result[i] = Operator.Apply(_T[i]);
        return result;
    }

    /// <summary>
    /// EM→GR: inject EM energy density as a source term on vertices.
    /// S[i] = energy density at vertex i, scaled by strength.
    /// </summary>
    public void AddSource(float[] S, float strength)
    {
        if (S == null || _Source == null) return;
        int n = Mathf.Min(S.Length, _Source.Length);
        for (int i = 0; i < n; i++)
            _Source[i] += strength * S[i];
    }

    /// <summary>Called by UniverseRoot.StartSimulation.</summary>
    public void Start(double delta) => _PhysicsProcess(delta);
}
