//  SimplicialComplexNode.cs
public partial class SimpleicialComplex : Node
{
    public List<Vertex> Verticies = new();
    public List<Edge> Edges = new();
    public List<Face> Faces = new();

    public SparseMatrix D1;
    public SparseMatrix D2;

    public void BuildIncidenceMatrices()
    {
        D1 = IncidenceBuilder.BuildD1(Verticies, Edges);
        D2 = IncidenceBuilder.BuildD2(Edges, Faces);
    }

}
// Thus the Formululation of everything harmonic.