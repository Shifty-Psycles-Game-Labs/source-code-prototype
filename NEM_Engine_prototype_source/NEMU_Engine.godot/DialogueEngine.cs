/// <summary>
/// DialogueEngine — generates a single NPC dialogue line each call.
///
/// Driven entirely by the NPC's live H-element state (no per-NPC switch in
/// the caller — the NPC type is resolved from its C# class name so new
/// characters only require entries in DialogueDatabase and, optionally,
/// a tone-selection override).
///
/// Pipeline:
///   1. Read peak attention weight from the NPC's HarmonicAttentionMatrix.
///   2. Read T_E.Valence and T_I.IntentDirection from the NPC's H-element.
///   3. SelectTone() maps those three scalars to a DialogueTone.
///   4. DialogueDatabase.GetLine() returns the canned line for (npcId, tone).
///
/// Attention weight threshold constants match the NPC personality profiles
/// defined in NpcThalegrim._Ready() and NpcBumble._Ready().
/// </summary>
public class DialogueEngine
{
    // ── pseudo-LLM retrieval index (Option N) ─────────────────────────────────

    /// <summary>
    /// Optional pseudo-LLM retrieval index.
    ///
    /// When non-null and non-empty, <see cref="GenerateLine"/> uses
    /// <see cref="PseudoLLMIndex.Retrieve"/> instead of the canned
    /// <see cref="DialogueDatabase"/> lookup.  The canned system remains fully
    /// functional — set this to null (the default) to keep original behaviour.
    ///
    /// Populate via:
    ///   engine.TextIndex = new PseudoLLMIndex();
    ///   engine.TextIndex.AddChunk("fb_001", "Long rant about harmonic structure…");
    ///   engine.TextIndex.AddChunk("quip_007", "You're poking the manifold with a stick again.");
    /// </summary>
    public PseudoLLMIndex? TextIndex { get; set; }

    // ── thresholds (match NPC personality tuning) ─────────────────────────────

    /// <summary>
    /// Thalegrim: attention weight above this drives DialogueTone.Critical.
    /// Calibrated against Thalegrim's low coupling alpha (0.05) — the threshold
    /// is deliberately low so it fires even when manifold tension is modest.
    /// </summary>
    private const float ThalegrimCriticalAttThreshold  = 0.6f;

    /// <summary>
    /// Thalegrim: intent direction above this drives DialogueTone.Lecturing.
    /// Matches his baseline IntentDirection = 0.8 so he lectures by default.
    /// </summary>
    private const float ThalegrimLecturingIntentThreshold = 0.7f;

    /// <summary>
    /// Thalegrim: valence below this drives DialogueTone.Grumpy.
    /// His baseline valence is −0.2 so this fires from the start.
    /// </summary>
    private const float ThalegrimGrumpyValenceThreshold = -0.3f;

    /// <summary>
    /// Bumble: attention weight above this drives DialogueTone.Clingy.
    /// Calibrated against Bumble's high coupling alpha (0.4).
    /// </summary>
    private const float BumbleClingyAttThreshold = 0.7f;

    /// <summary>
    /// Bumble: valence above this drives DialogueTone.Panicked.
    /// Bumble panics when excitement tips over into positive arousal overload.
    /// </summary>
    private const float BumblePanicValenceThreshold = 0.5f;

    // ── public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a dialogue line for <paramref name="npc"/> in the context of
    /// the player's current manifold state (passed as a snapshot).
    ///
    /// The player snapshot is available from PlayerHElement.GetSnapshot() and
    /// is used only for tone modulation; the returned line is attributed to the NPC.
    /// </summary>
    /// <param name="npc">The speaking NPC — must have a fully initialised NpcMind.</param>
    /// <param name="player">Current player manifold snapshot (from PlayerHElement.GetSnapshot()).</param>
    /// <returns>A canned dialogue line appropriate to the NPC's current H-element state.</returns>
    public string GenerateLine(NpcMind npc, in HManifoldSnapshot player)
    {
        // ── Option N: pseudo-LLM retrieval path ───────────────────────────────
        // When the index is populated it drives line selection; otherwise fall
        // through to the existing canned-line pipeline below.
        if (TextIndex != null && TextIndex.Count > 0)
        {
            float[] query = PseudoLLMIndex.BuildQueryEmbedding(npc);
            return TextIndex.Retrieve(query);
        }

        // ── Canned-line pipeline (unchanged) ─────────────────────────────────
        // Peak attention weight across the NPC's full 4×4 HAM
        float att     = PeakAttentionWeight(npc.Attention);
        float emotion = npc.T_E.Valence;
        float intent  = npc.T_I.IntentDirection;

        string npcId  = npc.GetType().Name;   // "NpcThalegrim", "NpcBumble", etc.
        DialogueTone tone = SelectTone(emotion, intent, att, npc);
        return DialogueDatabase.GetLine(npcId, tone);
    }

    // ── internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Scan the full 4×4 weight table and return the single largest value.
    /// This is the "most active" dichotomy axis in the NPC's cognition right now.
    /// </summary>
    private static float PeakAttentionWeight(HarmonicAttentionMatrix ham)
    {
        float max = 0f;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                if (ham.Weights[i, j] > max)
                    max = ham.Weights[i, j];
        return max;
    }

    /// <summary>
    /// Map the three live scalars — emotion (valence), intent direction, and
    /// peak attention — to a DialogueTone based on the NPC's personality class.
    ///
    /// Priority within each NPC block is highest-to-lowest importance:
    ///   Thalegrim: Grumpy > Lecturing > Critical > Neutral
    ///   Bumble:    Panicked > Clingy > Dopey
    /// </summary>
    private static DialogueTone SelectTone(
        float emotion, float intent, float att, NpcMind npc)
    {
        if (npc is NpcThalegrim)
        {
            if (emotion < ThalegrimGrumpyValenceThreshold)   return DialogueTone.Grumpy;
            if (intent  > ThalegrimLecturingIntentThreshold) return DialogueTone.Lecturing;
            if (att     > ThalegrimCriticalAttThreshold)     return DialogueTone.Critical;
            return DialogueTone.Neutral;
        }

        if (npc is NpcBumble)
        {
            if (emotion > BumblePanicValenceThreshold)   return DialogueTone.Panicked;
            if (att     > BumbleClingyAttThreshold)      return DialogueTone.Clingy;
            return DialogueTone.Dopey;
        }

        return DialogueTone.Neutral;
    }
}
