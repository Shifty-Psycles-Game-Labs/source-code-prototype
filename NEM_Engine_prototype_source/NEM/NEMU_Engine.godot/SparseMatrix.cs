using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Single;

/// <summary>
/// Minimal Compressed Sparse Row (CSR) matrix for discrete Hodge Laplacian ops.
/// Rows x Cols shape. RowPtr[r]..RowPtr[r+1] indexes into ColIdx/Values for row r.
/// </summary>
public class SparseMatrix
{
    public int Rows { get; }
    public int Cols { get; }

    public readonly int[]   RowPtr;  // length = Rows + 1
    public readonly int[]   ColIdx;  // length = nnz
    public readonly float[] Values;  // length = nnz

    public int NNZ => Values.Length;

    public SparseMatrix(int rows, int cols, int[] rowPtr, int[] colIdx, float[] values)
    {
        if (rowPtr.Length != rows + 1)
            throw new ArgumentException($"rowPtr must have length rows+1 ({rows + 1}), got {rowPtr.Length}.");

        Rows   = rows;
        Cols   = cols;
        RowPtr = rowPtr;
        ColIdx = colIdx;
        Values = values;
    }

    // ── y = A x ───────────────────────────────────────────────────────────────
    public float[] Multiply(float[] x)
    {
        if (x.Length != Cols)
            throw new ArgumentException($"Vector length {x.Length} != Cols {Cols}.");

        float[] y = new float[Rows];
        for (int r = 0; r < Rows; r++)
        {
            float sum = 0f;
            for (int k = RowPtr[r]; k < RowPtr[r + 1]; k++)
                sum += Values[k] * x[ColIdx[k]];
            y[r] = sum;
        }
        return y;
    }

    // ── C = A * B (sparse × sparse) ───────────────────────────────────────────
    public static SparseMatrix Multiply(SparseMatrix A, SparseMatrix B)
    {
        if (A.Cols != B.Rows)
            throw new ArgumentException($"Shape mismatch: A.Cols={A.Cols} != B.Rows={B.Rows}.");

        int rows = A.Rows;
        int cols = B.Cols;

        var rowPtr = new System.Collections.Generic.List<int> { 0 };
        var colIdx = new System.Collections.Generic.List<int>();
        var values = new System.Collections.Generic.List<float>();

        var accum = new System.Collections.Generic.Dictionary<int, float>();

        for (int r = 0; r < rows; r++)
        {
            accum.Clear();
            for (int k = A.RowPtr[r]; k < A.RowPtr[r + 1]; k++)
            {
                int cA = A.ColIdx[k];
                float vA = A.Values[k];
                for (int j = B.RowPtr[cA]; j < B.RowPtr[cA + 1]; j++)
                {
                    int cB = B.ColIdx[j];
                    accum.TryGetValue(cB, out float cur);
                    accum[cB] = cur + vA * B.Values[j];
                }
            }
            foreach (var kv in accum)
            {
                colIdx.Add(kv.Key);
                values.Add(kv.Value);
            }
            rowPtr.Add(colIdx.Count);
        }

        return new SparseMatrix(rows, cols, rowPtr.ToArray(), colIdx.ToArray(), values.ToArray());
    }

    // ── C = A + B (same shape) ────────────────────────────────────────────────
    public static SparseMatrix Add(SparseMatrix A, SparseMatrix B)
    {
        if (A.Rows != B.Rows || A.Cols != B.Cols)
            throw new ArgumentException("SparseMatrix.Add: shape mismatch.");

        int rows = A.Rows;
        var rowPtr = new System.Collections.Generic.List<int> { 0 };
        var colIdx = new System.Collections.Generic.List<int>();
        var values = new System.Collections.Generic.List<float>();

        var accum = new System.Collections.Generic.Dictionary<int, float>();

        for (int r = 0; r < rows; r++)
        {
            accum.Clear();
            for (int k = A.RowPtr[r]; k < A.RowPtr[r + 1]; k++)
                accum[A.ColIdx[k]] = A.Values[k];
            for (int k = B.RowPtr[r]; k < B.RowPtr[r + 1]; k++)
            {
                int c = B.ColIdx[k];
                accum.TryGetValue(c, out float cur);
                accum[c] = cur + B.Values[k];
            }
            foreach (var kv in accum)
            {
                colIdx.Add(kv.Key);
                values.Add(kv.Value);
            }
            rowPtr.Add(colIdx.Count);
        }

        return new SparseMatrix(rows, cols: A.Cols, rowPtr.ToArray(), colIdx.ToArray(), values.ToArray());
    }

