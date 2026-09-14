using Godot;

/// <summary>
/// PlayerController — first-person CharacterBody3D with WASD movement, mouse-look,
/// optional gravity, and the full H4 action-context pipeline:
///
///   • MagicCastingState  — LMB hold-time magic and insanity accumulation
///   • MovementTrail      — two-frame movement detector and ghost-line recorder
///   • ActionContext      — per-frame H4 binding struct published to consumers
///
/// H4 pipeline each frame (_Process):
///   1. MagicCastingState.Tick(delta)          — insanity charge / decay
///   2. MovementTrail.Sample(pos)              — movement classification
///   3. Classify ActionMeta                    — NONE / MOVE / CAST / MOVE+CAST
///   4. Mirror T_E / T_I from PlayerHElement   — when sibling is present
///   5. RecomputeBelief()                      — Belief( Emotion ^ (Intent * ActionMeta) )
///   6. Publish ActionContext                  — readable by HUD and other consumers
///
/// Input map actions registered automatically:
///   move_forward, move_back, move_left, move_right, sprint, interact
///
/// Scene layout expected:
///   PlayerController (CharacterBody3D)
///   └─ Camera3D          ← child named "Camera3D"
///
/// Optional siblings located by NodePath:
///   ObserverController   ← bound in _Ready so the manifold mirrors the player
///   PlayerHElement       ← read each frame for Emotion / Intent / Knowledge proxies
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    // ── exports — movement ────────────────────────────────────────────────────
    [Export] public float Speed            = 6.0f;
    [Export] public float SprintMultiplier = 2.0f;
    [Export] public float MouseSensitivity = 0.2f;
    [Export] public float Gravity          = 9.8f;
    [Export] public bool  EnableGravity    = true;

    // ── exports — node paths ──────────────────────────────────────────────────
    [Export] public NodePath ObserverPath      = "/root/World/Observer";
    [Export] public NodePath PlayerHElementPath = "";   // leave blank to skip H-element coupling

    // ── exports — magic / insanity tunables ───────────────────────────────────
    [Export] public float InsanityRate              = 1.0f;
    [Export] public float InsanityDecayRate         = 0.4f;
    [Export] public float InsanityWarningThreshold  = 5.0f;

    // ── exports — movement trail ──────────────────────────────────────────────
    [Export] public float MovementThreshold = 0.01f;
    [Export] public int   MaxTrailLength    = 512;

    // ── private — Godot state ─────────────────────────────────────────────────
    private Camera3D?        _camera;
    private float            _pitch;
    private bool             _mouseCaptured;

    // ── H4 sub-systems ────────────────────────────────────────────────────────
    private readonly MagicCastingState _magic = new MagicCastingState();
    private readonly MovementTrail     _trail = new MovementTrail();

    /// <summary>
    /// Live per-frame H4 action context.
    /// Read by HUDManifoldDisplay, EntropyAnchorNode3D, or any other consumer.
    /// </summary>
    public ActionContext Context { get; private set; }

    // ── optional coupling ─────────────────────────────────────────────────────
    private PlayerHElement? _hElement;

    // ── lifecycle ─────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _camera = GetNodeOrNull<Camera3D>("Camera3D");
        if (_camera == null)
            GD.PushWarning("PlayerController: no Camera3D child found.");

        // Apply inspector tunables to sub-systems
        _magic.InsanityRate              = InsanityRate;
        _magic.InsanityDecayRate         = InsanityDecayRate;
        _magic.InsanityWarningThreshold  = InsanityWarningThreshold;
        _trail.MovementThreshold         = MovementThreshold;
        _trail.MaxTrailLength            = MaxTrailLength;

        EnsureInputActions();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _mouseCaptured  = true;

        // Bind to ObserverController
        var observer = GetNodeOrNull<ObserverController>(ObserverPath);
        observer?.BindPlayer(this, _camera);

        // Optional H-element coupling for Emotion / Intent / Knowledge mirroring
        if (!PlayerHElementPath.IsEmpty)
            _hElement = GetNodeOrNull<PlayerHElement>(PlayerHElementPath);
    }

    // ── input — LMB cast / mouse-look / escape ────────────────────────────────
    public override void _UnhandledInput(InputEvent @event)
    {
        // ── Magic cast: press → start, release → stop ──────────────────────
        if (@event.IsActionPressed("interact"))
            _magic.StartMagic();

        if (@event.IsActionReleased("interact"))
            _magic.StopMagic();

        // ── Mouse-look ─────────────────────────────────────────────────────
        if (@event is InputEventMouseMotion motion && _mouseCaptured)
        {
            RotateY(Mathf.DegToRad(-motion.Relative.X * MouseSensitivity));

            _pitch = Mathf.Clamp(
                _pitch - motion.Relative.Y * MouseSensitivity,
                -80f, 80f);

            if (_camera != null)
                _camera.RotationDegrees = new Vector3(_pitch, 0f, 0f);
        }

        // ── Mouse capture toggle ───────────────────────────────────────────
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            _mouseCaptured = !_mouseCaptured;
            Input.MouseMode = _mouseCaptured
                ? Input.MouseModeEnum.Captured
                : Input.MouseModeEnum.Visible;
        }
    }

    // ── _Process — H4 pipeline (non-physics, runs every rendered frame) ───────
    public override void _Process(double delta)
    {
        // 1. Advance magic state (insanity charge / decay / warning)
        _magic.Tick(delta);

        // 2. Sample movement trail from current world position
        var pos = GlobalTransform.Origin;
        _trail.Sample(pos);

        // 3. Classify ActionMeta (discrete H4 four-mode dichotomy)
        bool moving = _trail.IsMoving;
        bool casting = _magic.IsCasting;

        int actionMeta = (moving, casting) switch
        {
            (false, false) => ActionContext.ACTION_NONE,
            (true,  false) => ActionContext.ACTION_MOVE,
            (false, true)  => ActionContext.ACTION_CAST,
            (true,  true)  => ActionContext.ACTION_MOVE_CAST
        };

        // 4. Mirror cognitive field scalars from PlayerHElement when available
        float emotion   = 0f;
        float intent    = 0f;
        float knowledge = 0f;

        if (_hElement != null)
        {
            var snap  = _hElement.GetSnapshot();
            emotion   = snap.EPhase;           // Valence: −1 (fear) … +1 (joy)
            intent    = snap.IMagnitude;        // Goal-drive pressure [0, 1]
            knowledge = snap.KPhase;            // Semantic coherence [0, 1]
        }

        // 5. Build and recompute this frame's ActionContext
        var ctx = new ActionContext
        {
            Emotion    = emotion,
            Intent     = intent,
            ActionMeta = actionMeta,
            Knowledge  = knowledge,
            Insanity   = _magic.Insanity,
            Location   = pos
        };
        ctx.RecomputeBelief();
        Context = ctx;
    }

    // ── _PhysicsProcess — movement ────────────────────────────────────────────
    public override void _PhysicsProcess(double delta)
    {
        var vel = Velocity;

        // Gravity
        if (EnableGravity && !IsOnFloor())
            vel.Y -= Gravity * (float)delta;
        else if (IsOnFloor())
            vel.Y = 0f;

        // Planar movement
        var inputDir = Vector3.Zero;
        inputDir.X = Input.GetActionStrength("move_right")   - Input.GetActionStrength("move_left");
        inputDir.Z = Input.GetActionStrength("move_back")    - Input.GetActionStrength("move_forward");

        var direction = (Transform.Basis * inputDir).Normalized();
        var speed     = Input.IsActionPressed("sprint") ? Speed * SprintMultiplier : Speed;

        if (direction != Vector3.Zero)
        {
            vel.X = direction.X * speed;
            vel.Z = direction.Z * speed;
        }
        else
        {
            vel.X = Mathf.MoveToward(vel.X, 0f, speed);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, speed);
        }

        Velocity = vel;
        MoveAndSlide();
    }

    // ── trail access (for Line3D / ImmediateMesh wiring) ─────────────────────
    /// <summary>
    /// Read-only view of the movement ghost-trail.
    /// Pass to a Line3D or ImmediateMesh in a dedicated visual node.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<Vector3> MovementTrail
        => _trail.Trail;

    // ── helpers ───────────────────────────────────────────────────────────────
    private static void EnsureInputActions()
    {
        AddActionIfMissing("move_forward", Key.W);
        AddActionIfMissing("move_back",    Key.S);
        AddActionIfMissing("move_left",    Key.A);
        AddActionIfMissing("move_right",   Key.D);
        AddActionIfMissing("sprint",       Key.Shift);
        // "interact" — bound to LMB for magic cast.
        // Register key fallback (F) so keyboard-only testing works without a mouse.
        AddActionIfMissing("interact",     Key.F);
        AddMouseActionIfMissing("interact", MouseButton.Left);
    }

    private static void AddActionIfMissing(string action, Key key)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);

        var ev = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true };
        if (!InputMap.ActionHasEvent(action, ev))
            InputMap.ActionAddEvent(action, ev);
    }

    private static void AddMouseActionIfMissing(string action, MouseButton btn)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);

        var ev = new InputEventMouseButton { ButtonIndex = btn, Pressed = true };
        if (!InputMap.ActionHasEvent(action, ev))
            InputMap.ActionAddEvent(action, ev);
    }
}
