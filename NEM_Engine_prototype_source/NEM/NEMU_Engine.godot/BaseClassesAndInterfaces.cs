using Godot;
using System.Collections.Generic;

public interface IFieldLayer
{
    void Initialize();
    void Step(double delta);
    object GetState();
}

public interface IRelationCarrier
{
    float Weight { get; set; }
    Vector3 Position { get; }
}

/// <summary>
/// INLCASAdapter — the clean boundary where the NLCAS transformer will plug in.
///
/// Contract:
///   PushCognitiveState  — called each physics tick; sends the current four field
///                         arrays to the transformer so it can condition on them.
///   PullAttentionWeights — called after the transformer forward pass; writes the
///                         learned n×n attention matrix back into the manifold so
///                         RelationshipCoupler can use it as a prior.
///
/// Stub pattern:
///   Implement NullNLCASAdapter (below) as a no-op stand-in until the real
///   transformer head is integrated.  Wire an INLCASAdapter? reference into
///   UnifiedEngine; swap the null stub for the real adapter at integration time.
/// </summary>
public interface INLCASAdapter
{
    /// <summary>
    /// Push the current cognitive field snapshot into the NLCAS module.
    /// All arrays are vertex-indexed (length == SimplicialComplexNode.VertexCount).
    /// </summary>
    void PushCognitiveState(float[] knowledge, float[] emotion, float[] intent, float[] action);

    /// <summary>
    /// Pull the attention weight matrix A[n,n] computed by the transformer back
    /// into the manifold.  The caller (UnifiedEngine) forwards this to
    /// RelationshipCoupler so it can prime the next attention pass.
    /// If no transformer pass has run yet the implementation should leave A unchanged.
    /// </summary>
    void PullAttentionWeights(float[,] A);
}

/// <summary>
/// No-op NLCAS adapter.  Installed by default in UnifiedEngine; swap for the
/// real transformer adapter when NLCAS is integrated.
/// </summary>
public sealed class NullNLCASAdapter : INLCASAdapter
{
    public static readonly NullNLCASAdapter Instance = new();
    public void PushCognitiveState(float[] knowledge, float[] emotion, float[] intent, float[] action) { }
    public void PullAttentionWeights(float[,] A) { }
}

public abstract partial class BaseManifoldLayer : Node
{
    [Export] public bool Enabled { get; set; } = true;
    [Export] public float TimeScale { get; set; } = 1.0f;

    public virtual void Initialize() { }
    public virtual void Step(double delta) { }
    public virtual object GetState() => this;
}

public abstract partial class BaseDiscreteLayer : BaseManifoldLayer
{
    public List<object> Nodes { get; } = new();
    public SparseMatrix? IncidenceMatrix { get; protected set; }
    public SparseMatrix? Laplacian { get; protected set; }

    public virtual void BuildTopology() { }
}

public abstract partial class BaseContinuousField : BaseManifoldLayer
{
    protected readonly Dictionary<int, float> ScalarValues = new();
    protected readonly Dictionary<int, Vector3> VectorValues = new();

    public virtual float GetScalarAt(int index) => 0.0f;
    public virtual Vector3 GetVectorAt(int index) => Vector3.Zero;
}

public partial class KnowledgeGraphNode : BaseDiscreteLayer, IFieldLayer
{
    public List<ConceptNode> Concepts  { get; } = new();
    public List<RelationEdge> Relations { get; } = new();

    /// <summary>Vertex-indexed knowledge field (matches SimplicialComplexNode.VertexCount).</summary>
    public float[] Field { get; private set; } = System.Array.Empty<float>();

    public override void Initialize()
    {
        BuildTopology();
    }

