using Godot;
using System.Collections.Generic;

// ── CogField axis enum ─────────────────────────────────────────────────────
/// <summary>
/// The four OM4 cognitive axes mapped to DichotomyOperator constants.
/// </summary>
public enum CogField
{
    Knowledge = DichotomyOperator.KNOWLEDGE,   // 1
    Emotion   = DichotomyOperator.EMOTION,     // 2
    Intent    = DichotomyOperator.INTENT,      // 0
    Action    = DichotomyOperator.ACTION       // 3
}

// ── OM4 sub-objects ────────────────────────────────────────────────────────
// Plain C# objects owned by PlayerHElement (or NpcMind).
// They do not extend Node and carry no Godot lifecycle overhead.

// ─────────────────────────────────────────────────────────────────────────
/// <summary>
/// KnowledgeGraph (T_K) — discrete semantic field on M₄ (H4.md §"Knowledge…
/// discrete symbolic/semantic field").
///
/// Each node tracks a single fact, location, or NPC encountered by the player.
///   Mastery    — depth of understanding.   Grows through repeated engagement.
///   Familiarity— surface recognition.      Grows on first encounter; decays slowly.
///
/// Magnitude = node count (semantic breadth).
/// Phase     = mean mastery   (semantic coherence — how well the player knows what they know).
///
/// UpdateGated() applies intent-boost and emotion-debuff to the mastery evolution
/// of every node each frame.
/// </summary>
public class KnowledgeGraph
{
    public class Node
    {
        public string      Id          = "";
        public float       Mastery     = 0.1f;   // 0–1
        public float       Familiarity = 0.1f;   // 0–1
        public List<string> Edges      = new();
    }

    private readonly Dictionary<string, Node> _nodes = new();

    /// <summary>Semantic breadth — number of known concepts.</summary>
    public float Magnitude => _nodes.Count;

    /// <summary>Semantic coherence — average mastery across all known nodes.</summary>
    public float Phase => ComputePhase();

    /// <summary>Read-only access for NPC coupling.</summary>
    public IEnumerable<Node> Nodes => _nodes.Values;

    /// <summary>
    /// Record a knowledge event.  New concepts start at minimum mastery;
    /// repeated encounters raise Familiarity instead.
    /// </summary>
    public void AddKnowledge(string id)
    {
        if (!_nodes.ContainsKey(id))
            _nodes[id] = new Node { Id = id, Mastery = 0.1f, Familiarity = 0.1f };
        else
            _nodes[id].Familiarity = Mathf.Clamp(_nodes[id].Familiarity + 0.05f, 0f, 1f);
    }

    /// <summary>
    /// Gate mastery evolution by Emotion debuff and Intent boost (NEM §K-gate).
    ///
    ///   dMastery/dt = 0.01 · (intentBoost − valenceDebuff)
    ///
    /// Positive intent drives mastery up; negative valence (fear, frustration)
    /// drags it down.  Familiarity is unchanged here — it only shifts via AddKnowledge().
    /// </summary>
    public void UpdateGated(EmotionState E, IntentState I, double dt)
    {
        float debuff      = E.ValenceDebuff();
        float intentBoost = I.IntentBoost();
        float dMastery    = 0.01f * (intentBoost - debuff) * (float)dt;

        foreach (var node in _nodes.Values)
            node.Mastery = Mathf.Clamp(node.Mastery + dMastery, 0f, 1f);
    }

    private float ComputePhase()
    {
        if (_nodes.Count == 0) return 0f;
        float sum = 0f;
        foreach (var n in _nodes.Values) sum += n.Mastery;
        return sum / _nodes.Count;
    }
}

// ─────────────────────────────────────────────────────────────────────────
/// <summary>
/// EmotionState (T_E) — continuous affective field (H4.md §"Emotion functions
/// as the Debuff… represents the Body").
///
///   Valence     — −1 (fear/aversion) to +1 (joy/approach)
///   Arousal     —  0 (calm)          to  1 (excited/stressed)
///   Uncertainty —  0 (certain)       to  1 (disoriented)
///
/// Magnitude = |Valence| + Arousal + Uncertainty   (total affective load)
/// Phase     = Valence                             (emotional polarity)
///
/// ValenceDebuff() exposes the combined negative signal used by KnowledgeGraph
/// and IntentState to model cognitive load under stress.
/// </summary>
public class EmotionState
{
    public float Valence     = 0.0f;   // −1 … +1
    public float Arousal     = 0.1f;   //  0 … +1
    public float Uncertainty = 0.3f;   //  0 … +1

