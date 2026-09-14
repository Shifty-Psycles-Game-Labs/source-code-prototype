# ============================================================
# VerseNode.gd
# NEM-U Universal Shader Driver  —  B.P.LAW 2026
# ShiftyPsyclesLtd, Break-0.1 Licence (see README_XD_NEM-U_OM4_001.nmd)
#
# PURPOSE
# -------
# Reads every live NEM-U field scalar from the C# engine nodes once per
# _process() frame and pushes them as uniforms into a shared ShaderMaterial
# (res/shaders/VerseNode.gdshader).
#
# Every MeshInstance3D in the scene that has its MaterialOverride (or surface 0
# material) set to this shared ShaderMaterial will then react to the live
# manifold state with zero per-mesh CPU overhead — the GPU reads the uniforms
# directly from the shared resource block.
#
# USAGE
# -----
# 1. Add VerseNode.gd as a Node3D child of World in SceneTreeRoot.tscn.
# 2. Set the NodePath exports in the Inspector to point at the engine siblings.
# 3. Create a ShaderMaterial in the Inspector using res/shaders/VerseNode.gdshader
#    and assign it to the verse_material export below.
# 4. Assign that same ShaderMaterial to the MaterialOverride of any geometry
#    you want to be driven by the manifold (TheCityNode buildings, ProceduralFloor,
#    WireframeFieldVisualizer meshes, etc.).
# 5. Press Play — all geometry updates automatically every frame.
#
# COLLAPSE FLASH
# --------------
# Connect the EntropyCollapseEngine.collapse_event signal to _on_collapse_event().
# The flash decays at FlashDecayRate per second.  At 60 fps a value of 3.0 gives
# a ~0.33 s bright burst, which is perceptible without being distracting.
#
# NODE PATH DEFAULTS
# ------------------
# Defaults assume VerseNode is a direct child of World (same parent level as
# the engine siblings).  Adjust if your tree differs.
# ============================================================

extends Node3D

# ── shader material ────────────────────────────────────────────────────────────
## The shared ShaderMaterial using res/shaders/VerseNode.gdshader.
## Assign in the Inspector.  All geometry that should react to the manifold
## should share (not copy) this exact resource.
@export var verse_material: ShaderMaterial

# ── engine node paths ──────────────────────────────────────────────────────────
@export var gr_engine_path:        NodePath = "../GRHarmonicEngine"
@export var regulator_path:        NodePath = "../Regulator"
@export var magic_engine_path:     NodePath = "../MagicFieldEngine"
@export var player_h_element_path: NodePath = "../PlayerHElement"
@export var maxwell_engine_path:   NodePath = "../MaxwellEngine"
@export var entropy_engine_path:   NodePath = "../EntropyCollapseEngine"

# ── tuning ─────────────────────────────────────────────────────────────────────
## Vertex index sampled for per-vertex GR scalar.
## Keep at 0 for now; later VerseNode3D can expose the "nearest vertex to player"
## and this node can track it dynamically.
@export var gr_sample_vertex: int = 0

## Face index sampled for per-face EM flux.
@export var em_sample_face: int = 0

## Max torsion value used to normalise u_torsion_norm.
## Should match Regulator.MaxTorsion; duplicated here so GDScript can read it
## without needing a typed C# property accessor.
@export var max_torsion: float = 1000.0

## Player ThrottleThreshold — used to normalise player torsion.
@export var player_throttle_threshold: float = 2.0

## Flash decay rate (units per second).  1 / FlashDecayRate ≈ visible flash duration.
@export var flash_decay_rate: float = 3.0

# ── internal refs (resolved in _ready) ────────────────────────────────────────
var _gr:       Object   # GRHarmonicEngine (C# node)
var _reg:      Object   # Regulator
var _magic:    Object   # MagicFieldEngine
var _player_h: Object   # PlayerHElement
var _maxwell:  Object   # MaxwellEngine2Form

