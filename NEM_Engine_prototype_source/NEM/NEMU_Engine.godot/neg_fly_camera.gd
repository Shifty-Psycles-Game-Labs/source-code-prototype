# ============================================================
# NEG Fly Camera — v1
# ============================================================
# WHAT THIS DOES:
# Lets you look around with the mouse and move with WASD + Space/Shift.
# Attach this script to a Camera3D node in your scene.
#
# HOW TO ATTACH IT:
# 1. Select your Camera3D node in the Scene panel.
# 2. In the Inspector area, click the "Attach Script" icon (the
#    little scroll/paper icon at the top of the Inspector, or
#    right-click the Camera3D node -> Attach Script).
# 3. When the dialog pops up, instead of creating a new script,
#    choose "Load" and select THIS FILE (neg_fly_camera.gd).
# 4. Press Play. Click into the game window, then move the mouse
#    to look around, and use WASD to move, Space to rise, Shift
#    to descend. Press Esc to release the mouse cursor.
#
# REMINDER: because the shader is a SKY shader, moving position
# won't shift the pattern's parallax — only LOOKING AROUND (mouse)
# will visibly change what you see, since sky shaders only depend
# on view direction. This is expected, not a bug.
# ============================================================

extends Camera3D

@export var mouse_sensitivity: float = 0.003
@export var move_speed: float = 5.0

var _yaw: float = 0.0
var _pitch: float = 0.0

func _ready() -> void:
	# Capture the mouse so moving it turns the camera instead of
	# moving a cursor around the screen.
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func _unhandled_input(event: InputEvent) -> void:
	# Press Escape to release the mouse cursor (useful for quitting/alt-tabbing).
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

	# Click back into the window to re-capture the mouse.
	if event is InputEventMouseButton and event.pressed:
		if Input.mouse_mode == Input.MOUSE_MODE_VISIBLE:
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

	# Mouse movement turns the camera.
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		_yaw -= event.relative.x * mouse_sensitivity
		_pitch -= event.relative.y * mouse_sensitivity
		_pitch = clamp(_pitch, -1.4, 1.4) # roughly ±80 degrees, stops you flipping upside down
		rotation = Vector3(_pitch, _yaw, 0.0)

func _process(delta: float) -> void:
	# Build a movement direction from WASD relative to which way
	# the camera is currently facing.
	var input_dir := Vector3.ZERO
	if Input.is_key_pressed(KEY_W):
		input_dir -= transform.basis.z
	if Input.is_key_pressed(KEY_S):
		input_dir += transform.basis.z
	if Input.is_key_pressed(KEY_A):
		input_dir -= transform.basis.x
	if Input.is_key_pressed(KEY_D):
		input_dir += transform.basis.x
	if Input.is_key_pressed(KEY_SPACE):
		input_dir += Vector3.UP
	if Input.is_key_pressed(KEY_SHIFT):
		input_dir -= Vector3.UP

	if input_dir.length() > 0.0:
		input_dir = input_dir.normalized()

	global_position += input_dir * move_speed * delta
