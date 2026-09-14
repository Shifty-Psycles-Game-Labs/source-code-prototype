/// <summary>
/// DialogueDatabase — static canned-line lookup, keyed on NPC class name + DialogueTone.
///
/// Each entry returns a single placeholder string.  The intent is that these
/// constant tables are later replaced (or augmented) by a pseudo-LLM text
/// database keyed on the same (npcId, tone) pair — swap GetLine() internals
/// without touching callers.
///
/// NPC IDs match the C# class name returned by <c>npc.GetType().Name</c>
/// so new NPC subclasses are registered here by name, not by object reference.
/// </summary>
public static class DialogueDatabase
{
    /// <summary>
    /// Return a canned dialogue line for <paramref name="npcId"/> at the given <paramref name="tone"/>.
    /// Falls back to "..." when the NPC or tone has no registered entry.
    /// </summary>
    public static string GetLine(string npcId, DialogueTone tone)
    {
        return npcId switch
        {
            "NpcThalegrim" => tone switch
            {
                DialogueTone.Grumpy    => "Bah. Your presence disturbs the harmonic equilibrium.",
                DialogueTone.Lecturing => "Listen closely, apprentice. Magic obeys structure, not whim.",
                DialogueTone.Critical  => "You meddle with forces you barely comprehend.",
                _                      => "Hmm. The manifold stirs today.",
            },

            "NpcBumble" => tone switch
            {
                DialogueTone.Panicked => "Oh no oh no oh no—did I break the mesh again?",
                DialogueTone.Clingy   => "Uh… should I follow you? I'll follow you.",
                DialogueTone.Dopey    => "I saw a shiny thing! It was… shiny.",
                _                     => "Hi boss!",
            },

            _ => "...",
        };
    }
}
