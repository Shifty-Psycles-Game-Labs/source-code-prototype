using System;
using System.Collections.Generic;

/// <summary>
/// Builds the discrete coboundary (incidence) matrices for a SimplicialComplexNode.
///
///   D1  (|E| × |V|)  — edge–vertex incidence: discrete gradient / divergence
///   D2  (|F| × |E|)  — face–edge incidence:  discrete curl
///
/// Orientation convention
/// ───────────────────────
/// D1: for edge e = (v0, v1)  →  D1[e, v1] = +1,  D1[e, v0] = −1
///
/// D2: for face f = [v0, v1, v2, ...]
///     the boundary edges are (v0,v1), (v1,v2), ..., (vN-1,v0).
///     For each boundary edge (a,b) we look it up in the edge list:
///       if the edge is stored as (a,b)  → coefficient +1
///       if the edge is stored as (b,a)  → coefficient −1
///     If an edge on the face boundary is not found in the edge list it is
///     silently skipped (degenerate topology — the caller should ensure a
///     consistent edge list).
/// </summary>
public static class IncidenceBuilder
{
    // ── D1 : |E| × |V| ───────────────────────────────────────────────────────
    public static SparseMatrix BuildD1(SimplicialComplexNode complex)
    {
        int nV = complex.VertexCount;
        int nE = complex.EdgeCount;

        if (nE == 0 || nV == 0)
            return SparseMatrix.Empty;

        // Two non-zeros per row (one edge → two vertices)
        var rowPtr = new int[nE + 1];
        var colIdx = new int[nE * 2];
        var values = new float[nE * 2];

        for (int e = 0; e < nE; e++)
        {
            var (v0, v1) = complex.Edges[e];

            int row = e * 2;
            rowPtr[e] = row;

            // Order: put −1 first (lower index v0), then +1 (higher index v1)
            colIdx[row]     = v0;  values[row]     = -1f;
            colIdx[row + 1] = v1;  values[row + 1] = +1f;
        }
        rowPtr[nE] = nE * 2;

        return new SparseMatrix(nE, nV, rowPtr, colIdx, values);
    }

    // ── D2 : |F| × |E| ───────────────────────────────────────────────────────
    public static SparseMatrix BuildD2(SimplicialComplexNode complex)
    {
        int nE = complex.EdgeCount;
        int nF = complex.FaceCount;

        if (nF == 0 || nE == 0)
            return SparseMatrix.Empty;

        // Build a fast lookup: (min,max) → edge index
        var edgeLookup = BuildEdgeLookup(complex);

        var rowPtr = new List<int> { 0 };
        var colIdx = new List<int>();
        var values = new List<float>();

        for (int f = 0; f < nF; f++)
        {
            var verts = complex.Faces[f];
            int n = verts.Count;

            for (int i = 0; i < n; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % n];  // wrap-around for last boundary edge

                int lo = Math.Min(a, b);
                int hi = Math.Max(a, b);

                if (!edgeLookup.TryGetValue((lo, hi), out int eIdx))
                    continue; // edge not in the list — skip silently

                // Orientation: +1 if edge stored as (a,b), −1 if stored as (b,a)
                var (v0, v1) = complex.Edges[eIdx];
                float sign = (v0 == a) ? +1f : -1f;

                colIdx.Add(eIdx);
                values.Add(sign);
            }

            rowPtr.Add(colIdx.Count);
        }

        return new SparseMatrix(nF, nE, rowPtr.ToArray(), colIdx.ToArray(), values.ToArray());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Maps (min(v0,v1), max(v0,v1)) → edge index for O(1) face-edge lookup.</summary>
    private static Dictionary<(int, int), int> BuildEdgeLookup(SimplicialComplexNode complex)
    {
        var lookup = new Dictionary<(int, int), int>(complex.EdgeCount);
        for (int e = 0; e < complex.EdgeCount; e++)
        {
            var (v0, v1) = complex.Edges[e];
            int lo = Math.Min(v0, v1);
            int hi = Math.Max(v0, v1);
            lookup[(lo, hi)] = e;
        }
        return lookup;
    }
}
