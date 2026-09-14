// A tensor field stored at verticies (rank 2-symmetric).
using Godot;

public partial class TensorField : Resource

    // T[i] = 4x4 symmetric tensor at vertex i
    [Export] public Godot.Collections.Array<Matrix4x4> Values = new();

    public int Count => Values.Count;

    public Matrixx4x4 this[int i]
    {

        get => Values[i];
        set => Values[i] = value;
    } 