    /// <summary>Total affective load.</summary>
    public float Magnitude => Mathf.Abs(Valence) + Arousal + Uncertainty;

    /// <summary>Emotional polarity (used as phase in HAM).</summary>
    public float Phase => Valence;

    /// <summary>
    /// Evolve emotion from quest context (NEM §Emotion update):
    ///   • Progress (IntentBoost) reduces Uncertainty.
    ///   • Intent direction drifts Valence toward the goal polarity.
    ///   • Arousal decays toward 0 unless sustained by external injections.
    /// </summary>
    public void UpdateFromContext(KnowledgeGraph K, IntentState I, double dt)
    {
        float dtf = (float)dt;

        // Progress reduces uncertainty (knowing what to do next calms the field)
        Uncertainty = Mathf.Clamp(Uncertainty - 0.01f * I.IntentBoost() * dtf, 0f, 1f);

        // Valence drifts toward intent direction (doing what you want feels good)
        Valence = Mathf.Clamp(Valence + 0.02f * I.IntentDirection * dtf, -1f, 1f);

        // Arousal decays toward homeostasis
        Arousal = Mathf.Clamp(Arousal - 0.005f * dtf, 0f, 1f);
    }

    /// <summary>
    /// Combined cognitive-load debuff: negative valence × arousal → stress penalty.
    ///   debuff = clamp(−Valence·0.5 + Arousal·0.5, 0, 1)
    /// High arousal + negative valence = maximum debuff (panic, fear).
    /// </summary>
    public float ValenceDebuff() =>
        Mathf.Clamp(-Valence * 0.5f + Arousal * 0.5f, 0f, 1f);

    /// <summary>Inject an affective event (combat hit, success, dialogue shock).</summary>
    public void Inject(float arousalDelta, float valenceDelta = 0f)
    {
        Arousal = Mathf.Clamp(Arousal + arousalDelta, 0f, 1f);
        Valence = Mathf.Clamp(Valence + valenceDelta, -1f, 1f);
    }
}

// ─────────────────────────────────────────────────────────────────────────
/// <summary>
/// IntentState (T_I) — goal trajectory and commitment (H4.md §"Thus we can
/// divine, do they intend or not +1 or −1 on completing this quest?").
///
///   IntentDirection — −1 (avoid/flee) to +1 (pursue/complete)
///   Commitment      —  0 (undecided)  to  1 (fully committed)
///
/// Magnitude = |IntentDirection| · Commitment   (effective goal pressure)
/// Phase     = IntentDirection                  (goal polarity, used by HAM)
///
/// IntentBoost() is the positive goal-drive term consumed by KnowledgeGraph
/// and EmotionState.
/// </summary>
public class IntentState
{
    public float IntentDirection = 0f;   // −1 … +1
    public float Commitment      = 0.1f; //  0 … +1

    /// <summary>Effective goal-drive pressure.</summary>
    public float Magnitude => Mathf.Abs(IntentDirection) * Commitment;

    /// <summary>Goal polarity (used as phase in HAM).</summary>
    public float Phase => IntentDirection;

    /// <summary>
    /// Evolve intent from action history (NEM §Intent update):
    ///   • InteractionRate biases IntentDirection toward +1 (active pursuit).
    ///   • Consistency grows Commitment (sustained effort = growing resolve).
    /// </summary>
    public void UpdateFromQuestsAndActions(ActionAccumulator A, double dt)
    {
        float dtf = (float)dt;

        // Interaction rate drives intent toward positive (player is engaging)
        float interactionBias = A.InteractionRate * 0.1f;
        IntentDirection = Mathf.Clamp(IntentDirection + interactionBias * dtf, -1f, 1f);

        // Consistent action builds commitment
        Commitment = Mathf.Clamp(Commitment + 0.05f * A.Consistency * dtf, 0f, 1f);
    }

