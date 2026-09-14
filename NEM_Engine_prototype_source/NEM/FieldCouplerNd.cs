using Godot;

public partial class FieldCoupler : Node
{
    public float[] Field;
    public float[] FieldPrev;

    public override void _Ready()
    {
        var engine = GetNode<GREngine>("../GREngine");
        var ops = GetNode<GDiscreteOperators>("../HarmonicOperators");

        int n = engine.Vertices.Count;

        Field = new float[n];
        FieldPrev = new float[n];

        // Initialize field with random values
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        for (int i = 0; i < n; i++)
        {
            Field[i] = rng.RandfRange(-1f, 1f);
            FieldPrev[i] = Field[i];
        }

        GD.Print("FieldCoupler: Field initialized.");
    }

    public void Step(float dt)
    {
        var ops = GetNode<GDiscreteOperators>("../HarmonicOperators");
        int n = Field.Length;

        float[] next = new float[n];

        for (int i = 0; i < n; i++)
        {
            float lap = 0f;
            for (int j = 0; j < n; j++)
                lap += ops.Laplacian[i, j] * Field[j];

            next[i] = (2 * Field[i]) - FieldPrev[i] - (dt * dt) * lap;
        }

        FieldPrev = Field;
        Field = next;
    }
}
