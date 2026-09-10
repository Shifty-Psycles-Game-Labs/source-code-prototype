// ============================================================
// VerseNode.gdshader
// NEM-U Universal Surface Shader  —  B.P.LAW 2026
// ShiftyPsyclesLtd, Break-0.1 Licence (see README_XD_NEM-U_OM4_001.nmd)
//
// Maps every live NEM-U field scalar onto geometry colour, edge glow,
// vertex displacement, and opacity — with zero per-mesh CPU cost.
// All uniforms are pushed once per frame by VerseNode.gd from the
// C# engine nodes (GRHarmonicEngine, Regulator, MagicFieldEngine,
// PlayerHElement, EntropyCollapseEngine).
//
// Visual language (matches WireframeFieldVisualizer.cs colour conventions):
//   Dark void   → vacuum state  (ΔT = 0, zero torsion, no flux)
//   Cyan        → positive GR curvature / EM flux
//   Magenta     → high torsion / negative flux / AT saturation
//   Gold/amber  → intent dominance  (T_I high, player focused)
//   Violet      → knowledge saturation (T_K phase coherent)
//   Red         → emotion debuff / arousal spike
//   Entropy λ   → slow chromatic drift along progression axis
//
// Vertex stage:
//   Normal-direction displacement proportional to GR curvature scalar.
//   This makes every surface "breathe" with the manifold — buildings
//   pulse when the GR field propagates, floor ripples under curvature waves.
//
// Fragment stage:
//   1. Base albedo = harmonic field blend (GR ↔ EM ↔ torsion)
//   2. Edge fresnel glow driven by AT (attention threshold)
//   3. Cognitive tint layer (Intent/Emotion/Knowledge axes)
//   4. Entropy progression drift (λ shifts hue over simulation time)
//   5. Collapse flash: brief bright pulse at each entropy-collapse event
//   6. Final alpha: surfaces approach transparency as |ΔT| → 0 (harmonic vacuum)
// ============================================================

shader_type spatial;

// ── render mode ───────────────────────────────────────────────────────────────
// cull_disabled so thin geometry is visible from both sides.
// blend_mix with depth write keeps Z-sorting correct.
// Unshaded keeps colours true to the field signal — no lighting contamination.
render_mode blend_mix, depth_draw_opaque, cull_disabled, unshaded;

// ── physical field uniforms (pushed by VerseNode.gd each frame) ──────────────

/// GR curvature scalar sampled at the nearest manifold vertex [−1 … 1].
/// Positive = local curvature excess (mass/energy concentration).
/// Negative = curvature deficit (void / saddle region).
uniform float u_gr_scalar     : hint_range(-1.0, 1.0) = 0.0;

/// Maxwell EM flux magnitude at the nearest face [0 … 1].
uniform float u_em_flux        : hint_range(0.0, 1.0) = 0.0;

/// Normalised manifold torsion τ / MaxTorsion ∈ [0 … 1].
/// 0 = calm resonance, 1 = maximum rotational phase.
uniform float u_torsion_norm   : hint_range(0.0, 1.0) = 0.0;

/// AT (attention threshold) from Regulator ∈ [0 … 1].
/// AT ≈ 1 = manifold relaxed, full recursion open.
/// AT ≈ 0 = manifold saturated, recursion clamped.
uniform float u_at             : hint_range(0.0, 1.0) = 1.0;

/// Entropic progression parameter λ (monotonically increasing).
/// Used as a slow phase offset — drives chromatic drift over simulation time.
uniform float u_lambda         = 0.0;

// ── cognitive field uniforms ──────────────────────────────────────────────────

/// Mean Intent field magnitude across the manifold ∈ [0 … 1].
uniform float u_intent         : hint_range(0.0, 1.0) = 0.0;

/// Mean Emotion arousal across the manifold ∈ [0 … 1].
uniform float u_emotion_arousal: hint_range(0.0, 1.0) = 0.0;

/// Emotion valence (−1 = fear/aversion, +1 = joy/approach).
uniform float u_emotion_valence: hint_range(-1.0, 1.0) = 0.0;

/// Mean Knowledge field phase (semantic coherence) ∈ [0 … 1].
uniform float u_knowledge      : hint_range(0.0, 1.0) = 0.0;

// ── player H-element uniforms ─────────────────────────────────────────────────

/// Player total torsion normalised by ThrottleThreshold ∈ [0 … 1].
uniform float u_player_torsion : hint_range(0.0, 1.0) = 0.0;

