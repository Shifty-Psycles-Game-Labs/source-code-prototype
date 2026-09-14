using Godot;

[GlobalClass]
public partial class MaxwellEngineRes : Resource
{
    [Export] public int FaceCount;
    [Export] public float[] Field;      // 2-form values per face
    [Export] public float[] FieldPrev;  // leapfrog previous

    public void Init(int faceCount)
    {
        FaceCount = faceCount;
        Field = new float[faceCount];
        FieldPrev = new float[faceCount];

        var rng = new RandomNumberGenerator();
        rng.Randomize();

        for (int i = 0; i < faceCount; i++)
        {
            Field[i] = rng.RandfRange(-1f, 1f);
            FieldPrev[i] = Field[i];
        }
    }
}
