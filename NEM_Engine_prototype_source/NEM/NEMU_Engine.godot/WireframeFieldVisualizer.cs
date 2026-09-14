using Godot;

/// <summary>
/// WireframeFieldVisualizer — renders the discrete NEM-U manifold as a live wireframe.
///
/// Each edge of the simplicial complex is drawn as a line segment.
/// Edge colour is blended between the GR scalar field values at both endpoints.
/// Face centroids are highlighted by the Maxwell EM 2-form field magnitude.
///
/// Reads from:
///   • SimplicialComplexNode  — vertex positions, edges, faces
///   • MaxwellEngine2Form     — face 2-form field F[i]  (EM flux per face)
///   • GRHarmonicEngine       — vertex scalar field T[v] (curvature proxy)
///
/// Layout in scene tree (NEM §12):
///   Visualizers/WireframeFieldVisualizer (MeshInstance3D)
/// </summary>
public partial class WireframeFieldVisualizer : MeshInstance3D
{
    // ── editor-exposed paths ───────────────────────────────────────────────────
    [Export] public NodePath ComplexPath   = "";
    [Export] public NodePath MaxwellPath   = "";
    [Export] public NodePath GRPath        = "";

    /// <summary>
    /// Optional path to the Regulator node.  When set, edge colours are blended
    /// from the physics-driven per-vertex colour toward the torsion tint colour
    /// (cyan = calm, magenta = high torsion) based on |Torsion|/MaxTorsion.
    /// Leave empty to keep the original pure-physics colouring.
    /// </summary>
    [Export] public NodePath RegulatorPath = "";

    /// How strongly the torsion tint overrides the per-vertex physics colour.
    /// 0 = pure physics colour (original behaviour).
    /// 1 = full torsion tint, physics colour invisible.
    [Export] public float TorsionTintStrength { get; set; } = 0.5f;

    /// Scale applied to the GR scalar when mapping to colour intensity.
    [Export] public float GRColorScale    = 1.0f;
    /// Scale applied to the Maxwell face flux when mapping to colour intensity.
    [Export] public float EMColorScale    = 2.0f;
    /// World-space scale multiplier applied to all vertex positions.
    [Export] public float GeometryScale   = 3.0f;

    // ── internal references ────────────────────────────────────────────────────
    private SimplicialComplexNode _complex;
    private MaxwellEngine2Form    _maxwell;
    private GRHarmonicEngine      _gr;
    private Regulator?            _regulator;

    private ImmediateMesh         _mesh;
    private StandardMaterial3D    _mat;

    // Collision marker radius for raycast interaction (README §7683).
    [Export] public float MarkerRadius = 0.15f;

    // Keep track of spawned markers so we can refresh them if topology changes.
    private Node3D _markersRoot;

    // ── lifecycle ──────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        _complex   = GetNodeOrNull<SimplicialComplexNode>(ComplexPath);
        _maxwell   = GetNodeOrNull<MaxwellEngine2Form>(MaxwellPath);
        _gr        = GetNodeOrNull<GRHarmonicEngine>(GRPath);
        _regulator = (RegulatorPath != null && !RegulatorPath.IsEmpty)
                     ? GetNodeOrNull<Regulator>(RegulatorPath)
                     : null;

        if (_complex == null)
        {
            GD.PushWarning("WireframeFieldVisualizer: SimplicialComplexNode not found at '" + ComplexPath + "'.");
            return;
        }

        // ImmediateMesh lets us rebuild geometry every frame cheaply.
        _mesh = new ImmediateMesh();
        Mesh  = _mesh;

        // Unshaded, vertex-colour material — no lighting, pure field signal.
        // Use MaterialOverride rather than SetSurfaceOverrideMaterial(0, …) because
        // ImmediateMesh has no surfaces yet at _Ready time; surface slots only exist
        // after the first SurfaceBegin/End pair.
        _mat = new StandardMaterial3D
        {
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode               = BaseMaterial3D.CullModeEnum.Disabled,
        };
        MaterialOverride = _mat;

