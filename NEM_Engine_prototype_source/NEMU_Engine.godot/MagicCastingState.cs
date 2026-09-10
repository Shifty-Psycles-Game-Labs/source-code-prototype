using Godot;

/// <summary>
/// MagicCastingState — hold-time magic and insanity accumulator.
///
/// Implements the H4.nmd §PART 1-2 magic lifecycle:
///   Pressed  → StartMagic()  — begin cast, reset hold timer
///   Released → StopMagic()   — end cast, decay insanity
///   Held     → Tick(delta)   — charge magic; accumulate insanity
///
/// Insanity:
///   Grows at InsanityRate (units/second) while casting.
///   Decays at InsanityDecayRate (units/second) when not casting.
///   Fires the InsanityWarning event every frame insanity >= InsanityWarningThreshold.
///   Resets hold timer on StartMagic so multi-tap doesn't stack indefinitely.
///
/// This class is a plain C# object (not a Node) — PlayerController owns it and
/// calls Tick() from _Process.  Signal emission goes through the GD.Print path
/// for now so the warning is visible in the Godot debugger without extra wiring.
/// </summary>
public sealed class MagicCastingState
{
    // ── tunables ─────────────────────────────────────────────────────────────
    /// <summary>Insanity units accumulated per second of uninterrupted casting.</summary>
    public float InsanityRate           = 1.0f;

    /// <summary>Insanity units lost per second when not casting.</summary>
    public float InsanityDecayRate      = 0.4f;

    /// <summary>
    /// Insanity threshold above which the warning fires each frame.
    /// 5 seconds at the default InsanityRate = threshold 5.0.
    /// </summary>
    public float InsanityWarningThreshold = 5.0f;

    /// <summary>Hard cap — insanity cannot exceed this value.</summary>
    public float InsanityCap            = 20.0f;

    // ── live state ────────────────────────────────────────────────────────────
    /// <summary>True from the moment the interact button is pressed until it is released.</summary>
    public bool  IsCasting      { get; private set; }

    /// <summary>Seconds the current cast has been held without interruption.</summary>
    public double MagicHoldTime  { get; private set; }

    /// <summary>
    /// Accumulated insanity level [0, InsanityCap].
    /// Grows while casting, decays at rest.
    /// </summary>
    public float Insanity        { get; private set; }

    /// <summary>
    /// True for exactly one Tick() call when magic charge exceeds the warning threshold.
    /// Callers should read this flag once and react (HUD flash, sound, etc).
    /// Remains true every frame insanity is above threshold while casting.
    /// </summary>
    public bool  InsanityWarning { get; private set; }

    // ── input callbacks ───────────────────────────────────────────────────────

    /// <summary>
    /// Call when the "interact" action is pressed.
    /// Resets the hold timer but does NOT reset insanity — accumulated charge persists.
    /// </summary>
    public void StartMagic()
    {
        IsCasting    = true;
        MagicHoldTime = 0.0;
    }

    /// <summary>Call when the "interact" action is released.</summary>
    public void StopMagic()
    {
        IsCasting = false;
        // Don't zero MagicHoldTime here — the last hold duration stays readable until
        // the next StartMagic() so debug displays don't flicker to 0 on release.
    }

    // ── per-frame ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Advance magic state by one frame.
    /// Call from PlayerController._Process(delta).
    ///
    /// While casting:
    ///   MagicHoldTime += delta
    ///   Insanity      += InsanityRate * delta
    ///   InsanityWarning = Insanity >= InsanityWarningThreshold
    ///
    /// While idle:
    ///   Insanity      -= InsanityDecayRate * delta  (floor: 0)
    ///   InsanityWarning = false
    /// </summary>
    public void Tick(double delta)
    {
        if (IsCasting)
        {
            MagicHoldTime += delta;
            Insanity       = Mathf.Clamp(
                Insanity + InsanityRate * (float)delta,
                0f, InsanityCap);

            InsanityWarning = Insanity >= InsanityWarningThreshold;
            if (InsanityWarning)
                GD.Print($"WARNING --- magicInsanity levels too high  [{Insanity:F2}]");
        }
        else
        {
            Insanity = Mathf.Clamp(
                Insanity - InsanityDecayRate * (float)delta,
                0f, InsanityCap);

            InsanityWarning = false;
        }
    }
}
