Main.tscn
    SimplicialComplex
    HodgeLaplacian
    Maxwell2Form
    RamingSampler (Node3D)

// RovingamplerTestScene.cs
public partial class RoamingSampler : Node3D
{
    public SimplicialComplex Complex;
    public Maxwell2Form Field;

    public override void _Process(double delta)
    {
        // Move randomly
        GlobalPosition += new Vector3(
            GD.Randf() - 0.5f,
            GD.Randf() - 0.5f,
            GD.Randf() - 0.5f
        );

        // Sample Nearest vertex
        var v = Complex.GetNearestVertex(GlobalPosition);
        var value = Field.F[v.Index];

        // Visualize
        Modulate = new Color(1, 1 - value, 1 - value);
    }
}
// This gives a visible, moving probe, a `H_n` into harmonic manifold. 
