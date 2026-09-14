using Godot;

/// <summary>
/// HarmonicTensor — rank-4 cognitive state at a single manifold vertex.
///
/// Stores the four NEM-U cognitive components (Intent, Knowledge, Emotion, Action)
/// as a single value-type unit.  All Regulator operations (ψ-dissipation, AT-gating)
/// are applied directly to this struct in one pass over the vertex array, rather
/// than four separate passes over four separate float[].
///
/// Torsion is *derived* from field phases, never accumulated independently.
/// Torsion is geometric (rotational): τ(t+1) = τ(t) + Δθ.
/// It is naturally bounded and oscillatory — no scalar drift.
/// </summary>
public struct HarmonicTensor
{
    public float Intent;
    public float Knowledge;
    public float Emotion;
    public float Action;

    /// <summary>
    /// L1 magnitude of all four components.
    /// Used for field-strength computations; not the torsion source.
    /// </summary>
    public float Magnitude() =>
        System.Math.Abs(Intent)    +
        System.Math.Abs(Knowledge) +
        System.Math.Abs(Emotion)   +
        System.Math.Abs(Action);

    /// <summary>
    /// Torsion angle of this vertex state — the rotational phase in the
    /// (Intent×Knowledge, Emotion×Action) decomposition of the rank-4 bundle.
    ///
    ///   θ = atan2(Emotion × Action,  Intent × Knowledge)
    ///
    /// Interpretation (NEM geometric replacement for scalar stress):
    ///   • sin(θ) ∈ [−1, 1] — bounded, oscillatory torsion contribution.
    ///   • θ = 0 or π  → resonance (zero torsion).
    ///   • θ = ±π/2   → maximum torsion (orthogonal field planes).
    ///
    /// TotalTorsion = Σ_v sin(H[v].TorsionAngle())  ∈ [−|V|, |V|].
    /// </summary>
    public float TorsionAngle() =>
        (float)System.Math.Atan2(
            (double)(Emotion * Action),
            (double)(Intent  * Knowledge + 1e-9f));  // ε avoids atan2(0,0)
}

/// <summary>
/// Cognitive dynamics engine: rank-4 tensor bundle H = (T_intent, T_knowledge, T_emotion, T_action)
/// evolving as gradient flow over a potential Φ(H) on M₄ (NEM §"Cognitive dynamics").
///
/// Dynamical law:
///   dH/dλ = −∇Φ(H)
///
/// Discrete implementation:
///   The cognitive state is stored as a HarmonicTensor[] — one struct per vertex.
///   The potential Φ is a weighted quadratic of each field's Laplacian residual:
///     ∇Φ(T_k) = Δ₀ T_k       (graph Laplacian on vertices)
///
///   Stepper (gradient descent w/ step λ):
///     T_k(n+1) = T_k(n) − λ · Δ₀ T_k(n)
///
/// This drives each cognitive field toward harmonicity (Δ T → 0) over progression λ.
/// Fixed points are cognitive equilibria: ∇Φ(H*) = 0.
///
/// Torsion is derived, never accumulated:
///   TotalTorsion() = Σ_v sin(H[v].TorsionAngle())
///
/// This replaces the old scalar-stress Tension field.  Torsion is geometric
/// (rotational, phase-based) and is naturally bounded: τ ∈ [−|V|, |V|].
/// The update rule τ(t+1) = τ(t) + Δθ is oscillatory rather than accumulative,
/// which eliminates runaway scalar drift and makes the global field category-
/// consistent with the rest of the NEM-U operator stack (divergence, rotation,
/// Laplacian, harmonic operators are all geometric).
///
/// Δ/δ Dichotomy (NEM-U DeltaDelta, B.P.LAW):
///   After each gradient-flow step, the HarmonicAttentionMatrix is recomputed.
///   M[i,j] = Δ·Tᵢ − δᵢⱼ·Tⱼ encodes the cross-field conceptual torsions.
///   The softmax attention matrix A[i,j] then modulates the next gradient step.
///
/// Coupling inputs:
///   — EmFlux   (float[nV]) from MaxwellEngine, projected to vertices → modulates Emotion
///   — Curvature(float[nV]) from GRHarmonicEngine                    → modulates Intent
/// </summary>
public partial class MagicFieldEngine : Node
{
    [Export] public NodePath ComplexPath = "";

    // Gradient-flow step size (λ in NEM progression)
    [Export] public float LambdaStep  = 0.01f;

    // Base step size — SetDepth() scales from this value, not from the
    // already-throttled LambdaStep, so repeated calls don't ratchet it to zero.
    private float _baseLambdaStep = 0.01f;

