/// <summary>
/// TextChunk — a single corpus entry in the pseudo-LLM retrieval index.
///
/// Holds the raw text and its surface-feature embedding vector.
/// The embedding is populated by PseudoLLMIndex.Embed() when the chunk is
/// added; at integration time the embedding array can be replaced by a real
/// dense vector without changing callers.
/// </summary>
public class TextChunk
{
    /// <summary>
    /// Stable identifier for this chunk (e.g. "fb_001", "quip_007").
    /// Used for de-duplication and debugging; not used during retrieval.
    /// </summary>
    public string Id = "";

    /// <summary>Raw text content of this corpus entry.</summary>
    public string Text = "";

    /// <summary>
    /// Surface-feature embedding vector computed by PseudoLLMIndex.Embed().
    ///
    /// Current skeleton (4 dimensions):
    ///   [0] text length      — proxy for verbosity / complexity
    ///   [1] sentence count   — proxy for structural completeness (dots)
    ///   [2] exclamation rate — proxy for high emotional valence / urgency
    ///   [3] question rate    — proxy for curiosity / uncertainty
    ///
    /// Swap for a real dense embedding (e.g. sentence-transformers output)
    /// at integration time — PseudoLLMIndex.Embed() is the only site to change.
    /// </summary>
    public float[] Embedding = System.Array.Empty<float>();
}
