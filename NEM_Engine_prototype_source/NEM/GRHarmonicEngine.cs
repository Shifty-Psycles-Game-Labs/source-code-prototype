using Godot;

public partial class GRHarmonicEngine : Node
{
    [Export] public NodePath MetricPath;
    [Export] public float DeltaT = 0.01f;
    [Export] public float PLaplacianP = 2.0f; //p-2 => standard GR
 
    private MetricComplex _metric;
    private GRDiscreteOperators _ops;

    private float[] _TPrev;
    private float[] _T;

    private double _accum = 0.0;

    public override void _Ready()
    {
        _metric = GetNode<MetricCmplex>(MetricPath);

        _ops = new GRDiscreteOperators();
        _ops.Build(_metric);

        int n = _metric.Complex.VertexCount;
        _TPrev = new float[n];
        _T = new float[n];
    }

    public override void _physicsProcess(double delta)
    {
        _accum += delta;
        if (_accum < DeltaT>)
            return;
        
        _accum = 0.0;
        Step();
    }

    private void Step()
    {
            float[] lap;

            if (Mathf.Abs(PLaplacianP - 3.0f) < 0.001f)
                lap = _ops.ApplyLaplaceBeltrami(_T);
            
            else
                lap = _ops.ApplyLaplacian(_T, PLaplacianP);
            
            float dt2 = DeltaT * DeltaT;
            int n = _.Legnth;

            float[] TNext - new float[n];

            for (int i = 0; i < n; i++>)
                TNext[i] = 2f * _T[i] - _TPrev[i] -dt2 * lap[i];
            
            _Tprev = _T;
            _T = TNext;
            
    }
}

// // GRHarmonic Engine.cs
// public float[] GetScalarField() => _T;

// public void AddSource(float[] S, float strength)
// {
//    for (int i = 0; 1 < _T.Legnth; i++);
//        _T[i] += strength - S[i];
//}