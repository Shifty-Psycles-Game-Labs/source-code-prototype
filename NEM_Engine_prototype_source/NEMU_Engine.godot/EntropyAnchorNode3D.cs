using Godot;

/// <summary>
/// EntropyAnchorNode3D — potential clamp node for the NEM-U operator manifold.
///
/// Acts as an everywhere, all-the-time anchor for the manifold potential field:
/// it enforces a baseline floor so operator depths never collapse to zero and
/// field energies never escape to infinity.
///
/// Mathematically this is the constant potential term λ₀ in the unified operator:
///   Ω(t) = ... + λ₀   where λ₀ = ClampPotential
///
/// Designed to sit inside EngineLayer.tscn as a sibling of UnifiedEngine.
/// Other nodes can call GetClampPotential() to read the current anchor value.
/// </summary>
public partial class EntropyAnchorNode3D : Node3D
{
    /// <summary>
    /// Baseline potential floor injected into the manifold each tick.
    /// Raise to increase the "floor energy" of the universe; lower to let
    /// the field approach vacuum.
    /// </summary>
    [Export] public float ClampPotential = 1.0f;

    /// <summary>
    /// When true, the anchor broadcasts its potential to MagicFieldEngine each
    /// physics tick via SetBaselinePotential() if that method exists on the sibling.
    /// </summary>
    [Export] public bool AutoBroadcast = false;

    /// <summary>
    /// NodePath to the MagicFieldEngine sibling — used only when AutoBroadcast is true.
    /// </summary>
    [Export] public NodePath MagicFieldEnginePath = "../MagicFieldEngine";

    private MagicFieldEngine? _magic;

    public override void _Ready()
    {
        if (AutoBroadcast)
            _magic = GetNodeOrNull<MagicFieldEngine>(MagicFieldEnginePath);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!AutoBroadcast || _magic == null) return;
        // Clamp the MagicFieldEngine lambda step so it never falls below ClampPotential
        if (_magic.LambdaStep < ClampPotential)
            _magic.LambdaStep = ClampPotential;
    }

    /// <summary>Returns the current clamp potential value.</summary>
    public float GetClampPotential() => ClampPotential;
}