    /// <summary>
    /// Positive goal-drive scalar (0–1).
    ///   IntentBoost = Commitment · |IntentDirection|
    /// </summary>
    public float IntentBoost() => Commitment * Mathf.Abs(IntentDirection);
}

// ─────────────────────────────────────────────────────────────────────────
/// <summary>
/// ActionAccumulator (T_A) — aggregates raw player input into a structured
/// action field (H4.md §"Action as the player input… Idle gives us the void").
///
/// Tracks cumulative time spent moving, looking, interacting, and idling.
/// All rates are raw accumulated seconds — callers normalise if needed.
///
///   MoveRate        — time spent moving  (seconds accumulated)
///   LookRate        — time spent looking (seconds accumulated)
///   InteractionRate — time spent interacting
///   IdleRate        — time spent idle    (the "void" component)
///
/// Magnitude = MoveRate + LookRate + InteractionRate   (total active time)
/// Phase     = IdleRate                                (void component)
/// Consistency = 1 − IdleRate / max(1, Magnitude+IdleRate)  (active fraction)
/// </summary>
public class ActionAccumulator
{
    public float MoveRate        = 0f;
    public float LookRate        = 0f;
    public float InteractionRate = 0f;
    public float IdleRate        = 0f;

    /// <summary>Total active engagement time.</summary>
    public float Magnitude => MoveRate + LookRate + InteractionRate;

    /// <summary>Void / absence-of-action component (used as phase in HAM).</summary>
    public float Phase => IdleRate;

    /// <summary>Active fraction of total session time: 1 when never idle, 0 when always idle.</summary>
    public float Consistency
    {
        get
        {
            float total = Magnitude + IdleRate;
            return total > 0f ? Magnitude / total : 0f;
        }
    }

    /// <summary>
    /// AI-driven rate modulation from NpcMind.ApplyBehaviourModulation().
    ///
    /// Scales all active rates by <paramref name="gain"/> (CognitiveGain from regulator),
    /// then adds an attention-proportional reactivity boost to InteractionRate.
    ///
    ///   MoveRate        *= gain
    ///   LookRate        *= gain
    ///   InteractionRate *= gain, then += attention * 0.1
    ///
    /// IdleRate is not scaled — the void component is not subject to modulation.
    /// </summary>
    public void ModulateAI(float gain, float attention)
    {
        MoveRate        *= gain;
        LookRate        *= gain;
        InteractionRate *= gain;

        // High attention → NPC becomes more reactive (seeks interaction)
        InteractionRate += attention * 0.1f;

        // Keep rates non-negative
        MoveRate        = Mathf.Max(0f, MoveRate);
        LookRate        = Mathf.Max(0f, LookRate);
        InteractionRate = Mathf.Max(0f, InteractionRate);
    }

    /// <summary>
    /// Accumulate input buckets each physics tick.
    /// Time is added to the appropriate bucket; idle only accumulates when
    /// all three active actions are absent.
    /// </summary>
    public void SampleInput(double dt)
    {
        float dtf = (float)dt;

        bool moved      = Input.IsActionPressed("move_forward")
                       || Input.IsActionPressed("move_back")
                       || Input.IsActionPressed("move_left")
                       || Input.IsActionPressed("move_right");
        bool looked     = Input.IsActionPressed("look_left")
                       || Input.IsActionPressed("look_right")
                       || Input.IsActionPressed("look_up")
                       || Input.IsActionPressed("look_down");
        bool interacted = Input.IsActionPressed("interact");

        if (moved)      MoveRate        += dtf;
        if (looked)     LookRate        += dtf;
        if (interacted) InteractionRate += dtf;

        if (!moved && !looked && !interacted)
            IdleRate += dtf;
    }
}

// ── PlayerHElement ─────────────────────────────────────────────────────────

