using Godot;

public partial class MaxwellEngine : Node
{
    [Export] public MaxwellEngineRes FieldRes;
    public float[,] Laplacian2; // Δ₂ on faces

    public override void _Ready()
    {
        var complex = GetNode<SimplicialComplex>("../SimplicialComplex");
        var ops = GetNode<HodgeOperators>("../HodgeOperators");

        int fCount = complex.Faces.Count;

        if (FieldRes == null)
            FieldRes = new MaxwellEngineRes();

        FieldRes.Init(fCount);
        Laplacian2 = ops.Laplacian2; // precomputed Δ₂

        GD.Print("MaxwellEngine: initialized with ", fCount, " faces.");
    }

    public void Step(float dt)
    {
        int n = FieldRes.Field.Length;
        var next = new float[n];

        for (int i = 0; i < n; i++)
        {
            float lap = 0f;
            for (int j = 0; j < n; j++)
                lap += Laplacian2[i, j] * FieldRes.Field[j];

            next[i] = (2 * FieldRes.Field[i]) - FieldRes.FieldPrev[i] - (dt * dt) * lap;
        }

        FieldRes.FieldPrev = FieldRes.Field;
        FieldRes.Field = next;
    }
}


// MaxwellEngine2Form.cs
// public float[] GetField() => _f;

// public void SetGeometryWeights(float[] weights, float stregnth)
// {
//    // eg. scale Delta2 calues per face
//    // or store weights and apply them in StepWave
// }
// Runs:
// ` Delta_2 F=0 `
// and the discrete wave equation:
// ` F_(n+1) = 2F_n -F_(n-1) - (Deltat)^2 Delta_2F_n `