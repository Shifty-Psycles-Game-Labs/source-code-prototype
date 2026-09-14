/// <summary>
/// HManifoldSnapshot — value-type boundary snapshot of a single H-element
/// on M₄ (player or NPC).
///
/// Produced by PlayerHElement.GetSnapshot() and NpcMind.GetSnapshot() each
/// physics tick.  RelationshipCoupler.CoupleNpcToPlayer() reads this to drive
/// gestalt stabilisation without holding a live node reference.
///
/// All fields match the public scalars of the four OM4 sub-objects:
///   K* → KnowledgeGraph
///   E* → EmotionState
///   I* → IntentState
///   A* → ActionAccumulator
/// </summary>
public struct HManifoldSnapshot
{
    // ── Knowledge (T_K) ────────────────────────────────────────────────────────
    /// <summary>Semantic breadth (node count).</summary>
    public float KMagnitude;
    /// <summary>Semantic coherence (mean mastery).</summary>
    public float KPhase;

    // ── Emotion (T_E) ──────────────────────────────────────────────────────────
    /// <summary>Total affective load: |Valence| + Arousal + Uncertainty.</summary>
    public float EMagnitude;
    /// <summary>Emotional polarity (= Valence).</summary>
    public float EPhase;
    /// <summary>Valence: −1 (fear/aversion) … +1 (joy/approach).</summary>
    public float EValence;
    /// <summary>Arousal: 0 (calm) … 1 (excited/stressed).</summary>
    public float EArousal;
    /// <summary>Uncertainty: 0 (certain) … 1 (disoriented).</summary>
    public float EUncertainty;

    // ── Intent (T_I) ───────────────────────────────────────────────────────────
    /// <summary>Effective goal-drive pressure: |IntentDirection| × Commitment.</summary>
    public float IMagnitude;
    /// <summary>Goal polarity (= IntentDirection).</summary>
    public float IPhase;
    /// <summary>Direction: −1 (avoid) … +1 (pursue).</summary>
    public float IIntentDirection;
    /// <summary>Commitment: 0 (undecided) … 1 (fully committed).</summary>
    public float ICommitment;

    // ── Action (T_A) ───────────────────────────────────────────────────────────
    /// <summary>Total active engagement time.</summary>
    public float AMagnitude;
    /// <summary>Void / idle component.</summary>
    public float APhase;

    // ── Manifold summary ───────────────────────────────────────────────────────
    /// <summary>Name of the dominant dichotomy axis this tick.</summary>
    public string DominantAxis;
    /// <summary>Total HAM torsion scalar this tick.</summary>
    public float  TotalTorsion;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// IHElement — contract satisfied by any cognitive H-element node (player or NPC).
///
/// Allows RelationshipCoupler.CoupleNpcToPlayer() and other coupling code to
/// work uniformly over player and NPC H-elements without a concrete type dependency.
/// </summary>
public interface IHElement
{
    /// <summary>Returns the current field snapshot for this tick.</summary>
    HManifoldSnapshot GetSnapshot();

    /// <summary>Name of the dominant dichotomy axis this tick.</summary>
    string DominantAxis { get; }

    /// <summary>Total manifold torsion scalar this tick.</summary>
    float TotalTorsion { get; }
}