/// Player emotion valence re-exposed for local colour shift.
uniform float u_player_valence : hint_range(-1.0, 1.0) = 0.0;

/// Player intent direction (−1 flee … +1 pursue).
uniform float u_player_intent  : hint_range(-1.0, 1.0) = 0.0;

// ── entropy collapse flash ────────────────────────────────────────────────────

/// Flash intensity from the most recent entropy collapse event ∈ [0 … 1].
/// VerseNode.gd injects 1.0 on each CollapseEvent and decays it over ~0.3 s.
uniform float u_collapse_flash : hint_range(0.0, 1.0) = 0.0;

// ── geometry tuning ───────────────────────────────────────────────────────────

/// Maximum normal-direction displacement in world units driven by GR curvature.
/// Set to 0 to freeze geometry; raise for dramatic field-breathing.
uniform float u_displacement_scale : hint_range(0.0, 2.0) = 0.08;

/// Opacity floor — surfaces never go fully transparent even in vacuum.
/// Raise to keep geometry always visible; lower for immersive void aesthetic.
uniform float u_alpha_floor    : hint_range(0.0, 1.0) = 0.25;

/// Overall emission intensity multiplier.
uniform float u_emission_scale : hint_range(0.0, 4.0) = 1.0;


// ════════════════════════════════════════════════════════════════════════════
// HELPERS
// ════════════════════════════════════════════════════════════════════════════

// Map a signed scalar to the same colour convention used by
// WireframeFieldVisualizer.FieldToColor():
//   v ≥ 0 → dark-blue → bright cyan
//   v < 0 → dark-blue → bright magenta
vec3 field_to_color(float v) {
	v = clamp(v, -1.0, 1.0);
	if (v >= 0.0) {
		// 0 → (0, 0, 0.1)   +1 → (0, 1, 1)   cyan path
		return vec3(0.0, v, v + 0.1 * (1.0 - v));
	} else {
		// 0 → (0, 0, 0.1)   -1 → (1, 0, 1)   magenta path
		float m = -v;
		return vec3(m, 0.0, m + 0.1 * (1.0 - m));
	}
}

// Smooth Fresnel approximation for edge glow.
// Normal must be in view space (provided by Godot as NORMAL).
float fresnel(vec3 normal_vs, float power) {
	// dot(N, V) ≈ dot(normal_vs, vec3(0,0,1)) in view space
	float ndotv = abs(dot(normalize(normal_vs), vec3(0.0, 0.0, 1.0)));
	return pow(1.0 - ndotv, power);
}

// Chromatic phase shift driven by λ (slow, entropic).
// Returns a unit-length hue rotation applied additively to RGB.
vec3 lambda_tint(float lambda) {
	// λ cycles through hue every 200 units of progression.
	float phase = fract(lambda / 200.0) * 6.28318;
	// Smooth colour wheel: R-G-B lobe contributions.
	return vec3(
		0.5 + 0.5 * cos(phase),
		0.5 + 0.5 * cos(phase + 2.09440),   // 120°
		0.5 + 0.5 * cos(phase + 4.18879)    // 240°
	) * 0.15;   // subtle — it is an ambient drift, not a flash
}

// Torsion-driven cyan ↔ magenta tint (identical logic to WireframeFieldVisualizer).
vec3 torsion_tint(float torsion_norm) {
	vec3 calm  = vec3(0.0, 1.0, 1.0);    // cyan
	vec3 tense = vec3(1.0, 0.0, 1.0);    // magenta
	return mix(calm, tense, torsion_norm);
}


// ════════════════════════════════════════════════════════════════════════════
// VERTEX STAGE
// Displaces each vertex along its normal by the GR curvature scalar.
// This makes manifold curvature waves visibly ripple through all geometry.
// ════════════════════════════════════════════════════════════════════════════
void vertex() {
	// Displacement magnitude = curvature amplitude × tuning scale.
	// Negative curvature compresses; positive curvature expands.
	float disp = u_gr_scalar * u_displacement_scale;

	// NORMAL is in model space in the vertex stage.
	VERTEX += NORMAL * disp;
}


