/// <summary>
/// QuestEngine — evaluates a single quest's narrative state from the
/// intent alignment between the player and a target NPC.
///
/// Theory (NEM §Intent field / quest branching):
///   Each cognitive entity carries an IntentDirection ∈ [−1, +1].
///   The alignment scalar is the raw product:
///     alignment = player.IIntentDirection · npc.T_I.IntentDirection
///
///   Alignment > +0.6  → both intents are co-directional and strong.
///                        The quest advances; the NPC leans constructive.
///   Alignment &lt; −0.4  → intents are opposed.
///                        The NPC becomes hostile or uncooperative.
///   Otherwise         → weak or ambiguous alignment (neutral tick).
///
/// Thresholds are intentionally asymmetric: it is harder to advance a quest
/// (requires strong mutual intent) than to block it (mild opposition suffices).
/// This mirrors the asymmetry in the H-element where Commitment amplifies
/// intent direction but does not recover quickly from opposition.
///
/// NPC commitment is factored in by reading T_I.IntentDirection directly
/// (already scaled by T_I.Commitment through IntentBoost — but here we want
/// the raw direction, so commitment is an implicit modifier via the boundary
/// coupling already applied in NpcMind.ReceivePlayerBoundary()).
///
/// Usage:
/// <code>
///   var result = _questEngine.Evaluate(playerSnapshot, npcMind, activeQuest);
///   if (result == QuestState.Advancing) activeQuest.Progress += delta * rate;
/// </code>
/// </summary>
public class QuestEngine
{
    // ── thresholds ────────────────────────────────────────────────────────────

    /// <summary>Alignment product above this value advances the quest.</summary>
    private const float AdvancingThreshold = 0.6f;

    /// <summary>Alignment product below this value blocks the quest.</summary>
    private const float BlockedThreshold = -0.4f;

    // ── public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate quest state for one (player, NPC, quest) triplet.
    ///
    /// The <paramref name="quest"/> parameter is present for future extensions
    /// (sub-objective guards, completion caps) but is not consumed in this
    /// skeleton iteration.
    /// </summary>
    /// <param name="player">Current player manifold snapshot.</param>
    /// <param name="npc">The NPC whose intent is compared against the player's.</param>
    /// <param name="quest">The quest being evaluated (reserved for sub-objective logic).</param>
    public QuestState Evaluate(
        in HManifoldSnapshot player,
        NpcMind npc,
        Quest quest)
    {
        float pIntent = player.IIntentDirection;
        float nIntent = npc.T_I.IntentDirection;

        float alignment = pIntent * nIntent;

        if (alignment > AdvancingThreshold)
            return QuestState.Advancing;

        if (alignment < BlockedThreshold)
            return QuestState.Blocked;

        return QuestState.Neutral;
    }
}
