using Godot;
using System.Collections.Generic;

/// <summary>
/// Owns the discrete spacetime mesh (K0 vertices, K1 edges, K2 faces) and
/// exposes the pre-built incidence matrices D1 and D2.
///
/// Populate Vertices / Edges / Faces before _Ready or call
/// BuildIncidenceMatrices() manually after changing topology.
/// </summary>
public partial class SimplicialComplexNode : Node
{
    // K0 — spacetime events (position is optional / informational)
    public List<Vector3> Vertices { get; } = new();

    // K1 — each edge is a pair of vertex indices (v0, v1), v0 < v1
    public List<(int v0, int v1)> Edges { get; } = new();

    // K2 — each face is a list of vertex indices (at least 3)
    public List<List<int>> Faces { get; } = new();

    // Pre-built incidence matrices (rebuilt whenever BuildIncidenceMatrices is called)
    public SparseMatrix D1 { get; private set; } = SparseMatrix.Empty; // |E| × |V|
    public SparseMatrix D2 { get; private set; } = SparseMatrix.Empty; // |F| × |E|

    public int VertexCount => Vertices.Count;
    public int EdgeCount   => Edges.Count;
    public int FaceCount   => Faces.Count;

    public override void _Ready()
    {
        SeedTetrahedron();
        BuildIncidenceMatrices();
    }

    /// <summary>Called by UniverseRoot.StartSimulation via reflection.</summary>
    public void Initialize()
    {
        SeedTetrahedron();
        BuildIncidenceMatrices();
    }

    /// <summary>
    /// Seeds the simplicial complex with a unit tetrahedron (4 vertices, 6 edges, 4 faces).
    /// This gives every downstream engine (Maxwell, GR, MagicField) a non-empty mesh
    /// to operate on so the simulation loop is no longer processing empty arrays.
    /// </summary>
    private void SeedTetrahedron()
    {
        Vertices.Clear();
        Edges.Clear();
        Faces.Clear();

        // K0 — four vertices of a unit tetrahedron
        Vertices.Add(new Vector3( 1f,  1f,  1f));
        Vertices.Add(new Vector3(-1f, -1f,  1f));
        Vertices.Add(new Vector3(-1f,  1f, -1f));
        Vertices.Add(new Vector3( 1f, -1f, -1f));

        // K1 — six edges (all pairs, v0 < v1)
        Edges.Add((0, 1));
        Edges.Add((0, 2));
        Edges.Add((0, 3));
        Edges.Add((1, 2));
        Edges.Add((1, 3));
        Edges.Add((2, 3));

        // K2 — four triangular faces (each triple of vertices)
        Faces.Add(new System.Collections.Generic.List<int> { 0, 1, 2 });
        Faces.Add(new System.Collections.Generic.List<int> { 0, 1, 3 });
        Faces.Add(new System.Collections.Generic.List<int> { 0, 2, 3 });
        Faces.Add(new System.Collections.Generic.List<int> { 1, 2, 3 });
    }

    public void BuildIncidenceMatrices()
    {
        D1 = IncidenceBuilder.BuildD1(this);
        D2 = IncidenceBuilder.BuildD2(this);
    }
}
