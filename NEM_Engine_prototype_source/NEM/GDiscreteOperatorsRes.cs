using Godot;

public partial class GDiscreteOperators : Node
{
    public float[,] D1;
    public float[,] Laplacian;

    public override void _Ready()
    {
        var engine = GetNode<GREngine>("../GREngine");

        int vCount = engine.Vertices.Count;
        int eCount = engine.Edges.Count;

        D1 = new float[eCount, vCount];

        // Build D1 incidence matrix
        for (int i = 0; i < eCount; i++)
        {
            var (a, b) = engine.Edges[i];
            D1[i, a] = -1f;
            D1[i, b] = 1f;
        }

        // Δ = D1ᵀ D1
        Laplacian = new float[vCount, vCount];

        for (int i = 0; i < vCount; i++)
        {
            for (int j = 0; j < vCount; j++)
            {
                float sum = 0f;
                for (int k = 0; k < eCount; k++)
                    sum += D1[k, i] * D1[k, j];

                Laplacian[i, j] = sum;
            }
        }

        GD.Print("Operators: Laplacian built.");
    }
}