// ════════════════════════════════════════════════════════════════════════════
// FRAGMENT STAGE
// ════════════════════════════════════════════════════════════════════════════
void fragment() {
	// ── 1. Physical field base colour ────────────────────────────────────────
	// GR curvature colour: cyan for positive, magenta for negative.
	vec3 gr_col = field_to_color(u_gr_scalar);

	// EM flux colour: brighter cyan as flux increases.
	vec3 em_col = vec3(0.0, u_em_flux, u_em_flux + 0.05 * (1.0 - u_em_flux));

	// Blend: GR dominates at low flux, EM takes over as flux rises.
	vec3 phys_col = mix(gr_col, em_col, clamp(u_em_flux * 2.0, 0.0, 1.0));

	// ── 2. Torsion overlay ───────────────────────────────────────────────────
	// Torsion_norm 0 → no shift, 1 → fully magenta (max rotational phase).
	vec3 tors_col = torsion_tint(u_torsion_norm);

	// Weight: torsion_norm² so it only asserts itself visually above ~0.5.
	float tors_weight = u_torsion_norm * u_torsion_norm * 0.6;
	vec3 phys_tors = mix(phys_col, tors_col, tors_weight);

	// ── 3. Cognitive field tint layer ────────────────────────────────────────
	// Intent → gold tint (warm amber: focused will).
	vec3 intent_col   = vec3(1.0, 0.75, 0.0) * u_intent;
	// Emotion → red (negative valence) or soft green (positive valence).
	float val_norm    = u_emotion_valence * 0.5 + 0.5;    // 0 … 1
	vec3 emotion_col  = mix(vec3(0.9, 0.1, 0.1), vec3(0.1, 0.9, 0.3), val_norm)
						* u_emotion_arousal;
	// Knowledge → violet (coherent structure).
	vec3 know_col     = vec3(0.6, 0.0, 1.0) * u_knowledge;

	// Cognitive contribution is additive and scaled down so it modulates
	// rather than overwhelms the physical field signal.
	vec3 cog_add = (intent_col + emotion_col + know_col) * 0.25;

	// Player tint: player high torsion shifts all geometry subtly toward magenta.
	vec3 player_shift = vec3(u_player_torsion, 0.0, u_player_torsion) * 0.12;
	// Player intent direction colours approach geometry gold (pursue) or red (flee).
	float pi_norm     = u_player_intent * 0.5 + 0.5;     // 0 … 1
	vec3 player_intent_shift = mix(vec3(0.3, 0.0, 0.0), vec3(0.3, 0.25, 0.0), pi_norm) * 0.1;

	vec3 base_col = phys_tors + cog_add + player_shift + player_intent_shift;

	// ── 4. Entropic progression chromatic drift ──────────────────────────────
	// λ accumulates monotonically — it slowly rotates the hue palette so that
	// the world looks subtly different across a long play session (entropic aging).
	vec3 lambda_shift = lambda_tint(u_lambda);
	base_col += lambda_shift;

	// ── 5. AT edge fresnel glow ──────────────────────────────────────────────
	// At AT ≈ 1 (relaxed manifold) → soft cyan rim light.
	// At AT ≈ 0 (saturated manifold) → intense magenta rim (recursion clamped).
	float rim     = fresnel(NORMAL, 4.0);
	vec3 rim_calm = vec3(0.0, 0.6, 1.0);      // cyan glow — open manifold
	vec3 rim_sat  = vec3(1.0, 0.0, 0.8);      // magenta — saturated
	vec3 rim_col  = mix(rim_sat, rim_calm, u_at);
	float rim_str = rim * (0.3 + 0.7 * (1.0 - u_at));   // stronger when saturated
	base_col     += rim_col * rim_str;

	// ── 6. Collapse flash ────────────────────────────────────────────────────
	// Brief bright white-cyan burst on entropy-collapse events.
	vec3 flash_col = vec3(0.8, 1.0, 1.0) * u_collapse_flash;
	base_col      += flash_col;

	// ── 7. Opacity: vacuum transparency ─────────────────────────────────────
	// Surfaces near the harmonic vacuum state (low field magnitudes) become
	// more transparent — the world opens up when the manifold is calm.
	float field_presence = clamp(
		abs(u_gr_scalar) + u_em_flux + u_torsion_norm * 0.5 + u_collapse_flash,
		0.0, 1.0);
	float alpha = mix(u_alpha_floor, 1.0, field_presence);

	// ── Final output ─────────────────────────────────────────────────────────
	ALBEDO    = base_col * u_emission_scale;
	ALPHA     = alpha;

	// Emission: same as albedo so the geometry self-illuminates (unshaded mode
	// ignores lighting, but setting EMISSION makes the surface visible in
	// Godot's GI probes and reflection probes if they are ever added).
	EMISSION  = base_col * u_emission_scale * 0.5;
}