    // Per-field coupling strengths
    [Export] public float EmToArousal       = 0.5f;   // EM flux → emotion
    [Export] public float CurvatureToIntent = 0.3f;   // GR curvature → intent

    // ── depth-throttled magic operator ────────────────────────────────────────
    /// <summary>
    /// p-Laplacian depth operator.  ManifoldRegulator writes MagicDepth into
    /// this each tick via SetDepth(); the p exponent interpolates between 2
    /// (safe/linear) and 6 (chaotic/nonlinear) as depth increases.
    /// </summary>
    public readonly MagicFieldOperator Operator = new MagicFieldOperator();

    /// <summary>
    /// Set the magic recursion depth from ManifoldRegulator.MagicDepth.
    /// Also scales LambdaStep so gradient-flow speed is depth-aware.
    /// Called by UnifiedEngine in step 6 after ManifoldRegulator.Apply().
    /// </summary>
    public void SetDepth(float depth)
    {
        Operator.ApplyDepth(depth);
        // Scale from the fixed base step, not from the already-throttled
        // LambdaStep — otherwise repeated calls would ratchet LambdaStep to 0.
        LambdaStep = Mathf.Max(0.001f, _baseLambdaStep * Operator.CurrentDepth);
    }

    // ── cognitive field state (vertex-indexed) ────────────────────────────────
    // One HarmonicTensor per vertex.  All four components live together so that
    // Regulator operations (ψ, AT) run in a single cache-friendly loop.
    private HarmonicTensor[] _H = System.Array.Empty<HarmonicTensor>();

    private SimplicialComplexNode?  _complex;
    private SparseMatrix?           _Delta0;   // graph Laplacian |V|×|V|

    // ── Δ/δ dichotomy layer ───────────────────────────────────────────────────
    private HarmonicAttentionMatrix _ham = new HarmonicAttentionMatrix();

    // ── field health diagnostics (populated each StepGradientFlow call) ───────
    private int   _fieldNaNCount = 0;
    private int   _fieldInfCount = 0;
    private float _fieldMaxValue = 0f;
    private float _fieldMinValue = 0f;

    /// <summary>
    /// Read-only field health snapshot from the most recent gradient-flow step.
    /// Used by HUDManifoldDisplay to show real-time stability metrics.
    /// </summary>
    public (int nanCount, int infCount, float maxValue, float minValue) FieldHealth =>
        (_fieldNaNCount, _fieldInfCount, _fieldMaxValue, _fieldMinValue);

    // ── lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _complex = GetNodeOrNull<SimplicialComplexNode>(ComplexPath);
        if (_complex == null)
        {
            GD.PushWarning("MagicFieldEngine: SimplicialComplexNode not found at '" + ComplexPath + "'.");
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
        int n = _complex!.VertexCount;
        _H    = new HarmonicTensor[n];

        // Seed small non-zero initial values so the flow has something to descend.
        var rng = new System.Random(42);
        for (int i = 0; i < n; i++)
        {
            _H[i].Intent    = (float)(rng.NextDouble() * 0.1);
            _H[i].Knowledge = (float)(rng.NextDouble() * 0.1);
            _H[i].Emotion   = (float)(rng.NextDouble() * 0.1);
            _H[i].Action    = 0f;
        }
    }

