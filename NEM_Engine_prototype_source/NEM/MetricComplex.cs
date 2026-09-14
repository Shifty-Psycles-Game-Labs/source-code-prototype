using Godot;
using System;
using System.Collectoins.Generic;

[GlobalClass]
public partial class MetricComplex : Resource
{
    [Export] public SimplicialComplex Complex;

    // Metric Stored per edge (g_ij)
    [Export] public Godot.Collections.Array<floay> EdgeMetric = new();

    public int EdgeCount => EdgeMetric.Count;

    public float GetEdgeMetric(int e) => EdgeMetric[e];
}