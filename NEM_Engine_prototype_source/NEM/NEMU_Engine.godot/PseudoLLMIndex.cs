using System.Collections.Generic;

/// <summary>
/// PseudoLLMIndex — minimal retrieval-based text layer (Option N).
///
/// Stores a corpus of <see cref="TextChunk"/> objects and retrieves the most
/// relevant one given a query embedding.
///
/// Embedding strategy (skeleton):
///   Four surface-feature dimensions:
///     [0] text length      (verbosity / complexity proxy)
///     [1] sentence count   (structural completeness proxy)
///     [2] exclamation rate (high valence / urgency proxy)
///     [3] question rate    (curiosity / uncertainty proxy)
///
///   These dimensions happen to align loosely with the four OM4 axes:
///     Intent magnitude   → questions asked (curiosity / goal-seeking)
///     Knowledge magnitude→ length (more words = more context)
///     Emotion magnitude  → exclamations (affective load)
///     Action magnitude   → sentence count (structured, sequential output)
///
/// Retrieval:
///   Cosine-normalised dot-product (scores proportional to cosine similarity)
///   so that magnitude differences between query and corpus don't dominate.
///
/// Integration path:
///   1. Replace Embed() with a call to a real embedding model.
///   2. Expand the float[] dimension to match the model's output size.
///   3. The rest of the class (AddChunk, Retrieve, BuildQueryEmbedding) is
///      unchanged — the embedding is the only substitution surface.
/// </summary>
public class PseudoLLMIndex
{
    private readonly List<TextChunk> _chunks = new();

    // ── corpus management ─────────────────────────────────────────────────────

    /// <summary>
    /// Add a text chunk to the index.
    /// The embedding is computed immediately and stored on the chunk.
    /// If a chunk with the same <paramref name="id"/> already exists it is
    /// replaced so the corpus stays deduplicated.
    /// </summary>
    public void AddChunk(string id, string text)
    {
        // Remove any existing entry with this id (replace semantics)
        _chunks.RemoveAll(c => c.Id == id);

        _chunks.Add(new TextChunk
        {
            Id        = id,
            Text      = text,
            Embedding = Embed(text),
        });
    }

    /// <summary>Returns the number of chunks currently in the index.</summary>
    public int Count => _chunks.Count;

    // ── embedding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute a 4-dimensional surface-feature embedding for <paramref name="text"/>.
    ///
    /// Dimensions:
    ///   [0] normalised text length      (len / 500, capped at 1)
    ///   [1] sentence density            (dots / max(1, len) × 500)
    ///   [2] exclamation density         (exclamation marks / max(1, len) × 500)
    ///   [3] question density            (question marks    / max(1, len) × 500)
    ///
    /// Normalisation keeps each dimension in a comparable range so that
    /// the dot product is a fair cosine proxy.
    ///
    /// Swap this entire method body for a real model call at integration time.
    /// </summary>
    private static float[] Embed(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new float[4];

        float len  = text.Length;
        float norm = Godot.Mathf.Max(1f, len);

        int dots  = 0;
        int excl  = 0;
        int ques  = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '.')  dots++;
            else if (c == '!') excl++;
            else if (c == '?') ques++;
        }

        return new float[]
        {
            Godot.Mathf.Min(len / 500f, 1f),   // [0] verbosity
            dots  / norm * 500f,                // [1] sentence density
            excl  / norm * 500f,                // [2] emotional valence
            ques  / norm * 500f,                // [3] curiosity / uncertainty
        };
    }

    // ── retrieval ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieve the corpus chunk most similar to <paramref name="queryEmbedding"/>
    /// using cosine-normalised dot product.
    ///
    /// Returns the matched text, or "..." when the corpus is empty.
    /// </summary>
    public string Retrieve(float[] queryEmbedding)
    {
        if (_chunks.Count == 0 || queryEmbedding == null || queryEmbedding.Length == 0)
            return "...";

        float qNorm = Norm(queryEmbedding);
        if (qNorm < 1e-6f) return _chunks[0].Text;  // degenerate query — return first

        float    bestScore = float.NegativeInfinity;
        TextChunk? best    = null;

        foreach (var chunk in _chunks)
        {
            float cNorm  = Norm(chunk.Embedding);
            float cosine = cNorm < 1e-6f
                ? 0f
                : Dot(queryEmbedding, chunk.Embedding) / (qNorm * cNorm);

            if (cosine > bestScore)
            {
                bestScore = cosine;
                best      = chunk;
            }
        }

        return best?.Text ?? "...";
    }

    /// <summary>
    /// Build a 4-dimensional query embedding from an NPC's live H-element state,
    /// mapping OM4 field magnitudes to the same 4 surface dimensions used by Embed().
    ///
    ///   [0] Knowledge magnitude → verbosity axis (how much the NPC "knows")
    ///   [1] Emotion  magnitude  → sentence density axis (structured affect)
    ///   [2] Emotion  magnitude  → valence axis (re-used; most salient affect signal)
    ///   [3] Intent   magnitude  → curiosity / goal-seeking axis
    ///
    /// Passed directly to Retrieve() to find the closest corpus line.
    /// </summary>
    public static float[] BuildQueryEmbedding(NpcMind npc)
    {
        return new float[]
        {
            npc.T_K.Magnitude,   // [0] verbosity ← knowledge breadth
            npc.T_E.Magnitude,   // [1] sentence density ← affective load
            npc.T_E.Magnitude,   // [2] exclamation density ← affective load (same axis)
            npc.T_I.Magnitude,   // [3] question density ← intent / goal drive
        };
    }

    // ── math helpers ─────────────────────────────────────────────────────────

    private static float Dot(float[] a, float[] b)
    {
        int len = System.Math.Min(a.Length, b.Length);
        float s = 0f;
        for (int i = 0; i < len; i++) s += a[i] * b[i];
        return s;
    }

    private static float Norm(float[] v)
    {
        float s = 0f;
        for (int i = 0; i < v.Length; i++) s += v[i] * v[i];
        return Godot.Mathf.Sqrt(s);
    }
}
