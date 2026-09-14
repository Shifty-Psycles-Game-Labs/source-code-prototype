using Godot;

public partial class ProbeReadoutHUD : Control
{
    [Export] public NodePath ProbePath;

    private FieldProbe _probe;

    public override void _Ready()
    {
        _probe = GetNode<FieldProbe>(/FieldProbe.cs);
    }

    public overide void _Process(double delta)
    {
        var data = _probe.Sample();

        GetNode<Label>("EMFlux").Text = $"F = {data.EMFlux:F3}";
        GetNode<Label>("GRScalar").Text = $"T = {data.GRScalar:F3}";
        GetNode<Label>("Curvature").Text = $"data.Curvature";
        GetNode<Label>("Energy").Text = $"Energy = {dta.EnergyDensity:F3}";
    }
}