    // ── physics tick ──────────────────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        if (_complex == null || _Delta0 == null) return;
        StepGradientFlow();
        UpdateHarmonicAttention();
        // Emergency brake: replace any NaN/Inf that slipped through this tick.
        SanitizeFields();
    }

    private void StepGradientFlow()
    {
        // dH/dλ = −∇Φ(H)  where ∇Φ(T) = Δ₀ T
        // The gradient step per field is modulated by HAM self-attention A[i,i].
        float stepI = LambdaStep * GetSelfAttention(DichotomyOperator.INTENT);
        float stepK = LambdaStep * GetSelfAttention(DichotomyOperator.KNOWLEDGE);
        float stepE = LambdaStep * GetSelfAttention(DichotomyOperator.EMOTION);
        float stepA = LambdaStep * GetSelfAttention(DichotomyOperator.ACTION);

        // Extract per-field slices, apply Laplacian, write back into _H.
        float[] tI = ExtractField(DichotomyOperator.INTENT);
        float[] tK = ExtractField(DichotomyOperator.KNOWLEDGE);
        float[] tE = ExtractField(DichotomyOperator.EMOTION);
        float[] tA = ExtractField(DichotomyOperator.ACTION);

        float[] lapI = _Delta0!.Multiply(tI);
        float[] lapK = _Delta0.Multiply(tK);
        float[] lapE = _Delta0.Multiply(tE);
        float[] lapA = _Delta0.Multiply(tA);

        // Track field health this step for HUD diagnostics.
        _fieldNaNCount  = 0;
        _fieldInfCount  = 0;
        _fieldMaxValue  = float.NegativeInfinity;
        _fieldMinValue  = float.PositiveInfinity;

        for (int v = 0; v < _H.Length; v++)
        {
            // Clamp Laplacian outputs before applying — NaN from SparseMatrix
            // must not propagate into the field state.
            float dI = SafeLap(lapI[v], v, "lapI");
            float dK = SafeLap(lapK[v], v, "lapK");
            float dE = SafeLap(lapE[v], v, "lapE");
            float dA = SafeLap(lapA[v], v, "lapA");

            _H[v].Intent    -= stepI * dI;
            _H[v].Knowledge -= stepK * dK;
            _H[v].Emotion   -= stepE * dE;
            _H[v].Action    -= stepA * dA;

            // Apply p-Laplacian nonlinearity via MagicFieldOperator.
            // At full depth (p=6): strongly nonlinear, field amplitudes compressed.
            // At shallow depth (p=2): near-linear, safe gradient descent.
            _H[v].Intent    = Operator.ApplyNonlinearity(_H[v].Intent);
            _H[v].Knowledge = Operator.ApplyNonlinearity(_H[v].Knowledge);
            _H[v].Emotion   = Operator.ApplyNonlinearity(_H[v].Emotion);
            _H[v].Action    = Operator.ApplyNonlinearity(_H[v].Action);

            // Accumulate field health metrics.
            TrackHealth(_H[v].Intent);
            TrackHealth(_H[v].Knowledge);
            TrackHealth(_H[v].Emotion);
            TrackHealth(_H[v].Action);
        }
    }

    /// <summary>
    /// Returns lap if finite; logs once per unique bad vertex and returns 0.
    /// Prevents NaN/Inf from the Laplacian multiply entering the field state.
    /// </summary>
    private static float SafeLap(float lap, int v, string label)
    {
        if (float.IsFinite(lap)) return lap;
        GD.PrintErr($"[MagicFieldEngine] Non-finite {label}[{v}] = {lap} — zeroed");
        return 0f;
    }

    /// <summary>Accumulate per-tick field health counters.</summary>
    private void TrackHealth(float val)
    {
        if (float.IsNaN(val))        { _fieldNaNCount++;  return; }
        if (float.IsInfinity(val))   { _fieldInfCount++;  return; }
        if (val > _fieldMaxValue)      _fieldMaxValue = val;
        if (val < _fieldMinValue)      _fieldMinValue = val;
    }

    /// <summary>
    /// Recomputes the HarmonicAttentionMatrix after each field step.
    /// M[i,j] = Δ·Tᵢ − δᵢⱼ·Tⱼ encodes the Δ/δ dichotomy for all axis pairs.
    /// </summary>
    private void UpdateHarmonicAttention()
    {
        _ham.Recompute(_Delta0!, new[]
        {
            ExtractField(DichotomyOperator.INTENT),
            ExtractField(DichotomyOperator.KNOWLEDGE),
            ExtractField(DichotomyOperator.EMOTION),
            ExtractField(DichotomyOperator.ACTION)
        });
    }

    private float GetSelfAttention(int fieldIndex)
    {
        float w = _ham.Weights[fieldIndex, fieldIndex];
        if (w == 0f) return 1f;
        return _ham.AttentionRow(fieldIndex)[fieldIndex];
    }

    // ── coupling API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Inject the observer's action vector into the Action component at every vertex.
    /// Magnitude is broadcast uniformly — OM4 step 1 injection term.
    /// </summary>
    public void InjectActionVector(Godot.Vector3 actionVector)
    {
        float magnitude = actionVector.Length();
        if (magnitude == 0f) return;
        for (int i = 0; i < _H.Length; i++)
            _H[i].Action += magnitude;
    }

    /// <summary>Inject EM flux into the Emotion component at each vertex.</summary>
    public void InjectEmFlux(float[] emFluxOnVertices)
    {
        if (emFluxOnVertices == null) return;
        int n = Mathf.Min(emFluxOnVertices.Length, _H.Length);
        for (int i = 0; i < n; i++)
            _H[i].Emotion += EmToArousal * emFluxOnVertices[i];
    }

    /// <summary>Inject GR curvature into the Intent component at each vertex.</summary>
    public void InjectCurvature(float[] curvatureOnVertices)
    {
        if (curvatureOnVertices == null) return;
        int n = Mathf.Min(curvatureOnVertices.Length, _H.Length);
        for (int i = 0; i < n; i++)
            _H[i].Intent += CurvatureToIntent * curvatureOnVertices[i];
    }

    // ── field accessors — float[] views extracted from _H ────────────────────
    // These allocate on each call. Callers that need per-frame access should
    // cache the result for the tick, or use ExtractField() directly.

    public float[] GetIntent()    => ExtractField(DichotomyOperator.INTENT);
    public float[] GetKnowledge() => ExtractField(DichotomyOperator.KNOWLEDGE);
    public float[] GetEmotion()   => ExtractField(DichotomyOperator.EMOTION);
    public float[] GetAction()    => ExtractField(DichotomyOperator.ACTION);

    // ── dichotomy accessors ───────────────────────────────────────────────────

    /// <summary>Live reference to the Harmonic Attention Matrix.</summary>
    public HarmonicAttentionMatrix GetHAM() => _ham;

    /// <summary>Current dominant dichotomy axis name.</summary>
    public string DominantDichotomy() => _ham.DominantAxisName();

    /// <summary>
    /// Total manifold torsion — derived from field phases, never accumulated.
    ///
    ///   TotalTorsion = Σ_v sin(H[v].TorsionAngle())
    ///
    /// Torsion is geometric: it measures the rotational phase of each vertex's
    /// rank-4 field state in the (Intent×Knowledge, Emotion×Action) decomposition.
    /// sin(θ) ∈ [−1, 1] per vertex, so TotalTorsion ∈ [−|V|, |V|] — naturally
    /// bounded and oscillatory.  There is no scalar accumulation path; the field
    /// cannot drift without bound.
    ///
    /// This is the single canonical torsion source used by the Regulator to
    /// compute AT.  NPC behaviour is phase-modulated, not stress-modulated.
    /// </summary>
    public float TotalTorsion()
    {
        float tau = 0f;
        for (int i = 0; i < _H.Length; i++)
            tau += (float)System.Math.Sin(_H[i].TorsionAngle());
        return tau;
    }

    /// <summary>
    /// Cross-field dichotomy torsion from the HarmonicAttentionMatrix.
    ///   Φ_HAM = Σᵢⱼ W[i,j]²
    /// Distinct from TotalTorsion() — this measures structural dichotomy pressure,
    /// useful for HAM-driven attention modulation but not as the AT input.
    /// </summary>
    public float ComputeHAMTorsion() => _ham.TotalTorsion();

    /// <summary>
    /// Per-vertex torsion field for NLCAS positional encoding.
    /// Returns the per-vertex field of the dominant HAM dichotomy axis.
    /// </summary>
    public float[] GetLocalTorsionArray()
    {
        var (di, dj) = _ham.DominantAxis();
        if (di >= 0)
            return _ham.TorsionField(di, dj);
        return ExtractField(DichotomyOperator.INTENT);
    }

    /// <summary>
    /// Per-vertex scalar projection of the dominant dichotomy axis for NLCAS.
    /// </summary>
    public float[] GetLocalDichotomyArray()
    {
        var (di, dj) = _ham.DominantAxis();
        if (di < 0) return System.Array.Empty<float>();

        float[] field        = _ham.TorsionField(di, dj);
        float   axisStrength = _ham.Weights[di, dj];
        if (field == null || field.Length == 0) return System.Array.Empty<float>();

        float maxAbs = 0f;
        foreach (float f in field) { float a = System.Math.Abs(f); if (a > maxAbs) maxAbs = a; }
        float denom = maxAbs + 1e-6f;

        float[] result = new float[field.Length];
        for (int v = 0; v < field.Length; v++)
            result[v] = axisStrength * field[v] / denom;

        return result;
    }

    /// <summary>Set the coupling weight δᵢⱼ for a specific dichotomy axis.</summary>
    public void SetDichotomyCoupling(int i, int j, float delta) => _ham.SetCoupling(i, j, delta);

    /// <summary>
    /// Potential Φ(H) = ½ Σ_k ‖Δ₀ T_k‖²
    /// Returns a scalar measuring total deviation from cognitive equilibrium.
    /// </summary>
    public float ComputePotential()
    {
        if (_Delta0 == null) return 0f;
        return 0.5f * (
            NormSquared(_Delta0.Multiply(ExtractField(DichotomyOperator.INTENT)))    +
            NormSquared(_Delta0.Multiply(ExtractField(DichotomyOperator.KNOWLEDGE))) +
            NormSquared(_Delta0.Multiply(ExtractField(DichotomyOperator.EMOTION)))   +
            NormSquared(_Delta0.Multiply(ExtractField(DichotomyOperator.ACTION)))
        );
    }

    // ── Regulator API ─────────────────────────────────────────────────────────

    /// <summary>
    /// ψ-dissipation and AT-gating applied in a single pass over _H.
    ///
    /// Per vertex:
    ///   Intent    -= ψ · dt         then *= AT
    ///   Emotion   -= ψ · dt         then *= AT
    ///   Action    -= ψ · dt         then *= AT
    ///   Knowledge -= ψ · 0.5 · dt   (slower drain, not AT-gated — structural memory)
    ///
    /// Combining both operations into one loop eliminates four separate passes and
    /// makes it explicit that tension cannot grow between dissipation and gating.
    /// </summary>
    public void ApplyPsiAndAT(float psi, float at, float dt)
    {
        float drainFast = psi * dt;
        float drainSlow = psi * 0.5f * dt;

        for (int i = 0; i < _H.Length; i++)
        {
            // ψ-dissipation
            _H[i].Intent    -= drainFast;
            _H[i].Emotion   -= drainFast;
            _H[i].Action    -= drainFast;
            _H[i].Knowledge -= drainSlow;

            // AT-gating (knowledge excluded — structural memory persists under saturation)
            _H[i].Intent  *= at;
            _H[i].Emotion *= at;
            _H[i].Action  *= at;
        }
    }

    /// <summary>
    /// Idle flow gate: multiplicative decay when no player input for IdleTimeout seconds.
    /// </summary>
    public void ApplyIdleDecay(float tensionDecay, float potentialDecay,
                               float arousalDecay,  float urgencyDecay)
    {
        for (int i = 0; i < _H.Length; i++)
        {
            _H[i].Emotion   *= arousalDecay;
            _H[i].Intent    *= urgencyDecay;
            _H[i].Action    *= tensionDecay;
            _H[i].Knowledge *= potentialDecay;
        }
    }

    /// <summary>
    /// NaN/Infinity guard — replace bad values with 0.
    /// Called by Regulator as the final step each frame (emergency brake).
    /// </summary>
    public void SanitizeFields()
    {
        for (int i = 0; i < _H.Length; i++)
        {
            if (!float.IsFinite(_H[i].Intent))    _H[i].Intent    = 0f;
            if (!float.IsFinite(_H[i].Knowledge)) _H[i].Knowledge = 0f;
            if (!float.IsFinite(_H[i].Emotion))   _H[i].Emotion   = 0f;
            if (!float.IsFinite(_H[i].Action))    _H[i].Action    = 0f;
        }
    }

    // ── kept for backward compatibility (Regulator calls these if present) ────
    // These now delegate to the unified ApplyPsiAndAT method.
    // Left as thin wrappers so any external caller that existed before still compiles.

    /// <inheritdoc cref="ApplyPsiAndAT"/>
    public void ApplyPsiDissipation(float psi, float dt)
    {
        float drainFast = psi * dt;
        float drainSlow = psi * 0.5f * dt;
        for (int i = 0; i < _H.Length; i++)
        {
            _H[i].Intent    -= drainFast;
            _H[i].Emotion   -= drainFast;
            _H[i].Action    -= drainFast;
            _H[i].Knowledge -= drainSlow;
        }
    }

    /// <inheritdoc cref="ApplyPsiAndAT"/>
    public void ApplyAT(float at)
    {
        for (int i = 0; i < _H.Length; i++)
        {
            _H[i].Intent  *= at;
            _H[i].Emotion *= at;
            _H[i].Action  *= at;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a single component column from _H as a new float[].
    /// Used to feed the Laplacian, the HAM, and external callers expecting float[].
    /// </summary>
    private float[] ExtractField(int fieldIndex)
    {
        if (_H.Length == 0) return System.Array.Empty<float>();
        var result = new float[_H.Length];
        for (int v = 0; v < _H.Length; v++)
            result[v] = fieldIndex switch
            {
                DichotomyOperator.INTENT    => _H[v].Intent,
                DichotomyOperator.KNOWLEDGE => _H[v].Knowledge,
                DichotomyOperator.EMOTION   => _H[v].Emotion,
                DichotomyOperator.ACTION    => _H[v].Action,
                _ => 0f
            };
        return result;
    }

    private static float NormSquared(float[] v)
    {
        float s = 0f;
        foreach (var x in v) s += x * x;
        return s;
    }
}
