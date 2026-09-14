using Godot;

/// <summary>
/// HarmonicSeed — world-gen anchor that deforms a Platonic-solid mesh using
/// live curvature and magic-flux data from the NEMU engine.
///
/// Each vertex of the assigned mesh is displaced by:
///   offset.X = curvature[v] * RecursionDepth
///   offset.Y = flux[v]      * RecursionDepth
///   offset.Z = curvature[v] * flux[v] * 0.5
///
/// where curvature[v] comes from GRHarmonicEngine.GetScalarField() (depth-scaled
/// via GR_Operator.Apply) and flux[v] comes from MagicFieldEngine.GetLocalTorsionArray().
///
/// Both arrays are indexed by the SimplicialComplex vertex index.  The seed maps
/// its mesh vertices to complex vertices by finding the nearest complex vertex to
/// each mesh vertex position (resolved once in _Ready, cached as an index table).
///
/// Scene setup:
///   — Place this node as a child of World (sibling of UnifiedEngine).
///   — Assign a Platonic-solid ArrayMesh (or any ImportedMesh) to the Mesh export.
///   — Set GrPath / MagicPath / SimplicialPath to match SceneTreeRoot.tscn paths.
/// </summary>
public partial class HarmonicSeed : MeshInstance3D
{
    // ── exports ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The source mesh whose vertices will be harmonically displaced each physics
    /// tick.  Assign a Platonic-solid or any ArrayMesh in the Inspector.
    /// </summary>
    [Export] public Mesh SourceMesh { get; set; }

    /// <summary>
    /// Tag for this seed's role in world-gen ("platonic", "leynode", etc.).
    /// Unused at runtime; informational.
    /// </summary>
    [Export] public string SeedType { get; set; } = "platonic";

    /// <summary>
    /// Depth scalar ∈ [0, 1].  Scales all displacement amplitudes.
    /// 0 = no deformation.  1 = full harmonic expression.
    /// </summary>
    [Export] public float RecursionDepth { get; set; } = 1.0f;

    // ── node paths ─────────────────────────────────────────────────────────────
    [Export] public NodePath GrPath         { get; set; } = "../GRHarmonicEngine";
    [Export] public NodePath MagicPath      { get; set; } = "../MagicFieldEngine";
    [Export] public NodePath SimplicialPath { get; set; } = "../SimplicialComplex";

    // ── private state ─────────────────────────────────────────────────────────
    private GRHarmonicEngine?      _gr;
    private MagicFieldEngine?      _magic;
    private SimplicialComplexNode? _simplicial;

    /// <summary>
    /// For each mesh vertex, the index of the nearest SimplicialComplex vertex.
    /// Built once in _Ready.
    /// </summary>
    private int[] _complexIndex = System.Array.Empty<int>();

    /// <summary>
    /// Original (rest) vertex positions from SourceMesh, used as the base for
    /// each frame's displacement so offsets don't accumulate.
    /// </summary>
    private Vector3[] _restPositions = System.Array.Empty<Vector3>();

    // ── lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        if (SourceMesh == null)
        {
            GD.PushWarning("HarmonicSeed: no SourceMesh assigned.");
            return;
        }

        _gr         = GetNodeOrNull<GRHarmonicEngine>(GrPath);
        _magic      = GetNodeOrNull<MagicFieldEngine>(MagicPath);
        _simplicial = GetNodeOrNull<SimplicialComplexNode>(SimplicialPath);

        if (_gr == null)
            GD.PushWarning("HarmonicSeed: GRHarmonicEngine not found at '" + GrPath + "'.");
        if (_magic == null)
            GD.PushWarning("HarmonicSeed: MagicFieldEngine not found at '" + MagicPath + "'.");

        // Duplicate the source mesh so we can modify vertices without touching the asset.
        Mesh = (Mesh)SourceMesh.Duplicate();

        // Cache rest positions and build the nearest-complex-vertex index table.
        _restPositions = _GetMeshVertices(Mesh);
        _complexIndex  = _BuildComplexIndex(_restPositions);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Mesh == null || _restPositions.Length == 0) return;

        float[]? curvature = _gr?.GetScalarField();
        float[]? flux      = _magic?.GetLocalTorsionArray();

        // If neither field is available yet (engine not initialised), skip silently.
        if ((curvature == null || curvature.Length == 0) &&
            (flux      == null || flux.Length      == 0))
            return;

        _ApplyHarmonics(curvature, flux);
    }

    // ── deformation ──────────────────────────────────────────────────────────

    private void _ApplyHarmonics(float[]? curvature, float[]? flux)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int v = 0; v < _restPositions.Length; v++)
        {
            int ci = _complexIndex.Length > 0 ? _complexIndex[v] : -1;

            float curv = (curvature != null && ci >= 0 && ci < curvature.Length)
                ? curvature[ci] : 0f;
            float mag  = (flux != null && ci >= 0 && ci < flux.Length)
                ? flux[ci] : 0f;

            var offset = new Vector3(
                curv * RecursionDepth,
                mag  * RecursionDepth,
                curv * mag * 0.5f
            );

            st.AddVertex(_restPositions[v] + offset);
        }

        st.GenerateNormals();
        Mesh = st.Commit();
        // _restPositions intentionally NOT updated here — it always holds the
        // original undeformed vertex positions so each tick's displacement is
        // applied relative to the rest pose, not accumulated.
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Extract all vertex positions from surface 0 of a mesh.</summary>
    private static Vector3[] _GetMeshVertices(Mesh mesh)
    {
        var arrays = mesh.SurfaceGetArrays(0);
        if (arrays == null || arrays.Count == 0)
            return System.Array.Empty<Vector3>();
        return (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
    }

    /// <summary>
    /// For each mesh vertex find the index of the nearest SimplicialComplex vertex.
    /// If the simplicial complex is not present all indices are -1 (no displacement).
    /// </summary>
    private int[] _BuildComplexIndex(Vector3[] meshVerts)
    {
        var result = new int[meshVerts.Length];
        var cverts = _simplicial?.Vertices;

        if (cverts == null || cverts.Count == 0)
        {
            // No complex: map every mesh vertex to index -1 (zero displacement).
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            return result;
        }

        for (int v = 0; v < meshVerts.Length; v++)
        {
            float best = float.MaxValue;
            int   bi   = 0;
            for (int c = 0; c < cverts.Count; c++)
            {
                float d = meshVerts[v].DistanceSquaredTo(cverts[c]);
                if (d < best) { best = d; bi = c; }
            }
            result[v] = bi;
        }
        return result;
    }
}