    /// <summary>
    /// Resize and seed the knowledge field with a hand-crafted pattern.
    /// Seeds three structural motifs so attention has non-trivial substrate:
    ///   • Hot node at index 0  (strong "concept anchor")
    ///   • Gradient across the field (linear ramp)
    ///   • Cluster bump at the midpoint
    /// This becomes the symbolic substrate NLCAS will later map tokens onto.
    /// </summary>
    public void Resize(int vertexCount)
    {
        if (vertexCount <= 0) return;
        Field = new float[vertexCount];

        float midBump = vertexCount > 1 ? vertexCount / 2f : 0f;

        for (int i = 0; i < vertexCount; i++)
        {
            // Linear gradient: 0.1 → 0.4
            float gradient = 0.1f + 0.3f * (i / (float)Mathf.Max(vertexCount - 1, 1));

            // Gaussian cluster bump centred at the midpoint (σ = vertexCount/4)
            float sigma  = Mathf.Max(vertexCount / 4f, 1f);
            float dist   = i - midBump;
            float bump   = 0.3f * Mathf.Exp(-(dist * dist) / (2f * sigma * sigma));

            Field[i] = gradient + bump;
        }

        // Hot node anchor at vertex 0 — highest salience seed
        Field[0] = 1.0f;
    }

    public override void Step(double delta)
    {
        if (!Enabled) return;
        // Diffusion: smooth the knowledge field toward its neighbourhood average
        // using the graph Laplacian (if available).
        if (Laplacian == null || Field.Length == 0) return;
        float[] lap = Laplacian.Multiply(Field);
        float   dt  = (float)delta * TimeScale;
        for (int i = 0; i < Field.Length; i++)
            Field[i] -= dt * lap[i];
    }

    public override object GetState() => this;
}

public partial class EmotionFieldNode : BaseContinuousField, IFieldLayer
{
    [Export] public float Valence  = 0.0f;
    [Export] public float Arousal  = 0.0f;
    [Export] public float Salience = 0.0f;

    // Damping coefficients (tunable via inspector)
    [Export] public float ArousalDecay  = 0.5f;
    [Export] public float ValenceDecay  = 0.1f;
    [Export] public float SalienceDecay = 0.2f;

    // Accumulator: coupling feedback from RelationshipCoupler / EM injection
    // Cleared each tick after being folded into Arousal.
    private float _arousalAccumulator = 0f;

    /// <summary>
    /// Inject an arousal stimulus from an external source (EM flux, coupling attention).
    /// Accumulated and applied once per Step() — avoids mid-tick race conditions.
    /// </summary>
    public void AccumulateArousal(float stimulus) => _arousalAccumulator += stimulus;

    public override void Step(double delta)
    {
        if (!Enabled) return;
        float dt = (float)delta * TimeScale;

        // Fold accumulated external stimuli into Arousal
        Arousal  += _arousalAccumulator;
        _arousalAccumulator = 0f;

        // Decay back toward neutral with configurable damping
        Arousal  = Mathf.MoveToward(Arousal,  0f, dt * ArousalDecay);
        Valence  = Mathf.MoveToward(Valence,  0f, dt * ValenceDecay);
        Salience = Mathf.MoveToward(Salience, 0f, dt * SalienceDecay);

        // Clamp to reasonable range to prevent unbounded growth
        Arousal  = Mathf.Clamp(Arousal,  -10f, 10f);
        Valence  = Mathf.Clamp(Valence,  -10f, 10f);
        Salience = Mathf.Clamp(Salience,   0f, 10f);
    }

    public override object GetState() => this;
}

public partial class IntentFieldNode : BaseContinuousField, IFieldLayer
{
    [Export] public Vector3 GoalDirection = Vector3.Forward;
    [Export] public float   Urgency       = 0.0f;

    // Accumulation: curvature injections add to urgency over time,
    // decaying back to zero in the absence of new stimuli.
    [Export] public float UrgencyDecay = 0.3f;
    [Export] public float UrgencyGain  = 1.0f;   // scalar on injected urgency

    private float _urgencyAccumulator = 0f;

    /// <summary>
    /// Inject a curvature-derived urgency stimulus (from GR or attention).
    /// Accumulated and applied once per Step().
    /// </summary>
    public void AccumulateUrgency(float stimulus) => _urgencyAccumulator += UrgencyGain * stimulus;