# ── collapse flash state ───────────────────────────────────────────────────────
var _flash: float = 0.0

# ── sampled field state (updated per frame) ────────────────────────────────────
var _gr_scalar:      float = 0.0
var _em_flux:        float = 0.0
var _torsion:        float = 0.0
var _at:             float = 1.0
var _lambda:         float = 0.0
var _intent:         float = 0.0
var _emotion_arousal: float = 0.0
var _emotion_valence: float = 0.0
var _knowledge:      float = 0.0
var _player_torsion: float = 0.0
var _player_valence: float = 0.0
var _player_intent:  float = 0.0


# ══════════════════════════════════════════════════════════════════════════════
# LIFECYCLE
# ══════════════════════════════════════════════════════════════════════════════

func _ready() -> void:
	_gr       = get_node_or_null(gr_engine_path)
	_reg      = get_node_or_null(regulator_path)
	_magic    = get_node_or_null(magic_engine_path)
	_player_h = get_node_or_null(player_h_element_path)
	_maxwell  = get_node_or_null(maxwell_engine_path)

	# Connect entropy collapse flash signal if the engine exists.
	var entropy := get_node_or_null(entropy_engine_path)
	if entropy != null and entropy.has_signal("collapse_event"):
		entropy.connect("collapse_event", _on_collapse_event)

	if verse_material == null:
		push_warning("VerseNode.gd: verse_material is not assigned. Shader uniforms will not be pushed.")

	_report_wiring()


func _process(delta: float) -> void:
	if verse_material == null:
		return

	_sample_fields()
	_decay_flash(delta)
	_push_uniforms()


# ══════════════════════════════════════════════════════════════════════════════
# FIELD SAMPLING
# ══════════════════════════════════════════════════════════════════════════════

func _sample_fields() -> void:
	# ── GR curvature scalar at sample vertex ──────────────────────────────────
	if _gr != null:
		var t_array = _gr.GetScalarField()   # float[] from C#
		if t_array != null and t_array.size() > gr_sample_vertex:
			_gr_scalar = t_array[gr_sample_vertex]
		else:
			_gr_scalar = 0.0
	else:
		_gr_scalar = 0.0

	# ── Maxwell EM flux at sample face ────────────────────────────────────────
	if _maxwell != null:
		var f_array = _maxwell.GetField()    # float[] from C#
		if f_array != null and f_array.size() > em_sample_face:
			_em_flux = clamp(abs(f_array[em_sample_face]), 0.0, 1.0)
		else:
			_em_flux = 0.0
	else:
		_em_flux = 0.0

	# ── Regulator: torsion, AT, lambda ───────────────────────────────────────
	if _reg != null:
		var raw_torsion: float = _reg.Torsion
		_torsion = clamp(abs(raw_torsion) / max(max_torsion, 0.001), 0.0, 1.0)
		_at      = clamp(_reg.LastAT, 0.0, 1.0)
	else:
		_torsion = 0.0
		_at      = 1.0

	# ── MagicFieldEngine: cognitive field means ───────────────────────────────
	if _magic != null:
		# GetIntent/GetEmotion/GetKnowledge return float[] — take the mean.
		_intent          = _array_mean(_magic.GetIntent())
		_emotion_arousal = clamp(_array_mean(_magic.GetEmotion()), 0.0, 1.0)
		_knowledge       = clamp(_array_mean(_magic.GetKnowledge()), 0.0, 1.0)
		# λ is the global progression parameter from EntropyCollapseEngine;
		# MagicFieldEngine does not hold it directly, so we accumulate it from
		# the Regulator LambdaStep as a proxy if the collapse engine is absent.
		_lambda += clamp(_magic.LambdaStep, 0.0, 0.1)
	else:
		_intent          = 0.0
		_emotion_arousal = 0.0
		_knowledge       = 0.0

	# ── PlayerHElement ────────────────────────────────────────────────────────
	if _player_h != null:
		var snap = _player_h.GetSnapshot()    # HManifoldSnapshot struct from C#
		_player_torsion = clamp(abs(snap.TotalTorsion) / max(player_throttle_threshold, 0.001), 0.0, 1.0)
		_player_valence = clamp(snap.EValence, -1.0, 1.0)
		_player_intent  = clamp(snap.IIntentDirection, -1.0, 1.0)
		_emotion_valence = clamp(snap.EValence, -1.0, 1.0)
	else:
		_player_torsion  = 0.0
		_player_valence  = 0.0
		_player_intent   = 0.0
		_emotion_valence = 0.0


