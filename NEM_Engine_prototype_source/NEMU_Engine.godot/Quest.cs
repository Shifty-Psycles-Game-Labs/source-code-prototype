/// <summary>
/// QuestState — the three narrative outcomes produced by QuestEngine.Evaluate().
///
///   Advancing — player and NPC intent are positively aligned (product > +0.6).
///              The quest moves forward; dialogue tone becomes constructive.
///   Blocked   — intent vectors are opposed (product &lt; −0.4).
///              The NPC turns hostile or withdraws; quest progress stalls.
///   Neutral   — alignment is weak in either direction.
///              No narrative change this tick.
/// </summary>
public enum QuestState
{
    Neutral,    // |alignment| ≤ threshold — no momentum
    Advancing,  // player + NPC intents aligned  (product > +0.6)
    Blocked,    // player + NPC intents opposed   (product < −0.4)
}

// ── Quest ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Quest — a single named objective with a 0–1 progress scalar.
///
/// Progress is driven externally by game logic (interaction events, item pickups,
/// spell casts, etc.) and read by QuestEngine.Evaluate() to modulate NPC behaviour.
///
/// The intent skeleton is intentionally minimal — later versions will carry
/// a list of sub-objectives, prerequisite flags, and reward descriptors.
/// </summary>
public class Quest
{
    /// <summary>Unique identifier — matches dialogue-database keys where relevant.</summary>
    public string Id          = "";

    /// <summary>Human-readable one-liner shown in the player's journal.</summary>
    public string Description = "";

    /// <summary>Overall completion fraction [0, 1].  1 = fully resolved.</summary>
    public float  Progress    = 0f;
}