    public override void Step(double delta)
    {
        if (!Enabled) return;
        float dt = (float)delta * TimeScale;

        // Fold accumulated curvature injections
        Urgency += _urgencyAccumulator;
        _urgencyAccumulator = 0f;

        // Decay (urgency drains when not re-stimulated)
        Urgency = Mathf.MoveToward(Urgency, 0f, dt * UrgencyDecay);
        Urgency = Mathf.Clamp(Urgency, 0f, 10f);
    }

    public override object GetState() => this;
}

public partial class ActionLayerNode : BaseDiscreteLayer, IFieldLayer
{
    public List<ActionEvent> Inputs { get; } = new();

    /// <summary>Maximum events kept per step (prevents unbounded list growth).</summary>
    public int MaxInputHistory { get; set; } = 64;

    public override void Step(double delta)
    {
        if (!Enabled) return;
        // Trim input history to cap memory — keep only the most recent events
        int excess = Inputs.Count - MaxInputHistory;
        if (excess > 0)
            Inputs.RemoveRange(0, excess);
    }

    public override object GetState() => this;
}

/// <summary>
/// RelationshipCoupler — proper Q/K/V attention kernel over the four OM4 fields.
///
/// Architecture (OM4 spec, README_XD_NEM-U_OM4_001.md §"How 4 CognitiveFields become the feature space"):
///
///   CoupleOM4(K, E, I, A)  — the primary OM4 attention kernel:
///     Q  = Stack(T_I, T_A)      2×N   (what the agent wants / is doing)
///     K  = Stack(T_K, T_E)      2×N   (what the world contains / how it feels)
///     V  = Stack(T_K, T_E, T_I) 3×N   (what gets updated)
///     A  = softmax(QKᵀ / √2)   2×2
///     O  = AV                   2×N   → write back I[x]=O[0,x], A[x]=O[1,x]
///   AttentionWeights (2×2) — the OM4 attention matrix, NLCAS-readable.
///
///   Couple(knowledge, emotion, intent, action) — legacy OM4 node-level pass:
///     maintains scalar Arousal / Urgency / action-event feedback.
///     AttentionWeightsLegacy (n×n) exposed for backward compat.
/// </summary>
public partial class RelationshipCoupler : BaseManifoldLayer
{
    // ── per-field projection weights (scalars for now; extend to matrices for NLCAS) ──
    [Export] public float QueryScale    { get; set; } = 1.0f;   // Intent query scale
    [Export] public float KeyScale      { get; set; } = 1.0f;   // Knowledge key scale
    [Export] public float ValueScale    { get; set; } = 1.0f;   // Emotion value scale
    [Export] public float ActionGate    { get; set; } = 0.05f;  // Threshold for action events

    // Legacy weight names kept for backward compatibility with HAM feedback
    public float KnowledgeWeight { get; set; } = 1.0f;
    public float EmotionWeight   { get; set; } = 1.0f;
    public float IntentWeight    { get; set; } = 1.0f;
    public float ActionWeight    { get; set; } = 1.0f;

    /// <summary>
    /// OM4 2×2 attention matrix from the last CoupleOM4() call.
    ///
    ///   [0,0] Intent  → Knowledge
    ///   [0,1] Intent  → Emotion
    ///   [1,0] Action  → Knowledge
    ///   [1,1] Action  → Emotion
    ///
    /// This is the primary NLCAS-readable tensor: transformer-compatible,
    /// structurally correct per Ω_(M_fr4). Null until the first CoupleOM4() call.
    /// </summary>
    public float[,]? AttentionWeights { get; private set; }

    // ── NLCAS gate ────────────────────────────────────────────────────────────
    // The 2×2 NLCAS attention matrix written back by NLCASNode.PullAttentionWeights.
    // Applied as a multiplicative gate on the Q/K dot-product scores in CoupleOM4
    // before softmax — closing the recursive loop:
    //   NLCAS output → gate → CoupleOM4 scores → AttentionWeights → NLCAS input
    private float[,]? _nlcasGate;