# ══════════════════════════════════════════════════════════════════════════════
# UNIFORM PUSH
# ══════════════════════════════════════════════════════════════════════════════

func _push_uniforms() -> void:
	verse_material.set_shader_parameter("u_gr_scalar",      _gr_scalar)
	verse_material.set_shader_parameter("u_em_flux",        _em_flux)
	verse_material.set_shader_parameter("u_torsion_norm",   _torsion)
	verse_material.set_shader_parameter("u_at",             _at)
	verse_material.set_shader_parameter("u_lambda",         _lambda)
	verse_material.set_shader_parameter("u_intent",         _intent)
	verse_material.set_shader_parameter("u_emotion_arousal",_emotion_arousal)
	verse_material.set_shader_parameter("u_emotion_valence",_emotion_valence)
	verse_material.set_shader_parameter("u_knowledge",      _knowledge)
	verse_material.set_shader_parameter("u_player_torsion", _player_torsion)
	verse_material.set_shader_parameter("u_player_valence", _player_valence)
	verse_material.set_shader_parameter("u_player_intent",  _player_intent)
	verse_material.set_shader_parameter("u_collapse_flash", _flash)


# ══════════════════════════════════════════════════════════════════════════════
# COLLAPSE FLASH
# ══════════════════════════════════════════════════════════════════════════════

## Connected to EntropyCollapseEngine.collapse_event signal.
## vertex, entropy_value, em_flux, gr_scalar are passed by the signal but we
## only need the trigger — the flash is uniform across all geometry.
func _on_collapse_event(_vertex: int, _entropy_value: float, _em: float, _gr: float) -> void:
	_flash = 1.0

func _decay_flash(delta: float) -> void:
	_flash = max(_flash - flash_decay_rate * delta, 0.0)


# ══════════════════════════════════════════════════════════════════════════════
# HELPERS
# ══════════════════════════════════════════════════════════════════════════════

## Returns the mean of a C# float[] (exposed as a Godot Array in GDScript).
## Returns 0.0 if the array is null or empty.
func _array_mean(arr) -> float:
	if arr == null:
		return 0.0
	var n: int = arr.size()
	if n == 0:
		return 0.0
	var s: float = 0.0
	for v in arr:
		s += float(v)
	return s / float(n)


## Prints wiring status to the Godot output panel once at startup.
func _report_wiring() -> void:
	var ok  := []
	var bad := []
	if _gr       != null: ok.append("GRHarmonicEngine")   else: bad.append("GRHarmonicEngine")
	if _reg      != null: ok.append("Regulator")          else: bad.append("Regulator")
	if _magic    != null: ok.append("MagicFieldEngine")   else: bad.append("MagicFieldEngine")
	if _player_h != null: ok.append("PlayerHElement")     else: bad.append("PlayerHElement")
	if _maxwell  != null: ok.append("MaxwellEngine2Form") else: bad.append("MaxwellEngine2Form")
	if verse_material != null: ok.append("ShaderMaterial") else: bad.append("ShaderMaterial(NOT SET)")

	print("[VerseNode] Wired: ", ", ".join(ok))
	if bad.size() > 0:
		push_warning("[VerseNode] Not found: " + ", ".join(bad) + " — those uniforms will default to 0.")
