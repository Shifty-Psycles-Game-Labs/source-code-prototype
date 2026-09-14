using System;
using Godot;

/// <summary>
/// FieldProbe — samples the EM and GR fields at a world-space position.
///
/// Finds the nearest vertex/face in the SimplicialComplexNode and reads
/// the corresponding field values from MaxwellEngine2Form and GRHarmonicEngine.
/// </summary>
public partial class FieldProbe : Node3D
{
    [Export] public NodePath MaxwellPath      { get; set; } = "";
    [Export] public NodePath GRPath           { get; set; } = "";
    [Export] public NodePath SimplicialPath   { get; set; } = "";
    [Export] public float    ProbeRadius      { get; set; } = 0.5f;

    private MaxwellEngine2Form?     _maxwell;
    private GRHarmonicEngine?       _gr;
    private SimplicialComplexNode?  _simplicial;

    public override void _Ready()
    {
        _maxwell    = GetNodeOrNull<MaxwellEngine2Form>(MaxwellPath);
        _gr         = GetNodeOrNull<GRHarmonicEngine>(GRPath);
        _simplicial = GetNodeOrNull<SimplicialComplexNode>(SimplicialPath);
    }

    public ProbeData Sample()
    {
        Vector3 pos = GlobalTransform.Origin;

        float[] field  = _maxwell?.GetField()      ?? Array.Empty<float>();
        float[] scalar = _gr?.GetScalarField()     ?? Array.Empty<float>();

        int nearestFace   = FindNearestFace(pos);
        int nearestVertex = FindNearestVertex(pos);

        // Guard against -1 (empty mesh) or out-of-range
        float fVal = (field.Length > 0 && nearestFace   >= 0 && nearestFace   < field.Length)  ? field[nearestFace]   : 0.0f;
        float tVal = (scalar.Length > 0 && nearestVertex >= 0 && nearestVertex < scalar.Length) ? scalar[nearestVertex] : 0.0f;

        return new ProbeData
        {
            Position      = pos,
            EMFlux        = fVal,
            GRScalar      = tVal,
            Curvature     = Mathf.Abs(tVal),
            EnergyDensity = Mathf.Abs(fVal)
        };
    }

    /// <summary>
    /// Returns the index of the face whose first vertex is nearest to pos.
    /// Returns -1 if the complex has no faces.
    /// </summary>
    private int FindNearestFace(Vector3 pos)
    {
        if (_simplicial == null || _simplicial.Faces.Count == 0) return -1;

        int   bestIndex = 0;
        float bestDist  = float.MaxValue;

        for (int f = 0; f < _simplicial.Faces.Count; f++)
        {
            var face = _simplicial.Faces[f];
            if (face.Count == 0) continue;
            // Use the first vertex of the face as face representative
            Vector3 v = _simplicial.Vertices[face[0]];
            float d   = pos.DistanceTo(v);
            if (d < bestDist) { bestDist = d; bestIndex = f; }
        }

        return bestIndex;
    }

    /// <summary>
    /// Returns the index of the vertex nearest to pos.
    /// Returns -1 if the complex has no vertices.
    /// </summary>
    private int FindNearestVertex(Vector3 pos)
    {
        if (_simplicial == null || _simplicial.Vertices.Count == 0) return -1;

        int   bestIndex = 0;
        float bestDist  = float.MaxValue;

        for (int i = 0; i < _simplicial.Vertices.Count; i++)
        {
            float d = pos.DistanceTo(_simplicial.Vertices[i]);
            if (d < bestDist) { bestDist = d; bestIndex = i; }
        }

        return bestIndex;
    }
}

public struct ProbeData
{
    public Vector3 Position;
    public float   EMFlux;
    public float   GRScalar;
    public float   Curvature;
    public float   EnergyDensity;
}
