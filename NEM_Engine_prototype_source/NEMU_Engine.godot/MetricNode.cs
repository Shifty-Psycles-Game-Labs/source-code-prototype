using Godot;
using System.Collections.Generic;

public partial class MetricNode : Node3D
{
    [Export] public float DefaultEdgeLength = 1.0f;
    [Export] public float DefaultCurvature = 0.0f;

    private readonly Dictionary<int, float> _edgeWeights = new();
    private readonly Dictionary<int, float> _vertexCurvature = new();

    public override void _Ready()
    {
        // Scaffold for discrete metric state.
    }

    public void Initialize()
    {
        _edgeWeights.Clear();
        _vertexCurvature.Clear();
    }

    public void SetEdgeWeight(int edgeIndex, float weight)
    {
        _edgeWeights[edgeIndex] = weight;
    }

    public float GetEdgeWeight(int edgeIndex)
    {
        return _edgeWeights.TryGetValue(edgeIndex, out var value) ? value : DefaultEdgeLength;
    }

    public void SetVertexCurvature(int vertexIndex, float curvature)
    {
        _vertexCurvature[vertexIndex] = curvature;
    }

    public float GetVertexCurvature(int vertexIndex)
    {
        return _vertexCurvature.TryGetValue(vertexIndex, out var value) ? value : DefaultCurvature;
    }
}
