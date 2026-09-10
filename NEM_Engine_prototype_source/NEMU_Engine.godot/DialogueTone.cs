/// <summary>
/// DialogueTone — the seven expressive registers an NPC line can carry.
///
/// Tone is selected each call by DialogueEngine.SelectTone() from the NPC's
/// live EmotionState.Valence, IntentState.IntentDirection, and peak
/// HarmonicAttentionMatrix weight.  DialogueDatabase uses it as the line key.
/// </summary>
public enum DialogueTone
{
    Neutral,    // fallback — no strong signal
    Grumpy,     // Thalegrim: low valence
    Lecturing,  // Thalegrim: high positive intent
    Critical,   // Thalegrim: high attention weight
    Panicked,   // Bumble:    positive valence spike (high arousal)
    Clingy,     // Bumble:    high attention weight
    Dopey,      // Bumble:    fallback when calm
}