    // ── A^T ──────────────────────────────────────────────────────────────────
    public SparseMatrix Transpose()
    {
        int nnz = Values.Length;
        int[] rowCounts = new int[Cols];

        for (int r = 0; r < Rows; r++)
            for (int k = RowPtr[r]; k < RowPtr[r + 1]; k++)
                rowCounts[ColIdx[k]]++;

        int[] tRowPtr = new int[Cols + 1];
        for (int i = 0; i < Cols; i++)
            tRowPtr[i + 1] = tRowPtr[i] + rowCounts[i];

        int[] tColIdx = new int[nnz];
        float[] tValues = new float[nnz];
        int[] offset = new int[Cols];

        for (int r = 0; r < Rows; r++)
        {
            for (int k = RowPtr[r]; k < RowPtr[r + 1]; k++)
            {
                int c = ColIdx[k];
                int dest = tRowPtr[c] + offset[c];
                tColIdx[dest] = r;
                tValues[dest] = Values[k];
                offset[c]++;
            }
        }

        return new SparseMatrix(Cols, Rows, tRowPtr, tColIdx, tValues);
    }

    // ── Identity (utility) ────────────────────────────────────────────────────
    public static SparseMatrix Identity(int n)
    {
        int[] rowPtr = new int[n + 1];
        int[] colIdx = new int[n];
        float[] values = new float[n];
        for (int i = 0; i < n; i++)
        {
            rowPtr[i]  = i;
            colIdx[i]  = i;
            values[i]  = 1f;
        }
        rowPtr[n] = n;
        return new SparseMatrix(n, n, rowPtr, colIdx, values);
    }

    // ── Empty (0×0) ───────────────────────────────────────────────────────────
    public static SparseMatrix Empty => new SparseMatrix(0, 0, new[] { 0 }, Array.Empty<int>(), Array.Empty<float>());

    // ── MathNet interop ───────────────────────────────────────────────────────

    /// <summary>
    /// Converts this CSR matrix to a MathNet dense float matrix.
    /// Suitable for eigensolvers and exact linear algebra on small complexes.
    /// </summary>
    public Matrix<float> ToMathNetDense()
    {
        var A = DenseMatrix.Create(Rows, Cols, 0f);
        for (int r = 0; r < Rows; r++)
            for (int k = RowPtr[r]; k < RowPtr[r + 1]; k++)
                A[r, ColIdx[k]] += Values[k];
        return A;
    }

    /// <summary>
    /// Creates a <see cref="SparseMatrix"/> from a MathNet dense matrix
    /// by scanning for non-zero entries.
    /// </summary>
    public static SparseMatrix FromMathNetDense(Matrix<float> m, float threshold = 0f)
    {
        int rows = m.RowCount, cols = m.ColumnCount;
        var rp = new System.Collections.Generic.List<int> { 0 };
        var ci = new System.Collections.Generic.List<int>();
        var vs = new System.Collections.Generic.List<float>();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float v = m[r, c];
                if (Math.Abs(v) > threshold) { ci.Add(c); vs.Add(v); }
            }
            rp.Add(ci.Count);
        }
        return new SparseMatrix(rows, cols, rp.ToArray(), ci.ToArray(), vs.ToArray());
    }
}
