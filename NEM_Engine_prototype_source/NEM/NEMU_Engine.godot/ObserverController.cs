using Godot;

public partial class ObserverController : Camera3D
{
    [Export] public float MoveSpeed { get; set; } = 6.0f;
    [Export] public float SprintSpeed { get; set; } = 12.0f;
    [Export] public float LookSensitivity { get; set; } = 0.0025f;
    [Export] public bool InvertY { get; set; } = false;

    private Vector2 _lookDelta;
    private bool _mouseCaptured;
    private Vector3 _actionVector;

    // ── player binding (README §7369) ──────────────────────────────────────────
    // When a PlayerController calls BindPlayer(), the observer stops acting as a
    // free-fly camera and instead mirrors the player's world-position and gaze.
    private Node3D?   _boundPlayer;
    private Camera3D? _boundCamera;

    public Vector3 ActionVector => _actionVector;

    /// <summary>
    /// Bind this observer to a player node and its camera.
    /// Once bound, the observer tracks the player each frame and the free-fly
    /// controls are suppressed.
    /// </summary>
    public void BindPlayer(Node3D player, Camera3D? camera)
    {
        if (camera is null)
        {
            GD.PushWarning("ObserverController.BindPlayer: camera is null — observer will track position only.");
        }
        _boundPlayer = player;
        _boundCamera = camera;
        // Disable free-fly mouse capture — the player owns the mouse now.
        _mouseCaptured = false;
        Input.MouseMode = Input.MouseModeEnum.Captured; // player handles capture
    }

    public override void _Ready()
    {
        EnsureInputActions();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _mouseCaptured = true;
    }

    public override void _Process(double delta)
    {
        // If bound to a player, mirror position and forward direction.
        if (_boundPlayer != null)
        {
            GlobalPosition = _boundPlayer.GlobalPosition;
            if (_boundCamera != null)
            {
                // Face the direction the player's camera is looking.
                var forward = -_boundCamera.GlobalTransform.Basis.Z;
                if (forward.LengthSquared() > 0.0001f)
                    LookAt(GlobalPosition + forward, Vector3.Up);
            }
            _actionVector = ComputeActionVector();
            return;
        }

        if (_mouseCaptured)
        {
            MoveCamera((float)delta);
        }

        _actionVector = ComputeActionVector();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && _mouseCaptured)
        {
            _lookDelta += motion.Relative;
            RotateY(Mathf.DegToRad(-_lookDelta.X * LookSensitivity));
            var pitch = -_lookDelta.Y * LookSensitivity;
            RotationDegrees = new Vector3(
                Mathf.Clamp(RotationDegrees.X + Mathf.RadToDeg(pitch), -89f, 89f),
                RotationDegrees.Y,
                RotationDegrees.Z);
            _lookDelta = Vector2.Zero;
        }

        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            _mouseCaptured = !_mouseCaptured;
            Input.MouseMode = _mouseCaptured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        }
    }

    public Vector3 GetActionVector()
    {
        return _actionVector;
    }

    private Vector3 ComputeActionVector()
    {
        var dir = Vector3.Zero;

        if (Input.IsActionPressed("move_forward")) dir -= Transform.Basis.Z;
        if (Input.IsActionPressed("move_back")) dir += Transform.Basis.Z;
        if (Input.IsActionPressed("move_left")) dir -= Transform.Basis.X;
        if (Input.IsActionPressed("move_right")) dir += Transform.Basis.X;
        if (Input.IsActionPressed("move_up")) dir += Vector3.Up;
        if (Input.IsActionPressed("move_down")) dir -= Vector3.Up;

        return dir == Vector3.Zero ? Vector3.Zero : dir.Normalized();
    }

    private void MoveCamera(float delta)
    {
        var dir = ComputeActionVector();

        if (dir != Vector3.Zero)
        {
            var speed = Input.IsActionPressed("sprint") ? SprintSpeed : MoveSpeed;
            Position += dir * speed * delta;
        }
    }

    private static void EnsureInputActions()
    {
        AddActionIfMissing("move_forward", Key.W);
        AddActionIfMissing("move_back", Key.S);
        AddActionIfMissing("move_left", Key.A);
        AddActionIfMissing("move_right", Key.D);
        AddActionIfMissing("move_up", Key.Space);
        AddActionIfMissing("move_down", Key.Shift);
        AddActionIfMissing("sprint", Key.Ctrl);
    }

    private static void AddActionIfMissing(string action, Key key)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        var ev = new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = true
        };

        if (!InputMap.ActionHasEvent(action, ev))
        {
            InputMap.ActionAddEvent(action, ev);
        }
    }
}