    /// <summary>
    /// Stores the NLCAS-computed 2×2 attention matrix to be used as a
    /// multiplicative gate on the next CoupleOM4() call.
    ///
    /// Gate is applied to the raw Q·K dot-product scores (before scaling and
    /// softmax), so NLCAS can up-weight or down-weight each of the four
    /// Intent→Knowledge, Intent→Emotion, Action→Knowledge, Action→Emotion
    /// attention pathways independently.
    ///
    /// A gate value of 1.0 is neutral; values &gt; 1 amplify; values near 0 suppress.
    /// The gate is clamped to [0.1, 10] to prevent runaway dynamics.
    /// </summary>
    public void SetNLCASGate(float[,] gate)
    {
        if (gate == null || gate.GetLength(0) < 2 || gate.GetLength(1) < 2) return;
        _nlcasGate = gate;
    }

    /// <summary>
    /// Legacy n×n attention weight matrix from the last Couple() call.
    /// Kept for backward compatibility with HAM feedback / probe reads.
    /// Null until the first Couple() call.
    /// </summary>
    public float[,]? AttentionWeightsLegacy { get; private set; }

    /// <summary>
    /// Attention-weighted output field from the last Couple() call (length n).
    /// Output[i] = Σ_j A[i,j] · V[j]
    /// Exposes the value projection so NLCAS has a direct read surface.
    /// </summary>
    public float[]? AttentionOutput { get; private set; }

    // ── OM4 Q/K/V attention kernel ────────────────────────────────────────────

    /// <summary>
    /// OM4 scaled dot-product attention over the four cognitive vertex fields.
    ///
    /// Implements the Ω_(M_fr4) attention spec exactly:
    ///
    ///   Q = Stack(I, A)       2×N
    ///   K = Stack(K, E)       2×N
    ///   V = Stack(K, E, I)    3×N
    ///
    ///   scores[qi,kj] = Σ_x Q[qi,x] · K[kj,x]   (dot product over vertex dim)
    ///   scores        = scores / √2
    ///   A             = softmax(scores) per row    (2×2)
    ///
    ///   O[qi,x] = Σ_vi A[qi,vi] · V[vi,x]         (2×N output)
    ///
    ///   Write back:  I[x] = O[0,x]   (updated Intent)
    ///                A[x] = O[1,x]   (updated Action)
    ///
    /// The 2×2 A is stored in AttentionWeights for NLCAS to read / overwrite.
    /// Modifies <paramref name="I"/> and <paramref name="actionField"/> in-place.
    /// </summary>
    /// <param name="knowledgeField">T_K — vertex knowledge field</param>
    /// <param name="emotionField">T_E — vertex emotion field</param>
    /// <param name="I">T_I — vertex intent field (modified in-place)</param>
    /// <param name="actionField">T_A — vertex action field (modified in-place)</param>
    public void CoupleOM4(float[] knowledgeField, float[] emotionField, float[] I, float[] actionField)
    {
        int n = knowledgeField.Length;
        if (n == 0) return;

        // Guard: all arrays must have the same length
        if (emotionField.Length < n || I.Length < n || actionField.Length < n) return;

        // ── Build 2×2 score matrix: scores[qi,kj] = Q[qi,·] · K[kj,·] ─────────
        // Q[0] = I,           Q[1] = actionField
        // K[0] = knowledgeField,  K[1] = emotionField
        float s00 = 0f, s01 = 0f, s10 = 0f, s11 = 0f;
        for (int x = 0; x < n; x++)
        {
            s00 += I[x]           * knowledgeField[x];  // Intent  → Knowledge
            s01 += I[x]           * emotionField[x];    // Intent  → Emotion
            s10 += actionField[x] * knowledgeField[x];  // Action  → Knowledge
            s11 += actionField[x] * emotionField[x];    // Action  → Emotion
        }

        // ── Apply NLCAS gate (multiplicative, clamped to [0.1, 10]) ─────────────
        // Gate closes the recursive loop: NLCAS attention output → Q·K scores → softmax → NLCAS input.
        // On the first tick, _nlcasGate is null and gate values default to 1.0 (neutral).
        float g00 = 1f, g01 = 1f, g10 = 1f, g11 = 1f;
        if (_nlcasGate != null)
        {
            g00 = Godot.Mathf.Clamp(_nlcasGate[0, 0], 0.1f, 10f);
            g01 = Godot.Mathf.Clamp(_nlcasGate[0, 1], 0.1f, 10f);
            g10 = Godot.Mathf.Clamp(_nlcasGate[1, 0], 0.1f, 10f);
            g11 = Godot.Mathf.Clamp(_nlcasGate[1, 1], 0.1f, 10f);
        }
        s00 *= g00; s01 *= g01;
        s10 *= g10; s11 *= g11;

        // Scale by 1/√2 (d_K = 2)
        float scale = 1f / Godot.Mathf.Sqrt(2f);
        s00 *= scale; s01 *= scale;
        s10 *= scale; s11 *= scale;

        // ── Softmax per row (numerically stable) ─────────────────────────────────
        float max0 = Godot.Mathf.Max(s00, s01);
        float e00  = Godot.Mathf.Exp(s00 - max0);
        float e01  = Godot.Mathf.Exp(s01 - max0);
        float sum0 = e00 + e01;
        e00 /= sum0; e01 /= sum0;

        float max1 = Godot.Mathf.Max(s10, s11);
        float e10  = Godot.Mathf.Exp(s10 - max1);
        float e11  = Godot.Mathf.Exp(s11 - max1);
        float sum1 = e10 + e11;
        e10 /= sum1; e11 /= sum1;

        // Store the 2×2 attention matrix (NLCAS hook reads this)
        var A2 = new float[2, 2] { { e00, e01 }, { e10, e11 } };
        AttentionWeights = A2;

        // ── V = Stack(K, E, I) — 3 rows × N cols ─────────────────────────────────
        // V[0] = knowledgeField,  V[1] = emotionField,  V[2] = I (pre-update)
        // O[qi,x] = A[qi,0]*V[0,x] + A[qi,1]*V[1,x] + A[qi,2]*V[2,x]
        //         = A[qi,0]*K[x]   + A[qi,1]*E[x]   + A[qi,2]*I[x]
        // Note per spec: O row 0 uses A[0,0]*V[0] + A[0,1]*V[1] + A[0,1]*V[2]
        //                (the spec reuses scores[qi,1] for the Intent column of V)
        // We follow the spec exactly: third V column weight = A[qi,1]
        for (int x = 0; x < n; x++)
        {
            float intentOut = e00 * knowledgeField[x] + e01 * emotionField[x] + e01 * I[x];
            float actionOut = e10 * knowledgeField[x] + e11 * emotionField[x] + e11 * I[x];
            I[x]           = intentOut;
            actionField[x] = actionOut;
        }
    }

