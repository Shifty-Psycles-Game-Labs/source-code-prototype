using Godot;

/// <summary>
/// NpcBumble — the wizard's hapless servant.
///
/// Personality profile (NEM §NPC H-element baseline):
///   Low knowledge      — knows how to clean.  That's about it.
///   High instability   — positive but wildly oscillating valence
///   Weak intent        — no real goal, just reacts to whatever's happening
///   High action rate   — fidgety, clumsy, constantly moving
///   Highly influenced  — strong player coupling (easily led)
///   Low tension threshold — small changes in the manifold panic Bumble immediately
///
/// Bumble's action accumulator self-increments each tick (he fidgets even when
/// "idle").  His intent direction flips easily because his commitment is near-zero.
/// He is strongly coupled to the player manifold — he will mirror the player's
/// emotional state almost immediately.
///
/// Per-tick override:
///   MoveRate and LookRate increment each tick (fidgeting).
///   Arousal oscillates up when the player is nearby (he notices everything).
/// </summary>
public partial class NpcBumble : NpcMind
{
    public override void _Ready()
    {
        base._Ready();

        // ── Knowledge baseline — Bumble knows one thing ────────────────────
        T_K.AddKnowledge("cleaning");
        T_K.AddKnowledge("tripping_over_things");  // familiarity = 1.0 by experience

        // ── Emotion baseline — cheerful but unstable ───────────────────────
        T_E.Valence     =  0.1f;   // slightly positive (optimistic by default)
        T_E.Arousal     =  0.7f;   // high arousal (always on edge)
        T_E.Uncertainty =  0.6f;   // very uncertain about everything

        // ── Intent baseline — weak, directionless ─────────────────────────
        T_I.IntentDirection = 0.1f;  // vague positive drift (wants to help, sort of)
        T_I.Commitment      = 0.2f;  // flimsy resolve, easily redirected

        // ── Action baseline — high fidget baseline ─────────────────────────
        SetActivityRates(move: 0.5f, look: 0.4f, interact: 0.2f, idle: 0.1f);

        // ── Relationship — ally, strongly mirroring ────────────────────────
        RelationshipSign    = 1.0f;
        PlayerCouplingAlpha = 0.4f;   // strongly influenced by player
        FocusStressGain     = 0.2f;   // under stress Bumble panics, doesn't focus
    }

    public override void _Process(double delta)
    {
        base._Process(delta);  // Regulator AT + EvolveSelf

        float dt = (float)delta;

        // Bumble fidgets constantly — rates increment each tick
        // (capped at a reasonable maximum so he doesn't run off a cliff)
        T_A.MoveRate        = Mathf.Min(T_A.MoveRate        + 0.05f * dt * 60f, 5f);
        T_A.LookRate        = Mathf.Min(T_A.LookRate        + 0.03f * dt * 60f, 3f);

        // Arousal climbs when nearby NPCs (including the wizard) are active
        // Proxy: if manifold |TotalTorsion| > 0.2, Bumble gets excited
        if (System.Math.Abs(TotalTorsion) > 0.2f)
            T_E.Arousal = Mathf.Clamp(T_E.Arousal + 0.02f * dt * 60f, 0f, 1f);
    }
}
