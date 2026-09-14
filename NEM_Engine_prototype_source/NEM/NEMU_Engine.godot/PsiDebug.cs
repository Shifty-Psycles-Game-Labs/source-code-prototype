using Godot;

/// <summary>
/// PsiDebug — live 3D debug widget for the Regulator / ψ-field.
///
/// Scene layout (add as a Node3D in your World):
///   PsiDebug  (Node3D)  ← this script
///   ├─ BarMesh          (MeshInstance3D with a CubeMesh)
///   └─ Label            (Label3D, position above the bar)
///
/// Drag your Regulator node into the [Reg] export slot in the inspector.
///
/// Visual encoding:
///   Bar height  → |torsion| / MaxTorsion   (tall = saturated, flat = calm)
///   Bar colour  → blue (calm) → red (high torsion), mapped through HSV hue
///   Label text  → ψ, τ (torsion), P (potential), AT, mode tag
/// </summary>
public partial class PsiDebug : Node3D
{
    // ── exports ────────────────────────────────────────────────────────────────
    [Export] public Regulator?      Reg      { get; set; }
    [Export] public MeshInstance3D? BarMesh  { get; set; }
    [Export] public Label3D?        Label    { get; set; }

    /// <summary>World-space Y height of the bar when |torsion| == MaxTorsion.</summary>
    [Export] public float MaxBarHeight { get; set; } = 2.0f;

    /// <summary>Minimum bar Y scale so it is always visible even at zero torsion.</summary>
    [Export] public float MinBarHeight { get; set; } = 0.05f;

    // ── internal ───────────────────────────────────────────────────────────────
    // We own one material instance and mutate only its AlbedoColor each frame.
    // This avoids the per-frame GetActiveMaterial() lookup and cache invalidation.
    private StandardMaterial3D? _barMat;

    // ── lifecycle ──────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        if (BarMesh == null) return;

        // Create a dedicated unshaded material and assign it as an override so
        // we can update AlbedoColor without touching the mesh asset itself.
        _barMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        BarMesh.MaterialOverride = _barMat;
    }

    // ── per-frame update ───────────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        if (Reg == null) return;

        float torsionNorm = Mathf.Clamp(
            Reg.MaxTorsion > 0f ? System.Math.Abs(Reg.Torsion) / Reg.MaxTorsion : 0f,
            0f, 1f);

        UpdateBar(torsionNorm);
        UpdateLabel();
    }

    // ── bar ────────────────────────────────────────────────────────────────────

    private void UpdateBar(float torsionNorm)
    {
        if (BarMesh == null || _barMat == null) return;

        // Height: lerp between min and max
        float height = Mathf.Lerp(MinBarHeight, MaxBarHeight, torsionNorm);
        var scale    = BarMesh.Scale;
        scale.Y      = height;
        BarMesh.Scale = scale;

        // Hue: 0.6 (blue) → 0.0 (red) as torsion rises
        float hue = Mathf.Lerp(0.6f, 0.0f, torsionNorm);
        _barMat.AlbedoColor = Color.FromHsv(hue, 0.8f, 0.9f);
    }

    // ── label ──────────────────────────────────────────────────────────────────

    private void UpdateLabel()
    {
        if (Label == null || Reg == null) return;

        float at      = Reg.LastAT;
        string mode   = Reg.IsIdle ? "[idle]" : "[active]";
        string modeTag = Reg.Psi >= 0.1f ? "calm" : (Reg.Psi <= 0.03f ? "chaos" : "normal");

        Label.Text =
            $"ψ  = {Reg.Psi:0.000}\n" +
            $"τ  = {Reg.Torsion:0.0}\n" +
            $"P  = {Reg.Potential:0.0}\n" +
            $"AT = {at:0.00}\n" +
            $"{modeTag} {mode}";

        // Label colour mirrors bar hue for quick at-a-glance reading
        float torsionNorm = Mathf.Clamp(
            Reg.MaxTorsion > 0f ? System.Math.Abs(Reg.Torsion) / Reg.MaxTorsion : 0f,
            0f, 1f);
        Label.Modulate = Color.FromHsv(Mathf.Lerp(0.6f, 0.0f, torsionNorm), 0.6f, 1.0f);
    }
}