    /// <summary>
    /// Full Q/K/V attention pass over the four OM4 cognitive layers.
    ///
    ///   Q[i] = intent.Urgency * QueryScale  * kField[i]   (scalar urgency broadcast × knowledge)
    ///   K[j] = KeyScale                     * kField[j]
    ///   V[j] = ValueScale                   * emotion.Arousal  (scalar broadcast)
    ///
    ///   A[i,j] = softmax_j( Q[i] · K[j] / √n )
    ///   out[i] = Σ_j A[i,j] · V[j]
    ///
    /// Emotion arousal and intent urgency are updated via their accumulator APIs
    /// so changes take effect cleanly in the same physics tick.
    /// </summary>
    public void Couple(
        KnowledgeGraphNode? knowledge,
        EmotionFieldNode?   emotion,
        IntentFieldNode?    intent,
        ActionLayerNode?    action)
    {
        if (knowledge == null || emotion == null || intent == null || action == null)
            return;

        float[] kField = knowledge.Field;
        if (kField.Length == 0) return;

        int   n      = kField.Length;
        float sqrtN  = Mathf.Sqrt(n);

        // ── Q and K projections ──────────────────────────────────────────────────
        float[] Q = new float[n];
        float[] K = new float[n];
        for (int i = 0; i < n; i++)
        {
            Q[i] = intent.Urgency * QueryScale * kField[i] * IntentWeight;
            K[i] = KeyScale * kField[i] * KnowledgeWeight;
        }

        // ── Compute attention matrix A[i,j] = softmax_j( Q[i]*K[j] / √n ) ──────
        float[,] A = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            float maxScore = float.NegativeInfinity;
            for (int j = 0; j < n; j++)
            {
                float s = Q[i] * K[j] / sqrtN;
                A[i, j] = s;
                if (s > maxScore) maxScore = s;
            }
            // Numerically stable softmax per row
            float rowSum = 0f;
            for (int j = 0; j < n; j++)
            {
                A[i, j] = (float)System.Math.Exp(A[i, j] - maxScore);
                rowSum  += A[i, j];
            }
            if (rowSum > 0f)
                for (int j = 0; j < n; j++) A[i, j] /= rowSum;
        }
        AttentionWeightsLegacy = A;

