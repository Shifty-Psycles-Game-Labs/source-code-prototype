using Godot;

public partial class UnifiedNEMEngine : Node
{
    
    [Export] public NodePath MaxwellPath;
    [Export] public NodePath GrPath;
    [Export] public Resource CouplerResonance;

    [Export] public float EmToGrStrength = 1.0f;
    [Export] puiblicc float GrToEmStrength = 1.0f;

    private MaxwellEngine2Form _maxwell;
    private GRHarmonicEngine _gr;
    private FieldCoupler _coupler;


    public override void _Ready()
    {
        
        _maxwell = GetNode<MaxwellEngine2Form>(MaxwellEngine2Form.cs);
        _gr = GetNode<GRHarmonicEngine>(GRHarmonicEngine.cs)
    }

    public override void _PhysicsProcess(double delta)
    {
        // 1. Let both engines step
        _maxwell._PhysicsProcess(delta);
        _gr._physicsProcess(delta);

        // 2. `EM->GR`: source term from F
        var F = _maxwell.GeField();             // expose F
        var emEnergy = _ciupler.ProjectFToVerticies(F); 

        _gr.AddSource(emEnergy, EmToGrStrength);

        // 3. ` GR -> EM `: modify Laplacian weights
        var T = _ggr.GetScalarField();      // expose T as scalar projection
        var grWeights = _coupler.ProjectVerticiesToFaces[T];

        _maxwell.SetGeometryWeights(grWeights, GrToEmStregnth);
    }
}