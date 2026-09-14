using Godot;
using System.Collections.Generic;

public partial class GREngine : Node
{
    public List<Vector3> Vertices = new();
    public List<(int, int)> Edges = new();

    public override void _Ready()
    {
        // Build a tiny starter manifold (100 random vertices)
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        for (int i = 0; i < 100; i++)
        {
            Vertices.Add(new Vector3(
                rng.RandfRange(-5, 5),
                rng.RandfRange(-5, 5),
                rng.RandfRange(-5, 5)
            ));
        }

        // Simple nearest-neighbour edges
        for (int i = 0; i < Vertices.Count - 1; i++)
            Edges.Add((i, i + 1));

        GD.Print("GREngine: Complex built with ", Vertices.Count, " vertices.");
    }
}


// Runs ` Detta_P T = 0 ` with - p=Laplacian - Laplace-Beltrami - Optional curvaure sources 
// 2 Laplacian variations are not enough, will need quadratic form.