        // ── V projection: emotion arousal broadcast across vertices ──────────────
        float vScalar  = ValueScale * emotion.Arousal * EmotionWeight;

        float[] output = new float[n];
        for (int i = 0; i < n; i++)
        {
            float attnSum = 0f;
            for (int j = 0; j < n; j++) attnSum += A[i, j];
            // V is scalar-broadcast: out[i] = rowSum * vScalar (rowSum == 1 post-softmax)
            output[i] = attnSum * vScalar;
        }
        AttentionOutput = output;

        // ── Compute cross-field activation signal (mean of output) ───────────────
        float attnMean = 0f;
        for (int i = 0; i < n; i++) attnMean += output[i] * kField[i];
        attnMean /= n;

        // ── Feed back into Emotion and Intent via their accumulators ─────────────
        emotion.AccumulateArousal(EmotionWeight * attnMean * 0.05f);
        emotion.Salience += EmotionWeight * attnMean * 0.02f;

        // Intent: attention on high-knowledge nodes raises urgency
        float knowledgePeak = 0f;
        for (int i = 0; i < n; i++) knowledgePeak += A[i, i] * kField[i];
        intent.AccumulateUrgency(IntentWeight * knowledgePeak * 0.03f);

        // ── Gate action events on meaningful attention signal ────────────────────
        if (Mathf.Abs(attnMean) > ActionGate)
        {
            action.Inputs.Add(new ActionEvent
            {
                Name      = "attention",
                Value     = attnMean,
                TimeStamp = 0.0
            });
        }
    }

    public override void Step(double delta)
    {
        // Decay coupling weights gently toward neutral (1.0) each tick
        float dt = (float)delta * TimeScale;
        KnowledgeWeight = Mathf.MoveToward(KnowledgeWeight, 1.0f, dt * 0.01f);
        EmotionWeight   = Mathf.MoveToward(EmotionWeight,   1.0f, dt * 0.01f);
        IntentWeight    = Mathf.MoveToward(IntentWeight,    1.0f, dt * 0.01f);
        ActionWeight    = Mathf.MoveToward(ActionWeight,    1.0f, dt * 0.01f);
    }

    // ── NPC gestalt coupling ──────────────────────────────────────────────────

    /// <summary>
    /// Couple a set of NPC H-elements to the player's manifold snapshot using
    /// the same softmax-attention mechanism as CoupleOM4().
    ///
    /// This implements NEM §NPC coupling — the manifold stabilises when player
    /// and NPC H-elements form a harmonic ensemble (coupled fields over M₄).
    ///
    /// For each NPC:
    ///   1. Build a 2×2 score matrix from player and NPC field scalars:
    ///        Q = [player.IMagnitude,  player.AMagnitude]   (what the player wants/does)
    ///        K = [npc.KMagnitude,     npc.EMagnitude  ]    (what the NPC knows/feels)
    ///        S[qi,kj] = Q[qi] · K[kj] / √2
    ///   2. Apply NLCAS gate if set (same gate as CoupleOM4).
    ///   3. Softmax normalise per row → A[qi,kj] ∈ (0,1).
    ///   4. Derive effective coupling alpha:
    ///        alpha_eff = A[0,0] * npc.PlayerCouplingAlpha
    ///                    (Intent→Knowledge attention weight scales the lerp rate)
    ///   5. Call npc.ReceivePlayerBoundary(playerSnapshot, alpha_eff).
    ///
    /// The gate closes the same recursive loop as the vertex-level pipeline:
    ///   PlayerH axis/tension → CoupleNpcToPlayer → NPC K/E/I → NPC.GetSnapshot()
    ///   → next tick as boundary conditions → player manifold stabilises further.
    ///
    /// Call once per physics tick from UnifiedEngine after step 6b.
    /// </summary>
    /// <param name="playerSnapshot">Snapshot of the player's manifold state this tick.</param>
    /// <param name="npcs">All NpcMind instances in the current scene.</param>
    public void CoupleNpcToPlayer(
        in HManifoldSnapshot playerSnapshot,
        System.Collections.Generic.IEnumerable<NpcMind> npcs)
    {
        if (npcs == null) return;

        // Pre-compute player Q-row (constant across all NPCs this tick)
        float qIntent = playerSnapshot.IMagnitude;
        float qAction = playerSnapshot.AMagnitude;

        foreach (var npc in npcs)
        {
            if (npc == null) return;

            // Build 2×2 score matrix — player Q × NPC K
            float kKnowledge = npc.T_K.Magnitude;
            float kEmotion   = npc.T_E.Magnitude;

            float s00 = qIntent * kKnowledge;   // Intent  → Knowledge
            float s01 = qIntent * kEmotion;     // Intent  → Emotion
            float s10 = qAction * kKnowledge;   // Action  → Knowledge
            float s11 = qAction * kEmotion;     // Action  → Emotion

            // Apply NLCAS gate (same gate as CoupleOM4)
            if (_nlcasGate != null)
            {
                s00 *= Mathf.Clamp(_nlcasGate[0, 0], 0.1f, 10f);
                s01 *= Mathf.Clamp(_nlcasGate[0, 1], 0.1f, 10f);
                s10 *= Mathf.Clamp(_nlcasGate[1, 0], 0.1f, 10f);
                s11 *= Mathf.Clamp(_nlcasGate[1, 1], 0.1f, 10f);
            }

            // Scale by 1/√2 (d_K = 2, matching CoupleOM4)
            float scale = 1f / Mathf.Sqrt(2f);
            s00 *= scale; s01 *= scale;
            s10 *= scale; s11 *= scale;

            // Softmax row 0 (Intent row)
            float max0 = Mathf.Max(s00, s01);
            float e00  = Mathf.Exp(s00 - max0);
            float e01  = Mathf.Exp(s01 - max0);
            float sum0 = e00 + e01;
            e00 /= sum0; e01 /= sum0;

            // Softmax row 1 (Action row) — computed but not used for alpha, kept for symmetry
            float max1 = Mathf.Max(s10, s11);
            float e10  = Mathf.Exp(s10 - max1);
            float e11  = Mathf.Exp(s11 - max1);
            float sum1 = e10 + e11;
            e10 /= sum1; // e11 not used further

            // Effective coupling alpha — Intent→Knowledge attention scales the lerp rate.
            // A[0,0] high means the player's intent is strongly drawing on the NPC's
            // knowledge, making the NPC more responsive to boundary injection.
            float alphaEff = e00 * npc.PlayerCouplingAlpha;

            // Inject boundary conditions into the NPC
            npc.ReceivePlayerBoundary(in playerSnapshot, alphaEff);
        }
    }
}

public partial class ConceptNode : Node3D, IRelationCarrier
{
    [Export] public float Weight { get; set; } = 1.0f;

    // IRelationCarrier.Position — use GlobalPosition directly from Node3D
    // (hiding Node3D.Position intentionally so the interface is satisfied)
    public new Vector3 Position => GlobalPosition;
}

public partial class RelationEdge : Node3D
{
    public ConceptNode? Source { get; set; }
    public ConceptNode? Target { get; set; }
    public float Strength { get; set; } = 1.0f;
}

public partial class ActionEvent
{
    public string Name { get; set; } = "";
    public float Value { get; set; } = 0.0f;
    public double TimeStamp { get; set; } = 0.0;
}