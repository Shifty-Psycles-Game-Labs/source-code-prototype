using Godot;

/// <summary>
/// NpcThalegrim — Archmage Thalegrim, the grumpy old wizard.
///
/// Personality profile (NEM §NPC H-element baseline):
///   High knowledge     — decades of research, two forbidden disciplines
///   Low patience       — negative valence baseline, easily irritated
///   High volatility    — arousal spikes fast, decays slowly
///   Strong intent      — clear research agenda, high commitment
///   Slow action rate   — deliberate, minimal movement
///   Torsion-sensitive  — RelationshipSign = +1 (neutral toward player by default)
///
/// Thalegrim's K field has real entries from day one ("ancient_magic",
/// "forbidden_texts").  His Emotion baseline is negative and his Intent is
/// locked onto knowledge acquisition — he barely notices the player unless
/// they carry something useful or disrupt his work.
///
/// Per-tick override:
///   Arousal decays slightly each tick (he suppresses emotion with discipline),
///   but it re-climbs under high manifold torsion because knowledge-dominant
///   torsion is his natural habitat.
/// </summary>
public partial class NpcThalegrim : NpcMind
{
    public override void _Ready()
    {
        base._Ready();

        // ── Knowledge baseline — the wizard knows things ───────────────────
        T_K.AddKnowledge("ancient_magic");
        T_K.AddKnowledge("forbidden_texts");
        T_K.AddKnowledge("ley_lines");

        // ── Emotion baseline — low patience, suppressed arousal ────────────
        T_E.Valence     = -0.2f;   // slightly negative (irritable baseline)
        T_E.Arousal     =  0.3f;   // moderate arousal (old tension never fully drains)
        T_E.Uncertainty =  0.1f;   // he's certain about most things

        // ── Intent baseline — strong research drive ────────────────────────
        T_I.IntentDirection = 0.8f;  // strongly pursuing his goal
        T_I.Commitment      = 0.9f;  // decades of habit = near-unbreakable resolve

        // ── Action baseline — slow, deliberate ────────────────────────────
        // Thalegrim barely moves.  Low MoveRate, moderate Look (scanning his work).
        SetActivityRates(move: 0.05f, look: 0.3f, interact: 0.1f, idle: 0.55f);

        // ── Relationship — neutral to player unless proven otherwise ───────
        RelationshipSign      = 1.0f;
        PlayerCouplingAlpha   = 0.05f;  // almost unaffected by player (proud wizard)
        FocusStressGain       = 0.8f;   // under stress he focuses hard (deep expertise)
    }

    public override void _Process(double delta)
    {
        base._Process(delta);  // Regulator AT + EvolveSelf

        float dt = (float)delta;

        // Thalegrim's arousal slowly drains each tick (discipline over emotion)
        T_E.Arousal = Mathf.Clamp(T_E.Arousal - 0.01f * dt * 60f, 0f, 1f);

        // But when knowledge torsion is high (he's deep in research), arousal
        // creeps back up — the work excites him even when he denies it.
        if (System.Math.Abs(TotalTorsion) > 0.5f)
            T_E.Arousal = Mathf.Clamp(T_E.Arousal + 0.005f * dt * 60f, 0f, 1f);
    }
}
