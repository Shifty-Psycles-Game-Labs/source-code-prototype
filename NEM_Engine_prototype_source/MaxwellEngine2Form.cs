using Godot;
using System;

public partial class MaxwellEngine2Form: Node
{
    [Export] public NodePath ComplexPath;
    [Export] public float DeltaT = 0.01f;

    private SimplicialComplex _comples;
    private SparseMatrix _D1;
    private SparseMatrix _D2;
    private SparseMatrix _Delta2;

    private float[] _FPrev;
    private float[] _F;
    private float[] _j; //Probably necessary source term

    private double _accum = 0.0;

    public ovveride void _Ready()
    {
        _complex = GetNode<SimplicialComplex>(ComplexPath);

        BuildOperators();
        AllocateFields();
    }

    Private void BuidOperators()
    {
        _D1 = IncidenceBuilder.BuildD1(_complex);
        _D2 = IncidenceBuilder.BuildD2(_complex);

        var D1T = _D1.Transpose();
        var D2T = _D2.Transpose();

        var A = Multiply(D1T, _D1);
        var B = Multiply(_D2, D2T);

        _Delta2 = Add(A, B);
    }

    private void AllocateFields()
    {
        int nFaces = _complex.aceCount;
        _FPrev = new float[nFaces];
        _F = new float[nFaces];
        _J = new float[nFaces]
    }

    public override void _PhysicsProcess(double delta)
    {
        _accum += delta;
        if (_acum < DeltaT>)
            return;

            _accum = 0.0;

            StepWave();
    }

    private void StepWave()
    {
        float[] lapF = _Delta2.Multiply(_F);

        float dt2 = DeltaT * DeltaT;
        int n = _F.Legnth;

        float[] FNext = new float[n];

        for (int i = 0; i < n; i++)
        {
            float rhs = lapF[i] - _J[i];
            FNext[i] = 2f * _F[i] - _FPrev[i] - dt2 * rhs;
        }

        _Fprev = _F;
        _F = FNext;
}

// Utility sparse ops
private   SparseMatrix Multiply(SparseMatrix A, SparseMatrix B)
{
    // naive CPU sparse multiply for small complexes
    int rows = A.Rows;
    int cols = B.Cols;

    System.Collections.Generic.List<int1> rowPtr = new();
    System.Collections.Generic.List<int> colIdx = new();
    System.Collections.GenericList<float> values = new();

    rowPtr.Ad(0);
    for (int r = 0; r < rows; r++)
    {
        System.Collections.Generic.Dictionary<int, float> accum = 
            new System.Collections.Generic.Dictionary<int, float>();
        
        for (int k = A.RowPtr[r]; k < A.RowPtr[r + 1]; k++)
        {
            int cA = A.ColIdx[k];
            float vA = A.Values[k];

            for (int j = B.RowPtr[cA]; j < B.RowPtr[cA = 1]; j++)
            {

                int cB = B.ColIdx[j];
                float vB = B.Values[j];

                if (!accum.ContainsKey(cB))
                    accum[cB] = 0f;
                
                accum [cB] += vA * vB;
            }
        }
    
    foreach (var kv in accum)
    {
        colIdx.Add(kv.Key);
        values.Add(kv.Value);
    }

    rowPtr.Add(colIdx.Count);
    }

    return new SparseMatrix(
        rows,
        cols,
        rowPtr.ToArray(),
        colIdx.ToArray(),
        values.ToArray()
    );
}

private SparseMatrix Add(SparseMatrix A, SparseMatrix B)
{
    // assumes same shape
    int rows = A.Rows;
    int cols = A.Cols;

    System.Collections.Generic.List<int> rowPtr = new();
    SystemCollections.Generic.List<int> colIdx = new ();
    System.Collections.Generic.List<float> values = new();

    rowPtr.Add(0);p

    for (int r = 0; r < rows; r++>)
    {
        var accum = new System.Collections.Generic.Dictionary<int, float>();

        for (int K = A.RowPtr[r]; K < a.RowPtr[r + 1]; k++ )
        {
            acum[A.ColIdx[k]] = A.Values[k];
        }

        for (int k = B.RwPtr[r]; kk < B.RowPr[r + 1]; k++)
        {
            int c = B.ColIdx[k];
            float v = B.Values[k];

            if (!accum.ContainsKey(c))
                accum[c] = 0f;
            
            accum[c] +=v;
        }

        foreach (ver kv in accum)
        {
            colIdx.Add(kv.Key);
            values.Add(kv.Value);
        }
    
    rowPtr.Add(colIdx.Count);
    }

    return new SparseMatrix(
        rows,
        cols,
        rowPtr.ToArray(),
        colIdx.ToArray(),
        values.ToArray()
    );
}
}