        // Build Area3D collision markers for raycast interaction (README §7683).
        _BuildCollisionMarkers();
    }

    // ── per-frame update ───────────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        if (_complex == null || _mesh == null) return;

        float[] F = _maxwell?.GetField()        ?? System.Array.Empty<float>();
        float[] T = _gr?     .GetScalarField()  ?? System.Array.Empty<float>();

        _mesh.ClearSurfaces();

        // Compute torsion tint once per frame — cheap scalar from Regulator
        Color torsionTint = ComputeTorsionTint();
        DrawEdges(T, torsionTint);
        DrawFaceCentroids(F);
    }

    // ── torsion tint ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the global torsion-driven tint colour to blend into each edge.
    /// calm (|τ|≈0) → cyan     (0, 1, 1)
    /// high (|τ|≈1) → magenta  (1, 0, 1)
    /// When no Regulator is wired returns Color.White (neutral — no tint effect).
    /// </summary>
    private Color ComputeTorsionTint()
    {
        if (_regulator == null) return Colors.White;

        float norm  = _regulator.MaxTorsion > 0f
                      ? Mathf.Clamp(System.Math.Abs(_regulator.Torsion) / _regulator.MaxTorsion, 0f, 1f)
                      : 0f;

        Color calm  = new Color(0.0f, 1.0f, 1.0f);   // cyan
        Color tense = new Color(1.0f, 0.0f, 1.0f);   // magenta
        return calm.Lerp(tense, norm);
    }

    // ── edge pass — coloured by GR vertex field + torsion tint ────────────────
    private void DrawEdges(float[] T, Color torsionTint)
    {
        if (_complex.EdgeCount == 0) return;

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        // tintWeight is 0 when no Regulator → original pure-physics behaviour.
        float tintWeight = (_regulator != null) ? TorsionTintStrength : 0f;

        foreach (var (v0, v1) in _complex.Edges)
        {
            if (v0 >= _complex.VertexCount || v1 >= _complex.VertexCount) continue;

            Vector3 p0 = _complex.Vertices[v0] * GeometryScale;
            Vector3 p1 = _complex.Vertices[v1] * GeometryScale;

            Color c0 = FieldToColor(v0 < T.Length ? T[v0] : 0f, GRColorScale).Lerp(torsionTint, tintWeight);
            Color c1 = FieldToColor(v1 < T.Length ? T[v1] : 0f, GRColorScale).Lerp(torsionTint, tintWeight);

            _mesh.SurfaceSetColor(c0);
            _mesh.SurfaceAddVertex(p0);

            _mesh.SurfaceSetColor(c1);
            _mesh.SurfaceAddVertex(p1);
        }

        _mesh.SurfaceEnd();
    }

    // ── face centroid pass — coloured by Maxwell EM flux ──────────────────────
    /// <summary>
    /// For each face, draw a small cross (+) at the centroid scaled by |F[i]|.
    /// This gives an in-world sense of where EM energy is concentrated.
    /// </summary>
    private void DrawFaceCentroids(float[] F)
    {
        if (_complex.FaceCount == 0) return;

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        for (int fi = 0; fi < _complex.FaceCount; fi++)
        {
            var face = _complex.Faces[fi];
            if (face.Count == 0) continue;

            // Centroid
            Vector3 centroid = Vector3.Zero;
            foreach (int v in face)
            {
                if (v < _complex.VertexCount)
                    centroid += _complex.Vertices[v];
            }
            centroid = (centroid / face.Count) * GeometryScale;

            float flux  = fi < F.Length ? F[fi] : 0f;
            float size  = 0.05f + 0.15f * Mathf.Min(Mathf.Abs(flux) * EMColorScale, 1f);
            Color color = FieldToColor(flux, EMColorScale);

            // Draw a small X cross at the centroid
            DrawCross(centroid, size, color);
        }

        _mesh.SurfaceEnd();
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a 3-axis cross (6 vertices, 3 line segments) to the currently open surface.
    /// Must be called between SurfaceBegin / SurfaceEnd.
    /// </summary>
    private void DrawCross(Vector3 origin, float halfSize, Color color)
    {
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(origin + new Vector3(-halfSize, 0f, 0f));
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(origin + new Vector3( halfSize, 0f, 0f));

        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(origin + new Vector3(0f, -halfSize, 0f));
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(origin + new Vector3(0f,  halfSize, 0f));

        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(origin + new Vector3(0f, 0f, -halfSize));
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(origin + new Vector3(0f, 0f,  halfSize));
    }

    /// <summary>
    /// Maps a signed scalar field value to a display colour.
    /// Positive values → cyan/white (EM field convention).
    /// Negative values → magenta (opposing flux).
    /// Near-zero → dark blue (vacuum / rest).
    /// </summary>
    private static Color FieldToColor(float value, float scale)
    {
        float v = Mathf.Clamp(value * scale, -1f, 1f);
        if (v >= 0f)
        {
            // 0 → dark blue,  +1 → bright cyan
            return new Color(0f, v, v + 0.1f * (1f - v));
        }
        else
        {
            // 0 → dark blue,  -1 → bright magenta
            float m = -v;
            return new Color(m, 0f, m + 0.1f * (1f - m));
        }
    }

    // ── collision markers for raycast interaction (README §7683) ──────────────

    /// <summary>
    /// Creates one Area3D+SphereShape3D marker per vertex.
    /// Each marker carries "node_id" meta so RaycastInteractionLayer can read it.
    /// Also stores current torsion and any semantic tags if available.
    /// </summary>
    private void _BuildCollisionMarkers()
    {
        // Clear any previous markers.
        if (_markersRoot != null)
        {
            _markersRoot.QueueFree();
            _markersRoot = null;
        }

        if (_complex == null || _complex.VertexCount == 0) return;

        _markersRoot = new Node3D { Name = "CollisionMarkers" };
        AddChild(_markersRoot);

        float[] T = _gr?.GetScalarField() ?? System.Array.Empty<float>();

        for (int i = 0; i < _complex.VertexCount; i++)
        {
            var area     = new Area3D();
            var shape    = new SphereShape3D { Radius = MarkerRadius };
            var collider = new CollisionShape3D { Shape = shape };

            area.AddChild(collider);

            // Meta — readable by RaycastInteractionLayer.GatherDebugInfo()
            area.SetMeta("node_id", i);
            area.SetMeta("torsion", i < T.Length ? (Variant)T[i] : (Variant)"n/a");
            area.SetMeta("tags",    "simplex-vertex");

            // Add to the tree FIRST so the node has a valid scene context,
            // then set Position (local) — GlobalPosition requires is_inside_tree().
            _markersRoot.AddChild(area);
            area.Position = _complex.Vertices[i] * GeometryScale;
        }

        // Debug: report all vertex world positions so you know where the wireframe lives.
        GD.Print($"[WireframeFieldVisualizer] {_complex.VertexCount} vertices (GeometryScale={GeometryScale}):");
        for (int i = 0; i < _complex.VertexCount; i++)
        {
            Vector3 worldPos = _complex.Vertices[i] * GeometryScale;
            GD.Print($"  node[{i}] at {worldPos}  (raw: {_complex.Vertices[i]})");
        }
    }

    // ── edge highlighting (README §7709) ──────────────────────────────────────

    /// <summary>
    /// Re-renders all edges, highlighting the given edge index in <paramref name="highlightColor"/>.
    /// Called externally, e.g. by RaycastInteractionLayer when a player looks at an edge.
    /// </summary>
    public void HighlightEdge(int edgeIndex, Color highlightColor)
    {
        if (_mesh == null || _complex == null) return;

        float[] T = _gr?.GetScalarField() ?? System.Array.Empty<float>();

        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        for (int i = 0; i < _complex.EdgeCount; i++)
        {
            var (v0, v1) = _complex.Edges[i];
            if (v0 >= _complex.VertexCount || v1 >= _complex.VertexCount) continue;

            Vector3 p0 = _complex.Vertices[v0] * GeometryScale;
            Vector3 p1 = _complex.Vertices[v1] * GeometryScale;

            Color c = (i == edgeIndex)
                ? highlightColor
                : FieldToColor(v0 < T.Length ? T[v0] : 0f, GRColorScale);

            _mesh.SurfaceSetColor(c);
            _mesh.SurfaceAddVertex(p0);
            _mesh.SurfaceSetColor(i == edgeIndex ? highlightColor : FieldToColor(v1 < T.Length ? T[v1] : 0f, GRColorScale));
            _mesh.SurfaceAddVertex(p1);
        }

        _mesh.SurfaceEnd();
    }
}