/// <summary>
/// PlayerHElement — the player's rank-4 cognitive H-element on M₄.
///
/// Runs the full OM4 pipeline in scalar (player-local) form each physics tick:
///   1. Sample input        → ActionAccumulator
///   2. Update Intent       from quests + action history
///   3. Update Emotion      from context (K, I)
///   4. Gate Knowledge      by Emotion &amp; Intent
///   5. Recompute HAM       from scalar magnitudes + phases
///   6. Emit signal         PlayerManifoldStateChanged(axis, torsion)
///
/// The emitted signal drives two downstream consumers:
///   • UnifiedEngine.ApplyPlayerManifoldGate() — throttles Magic/GR operators
///     when player torsion exceeds ThrottleThreshold.
///   • RelationshipCoupler.CoupleNpcToPlayer() — boundary-condition injection
///     into NPC H-elements (gestalt stabilisation).
///
/// Expose a snapshot via GetSnapshot() so NPC coupling code can read all four
/// field scalars without holding a reference to the full node.
/// </summary>
public partial class PlayerHElement : Node, IHElement
{
    // ── exports ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Manifold torsion amplitude above this value triggers OM4 operator throttling in
    /// UnifiedEngine.  Tune to match your typical play-session torsion range.
    /// </summary>
    [Export] public float ThrottleThreshold { get; set; } = 2.0f;

    // ── Core fields on M4 ─────────────────────────────────────────────────────
    public KnowledgeGraph    T_K { get; private set; } = null!;
    public EmotionState      T_E { get; private set; } = null!;
    public IntentState       T_I { get; private set; } = null!;
    public ActionAccumulator T_A { get; private set; } = null!;

    /// <summary>Scalar HAM — cross-field torsion matrix (no Laplacian needed).</summary>
    public HarmonicAttentionMatrix Attention { get; private set; } = null!;

    // ── diagnostics ───────────────────────────────────────────────────────────
    /// <summary>Name of the dominant cognitive dichotomy axis this tick.</summary>
    public string DominantAxis { get; private set; } = "n/a";
    /// <summary>Total manifold torsion scalar this tick.</summary>
    public float  TotalTorsion { get; private set; } = 0f;

    // ── signal ────────────────────────────────────────────────────────────────
    [Signal]
    public delegate void PlayerManifoldStateChangedEventHandler(string axis, float torsion);

    // ── lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        T_K       = new KnowledgeGraph();
        T_E       = new EmotionState();
        T_I       = new IntentState();
        T_A       = new ActionAccumulator();
        Attention = new HarmonicAttentionMatrix();
    }

    public override void _PhysicsProcess(double delta)
    {
        // 1. Sample player input into Action field
        T_A.SampleInput(delta);

        // 2. Update Intent from quests + action history
        T_I.UpdateFromQuestsAndActions(T_A, delta);

        // 3. Update Emotion from context events
        T_E.UpdateFromContext(T_K, T_I, delta);

        // 4. Gate Knowledge by Emotion & Intent
        T_K.UpdateGated(T_E, T_I, delta);

        // 5. Recompute cross-field HAM.
        //    Array order: [Intent, Knowledge, Emotion, Action] = DichotomyOperator indices.
        var magnitudes = new float[4]
        {
            T_I.Magnitude,
            T_K.Magnitude,
            T_E.Magnitude,
            T_A.Magnitude
        };
        var phases = new float[4]
        {
            T_I.Phase,
            T_K.Phase,
            T_E.Phase,
            T_A.Phase
        };
        Attention.RecomputeScalar(magnitudes, phases);

        // 6. Publish dominant axis + torsion
        DominantAxis = Attention.DominantAxisName();
        TotalTorsion = Attention.TotalTorsion();

        EmitSignal(SignalName.PlayerManifoldStateChanged, DominantAxis, TotalTorsion);
    }

    // ── NPC coupling API ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight snapshot of all four field scalars.
    /// NPC coupling code reads this each tick instead of holding a node reference.
    /// </summary>
    public HManifoldSnapshot GetSnapshot() => new HManifoldSnapshot
    {
        KMagnitude        = T_K.Magnitude,
        KPhase            = T_K.Phase,
        EMagnitude        = T_E.Magnitude,
        EPhase            = T_E.Phase,
        EValence          = T_E.Valence,
        EArousal          = T_E.Arousal,
        EUncertainty      = T_E.Uncertainty,
        IMagnitude        = T_I.Magnitude,
        IPhase            = T_I.Phase,
        IIntentDirection  = T_I.IntentDirection,
        ICommitment       = T_I.Commitment,
        AMagnitude        = T_A.Magnitude,
        APhase            = T_A.Phase,
        DominantAxis      = DominantAxis,
        TotalTorsion      = TotalTorsion,
    };
}
