using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// UnifiedEngine — couples MaxwellEngine, GRHarmonicEngine, MagicFieldEngine,
/// and the four OM4 cognitive layers.
///
/// Full coupling pipeline (per physics tick):
///   1.  Read observer action vector → inject into ActionLayerNode + MagicFieldEngine.
///   2.  EM → GR : EM energy density on faces injected as GR source term.
///   3.  GR → EM : GR curvature field used to modulate EM geometry weights.
///   4.  GR → Cognitive : curvature injected into MagicFieldEngine intent field.
///   5.  EM → Cognitive : EM flux injected into MagicFieldEngine emotion field.
///   6.  ManifoldRegulator.Apply() — reads PlayerHElement HAM + torsion; outputs
///       GRDepth/MagicDepth/CognitiveGain; applies them to coupling scalars.
///       Subsumes previous ApplyHAMFeedback (step 6) + ApplyPlayerManifoldGate (6b).
///  6b.  NPC gestalt coupling — RelationshipCoupler.CoupleNpcToPlayer() injects player
///       manifold snapshot as boundary conditions into all NpcMind nodes.
///   7.  NLCAS positional encoding update — gather live per-vertex physics scalars → NLCASNode.
///   8.  NLCAS forward pass — PushCognitiveState → transformer → PullAttentionWeights.
///   9.  HAM torsion gate — scale NLCAS WriteBackAlpha by τ/(1+|τ|) so a high-torsion manifold
///       allows more transformer write-through.
///  10.  NLCAS gate → RelationshipCoupler.SetNLCASGate() — closes the recursive loop:
///       transformer attention output gates the next OM4 Q·K dot-product scores.
///  11.  RelationshipCoupler.CoupleOM4() — cross-field attention gated by step 10.
///  12.  RelationshipCoupler.Couple() — legacy scalar node-level coupling.
///  13.  Push MagicFieldEngine field means → EmotionFieldNode.Arousal / IntentFieldNode.Urgency.
///  14.  Emit ManifoldDiagnostics signal — torsion, axis, NPC attention weights → HUD.
///
/// Recursive attention loop (steps 8–11):
///   NLCAS output (2×2) → SetNLCASGate → CoupleOM4 Q·K scores → AttentionWeights → NLCAS input
///   This makes the manifold self-modulating: transformer attention shapes OM4 attention,
///   and OM4 attention shapes the next transformer context.
/// </summary>
public partial class UnifiedEngine : Node
{
    // ── node paths ─────────────────────────────────────────────────────────────
    [Export] public NodePath SimplicialPath   { get; set; } = "../SimplicialComplex";
    [Export] public NodePath MaxwellPath      { get; set; } = "../MaxwellEngine";
    [Export] public NodePath GrPath           { get; set; } = "../GRHarmonicEngine";
    [Export] public NodePath ObserverPath     { get; set; } = "../Observer";
    [Export] public NodePath MagicPath        { get; set; } = "../MagicFieldEngine";

    // ── OM4 cognitive layer paths ──────────────────────────────────────────────
    [Export] public NodePath KnowledgePath    { get; set; } = "../KnowledgeGraph";
    [Export] public NodePath EmotionPath      { get; set; } = "../EmotionField";
    [Export] public NodePath IntentPath       { get; set; } = "../IntentField";
    [Export] public NodePath ActionPath       { get; set; } = "../ActionLayer";
    [Export] public NodePath CouplerPath      { get; set; } = "../RelationshipCoupler";
    [Export] public NodePath RegulatorPath    { get; set; } = "../Regulator";
    [Export] public NodePath PlayerHElementPath { get; set; } = "../PlayerHElement";

    /// <summary>
    /// Path to the NLCASNode child.  When set, UnifiedEngine swaps the default
    /// NullNLCASAdapter for the live NLCASNode at _Ready time.
    /// Leave blank (empty NodePath) to keep the no-op stub.
    /// </summary>
    [Export] public NodePath NLCASPath        { get; set; } = "../NLCASNode";

    // ── coupling strengths ─────────────────────────────────────────────────────
    [Export] public float EmToGrStrength   { get; set; } = 1.0f;
    [Export] public float GrToEmStrength   { get; set; } = 1.0f;
    [Export] public float GrToCogStrength  { get; set; } = 0.3f;
    [Export] public float EmToCogStrength  { get; set; } = 0.5f;

    /// <summary>
    /// When true, ManifoldRegulator.Apply() runs each tick to throttle GR/Magic
    /// operators and shape NPC cognitive gain from player HAM torsion + axis.
    /// Disable to freeze operator depths at their default 1.0.
    /// </summary>
    [Export] public bool ManifoldRegulation { get; set; } = true;

    // ── node references ────────────────────────────────────────────────────────
    private SimplicialComplexNode? _simplicial;
    private MaxwellEngine2Form?   _maxwell;
    private GRHarmonicEngine?     _gr;
    private ObserverController?   _observer;
    private MagicFieldEngine?     _magic;

    private KnowledgeGraphNode?   _knowledge;
    private EmotionFieldNode?     _emotion;
    private IntentFieldNode?      _intent;
    private ActionLayerNode?      _action;
    private RelationshipCoupler?  _coupler;
    private Regulator?            _regulator;
    private PlayerHElement?       _playerH;

    // ── ManifoldRegulator (operator-level, not field-level) ───────────────────
    private readonly ManifoldRegulator _manifoldRegulator = new ManifoldRegulator();

    private Vector3 _latestActionVector = Vector3.Zero;
    public  Vector3 LatestActionVector => _latestActionVector;

    /// <summary>
    /// Exposes the MagicFieldEngine so HUDManifoldDisplay can read FieldHealth.
    /// </summary>
    public MagicFieldEngine? MagicField => _magic;

    // ── ManifoldDiagnostics signal ─────────────────────────────────────────────
    /// <summary>
    /// Emitted once per physics tick with the current manifold state.
    /// Connect to HUDManifoldDisplay.OnManifoldDiagnostics() in the Inspector.
    ///
    ///   torsion     — player HAM TotalTorsion this tick
    ///   dominantAxis— dominant dichotomy axis name (string)
    ///   npcWeights  — per-NPC float[] attention rows (one row per NpcMind in scene)
    /// </summary>
    [Signal]
    public delegate void ManifoldDiagnosticsEventHandler(
        float   torsion,
        string  dominantAxis,
        float[] npcWeights);

    // ── NLCAS adapter ──────────────────────────────────────────────────────────
    /// <summary>
    /// Plug in a real INLCASAdapter here at integration time.
    /// Defaults to the no-op NullNLCASAdapter so the pipeline is always safe to run.
    /// </summary>
    public INLCASAdapter NLCASAdapter { get; set; } = NullNLCASAdapter.Instance;

    public override void _Ready()
    {
        _simplicial = GetNodeOrNull<SimplicialComplexNode>(SimplicialPath);
        _maxwell    = GetNodeOrNull<MaxwellEngine2Form>(MaxwellPath);
        _gr         = GetNodeOrNull<GRHarmonicEngine>(GrPath);
        _observer   = GetNodeOrNull<ObserverController>(ObserverPath);
        _magic      = GetNodeOrNull<MagicFieldEngine>(MagicPath);

        _knowledge = GetNodeOrNull<KnowledgeGraphNode>(KnowledgePath);
        _emotion   = GetNodeOrNull<EmotionFieldNode>(EmotionPath);
        _intent    = GetNodeOrNull<IntentFieldNode>(IntentPath);
        _action    = GetNodeOrNull<ActionLayerNode>(ActionPath);
        _coupler    = GetNodeOrNull<RelationshipCoupler>(CouplerPath);
        _regulator  = GetNodeOrNull<Regulator>(RegulatorPath);
        _playerH    = GetNodeOrNull<PlayerHElement>(PlayerHElementPath);

        // Wire NLCAS adapter — replace the null stub if the node exists in the tree.
        if (NLCASPath != null && !NLCASPath.IsEmpty)
        {
            var nlcasNode = GetNodeOrNull<NLCASNode>(NLCASPath);
            if (nlcasNode != null)
                NLCASAdapter = nlcasNode;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // 1. Observer action vector → ActionLayerNode + MagicFieldEngine action field
        _latestActionVector = _observer != null
            ? _observer.GetActionVector()
            : Vector3.Zero;

        if (_action != null && _latestActionVector != Vector3.Zero)
        {
            _action.Inputs.Add(new ActionEvent
            {
                Name      = "move",
                Value     = _latestActionVector.Length(),
                TimeStamp = Time.GetTicksMsec() / 1000.0
            });
        }

        // Notify regulator of player input — resets idle timer and suppresses flow-gate decay.
        if (_latestActionVector != Vector3.Zero)
            _regulator?.RegisterInput();

        if (_magic != null)
            _magic.InjectActionVector(_latestActionVector);

        // 2. EM → GR coupling: inject EM energy density as GR source
        if (_maxwell != null && _gr != null)
        {
            float[] emField = _maxwell.GetField();
            if (emField != null)
            {
                int nF = emField.Length;
                var emEnergy = new float[nF];
                for (int f = 0; f < nF; f++) emEnergy[f] = emField[f] * emField[f];
                _gr.AddSource(emEnergy, EmToGrStrength);
            }
        }

        // 3. GR → EM coupling: curvature modulates EM geometry weights
        if (_gr != null && _maxwell != null)
        {
            float[] grField = _gr.GetScalarField();
            if (grField != null)
                _maxwell.SetGeometryWeights(grField, GrToEmStrength);
        }

        // 4. GR → Cognitive: curvature drives intent urgency
        if (_gr != null && _magic != null)
        {
            float[] grField = _gr.GetScalarField();
            if (grField != null)
                _magic.InjectCurvature(grField);
        }

        // 5. EM → Cognitive: EM flux drives emotional arousal
        if (_maxwell != null && _magic != null)
        {
            float[] emField = _maxwell.GetField();
            if (emField != null)
                _magic.InjectEmFlux(emField);
        }

        // 6. ManifoldRegulator — single call replaces ApplyHAMFeedback + ApplyPlayerManifoldGate.
        //    Uses player HAM + player torsion to compute GRDepth/MagicDepth/CognitiveGain,
        //    then applies them to MagicFieldEngine.LambdaStep and the cog coupling scalars.
        //    EM↔GR bias (global/local vs collapse/progression) is handled inside Apply().
        if (ManifoldRegulation && _playerH != null && _magic != null)
        {
            _manifoldRegulator.Apply(_playerH.Attention, _playerH.TotalTorsion);

            // Push depth into the operator layers — GRHarmonicEngine and MagicFieldEngine
            // each hold their own Operator instance that scales output accordingly.
            _magic.SetDepth(_manifoldRegulator.MagicDepth);
            _gr?.SetDepth(_manifoldRegulator.GRDepth);

            // Cog coupling strengths also scale by GR depth
            GrToCogStrength = Mathf.Clamp(GrToCogStrength * _manifoldRegulator.GRDepth, 0.001f, 10f);
            EmToCogStrength = Mathf.Clamp(EmToCogStrength * _manifoldRegulator.GRDepth, 0.001f, 10f);

            // Cache diagnostics (backward-compat with UniverseRoot HUD)
            _lastPlayerTorsion = _playerH.TotalTorsion;
            _lastPlayerGate    = _manifoldRegulator.MagicDepth;
            _lastPlayerAxis    = _playerH.DominantAxis;
        }

        // 6b. NPC gestalt coupling + behaviour modulation.
        //     CoupleNpcToPlayer() injects player boundary conditions into K/E/I.
        //     ApplyBehaviourModulation() then shapes T_A and fine-tunes E/I from
        //     the NPC's own HAM attention weights + ManifoldRegulator.CognitiveGain.
        if (_playerH != null && _coupler != null)
        {
            var snapshot = _playerH.GetSnapshot();
            // Collect NPC list once — reused in step 14 signal emission
            var npcList = GetNpcMinds().ToList();
            _coupler.CoupleNpcToPlayer(in snapshot, npcList);
            _lastNpcList = npcList;

            // Behaviour modulation — runs after boundary injection so the NPC's
            // post-injection HAM is already fresh when we read attention weights.
            if (ManifoldRegulation)
            {
                var (di, _) = _playerH.Attention.DominantAxis();
                int axisIndex = di >= 0 ? di : DichotomyOperator.INTENT;
                foreach (var npc in npcList)
                    npc.ApplyBehaviourModulation(_manifoldRegulator.CognitiveGain, axisIndex);
            }
        }

        // 7. NLCAS positional encoding update — gather live per-vertex physics scalars
        //    and push into the NLCASNode before the forward pass.
        if (NLCASAdapter is NLCASNode nlcasNode && _simplicial != null)
        {
            var vertices  = _simplicial.Vertices.ToArray();
            var curvature = _gr?.GetScalarField()         ?? System.Array.Empty<float>();
            var flux      = _maxwell?.GetFluxAtVertices()    ?? System.Array.Empty<float>();
            var torsion   = _magic?.GetLocalTorsionArray()   ?? System.Array.Empty<float>();
            var dichotomy = _magic?.GetLocalDichotomyArray() ?? System.Array.Empty<float>();

            nlcasNode.UpdatePositionalEncoding(vertices, curvature, flux, torsion, dichotomy);
        }

        // 8. HAM torsion gate — scale NLCAS WriteBackAlpha so a high-torsion manifold
        //    allows more transformer write-through (closer to full WriteBackAlpha),
        //    while a near-zero torsion manifold damps it (nothing to write back when
        //    fields are already in rotational equilibrium).
        //    Formula:  effectiveAlpha = WriteBackAlpha * (|τ| / (1 + |τ|))
        //    At τ=0: effectiveAlpha→0 (no write-back).  At |τ|→∞: →WriteBackAlpha.
        //    Applied BEFORE PushCognitiveState so the forward pass uses the gated alpha.
        if (NLCASAdapter is NLCASNode nlcasGateNode && _magic != null)
        {
            float torsion   = _magic.TotalTorsion();
            float hamGate   = System.Math.Abs(torsion) / (1f + System.Math.Abs(torsion));   // ∈ [0, 1)
            float baseAlpha = nlcasGateNode.WriteBackAlpha;
            nlcasGateNode.WriteBackAlphaOverride = baseAlpha * hamGate;
            _lastHAMGate        = hamGate;
            _lastEffectiveAlpha = baseAlpha * hamGate;
        }

        // 9. NLCAS forward pass: push cognitive state snapshot → transformer.
        //    WriteBackAlphaOverride (set in step 8) is used instead of the static WriteBackAlpha.
        if (_knowledge != null && _emotion != null && _intent != null && _action != null)
        {
            NLCASAdapter.PushCognitiveState(
                _knowledge.Field,
                _magic?.GetEmotion() ?? System.Array.Empty<float>(),
                _magic?.GetIntent()  ?? System.Array.Empty<float>(),
                _magic?.GetAction()  ?? System.Array.Empty<float>()
            );
        }

        // 10. Pull NLCAS attention weights back and feed them into the coupler as a gate.
        //     This closes the recursive loop:
        //       transformer attention (2×2) → SetNLCASGate → CoupleOM4 Q·K scores → AttentionWeights → transformer input
        if (_coupler != null)
        {
            // Initialise a 2×2 gate matrix; PullAttentionWeights fills it with transformer output.
            var gate = new float[2, 2] { { 1f, 1f }, { 1f, 1f } };
            NLCASAdapter.PullAttentionWeights(gate);
            _coupler.SetNLCASGate(gate);
        }

        // 11. OM4 Q/K/V attention: CoupleOM4 over the raw MagicFieldEngine vertex arrays,
        //     now gated by the NLCAS attention matrix from step 10.
        //     Modifies the intent and action arrays in-place (O[0]→I, O[1]→A).
        if (_magic != null && _coupler != null)
        {
            float[]? kArr = _magic.GetKnowledge();
            float[]? eArr = _magic.GetEmotion();
            float[]? iArr = _magic.GetIntent();
            float[]? aArr = _magic.GetAction();
            if (kArr != null && eArr != null && iArr != null && aArr != null)
                _coupler.CoupleOM4(kArr, eArr, iArr, aArr);
        }

        // 12. Legacy node-level coupling: maintains scalar Arousal/Urgency/action-event feedback.
        _coupler?.Couple(_knowledge, _emotion, _intent, _action);

        // 13. Push MagicFieldEngine field means → OM4 node scalars so they reflect live state
        if (_magic != null)
        {
            if (_emotion != null)
                _emotion.AccumulateArousal(FieldMean(_magic.GetEmotion()) - _emotion.Arousal);

            if (_intent != null)
                _intent.AccumulateUrgency(FieldMean(_magic.GetIntent()) - _intent.Urgency);
        }

        // 14. Emit ManifoldDiagnostics signal — HUDManifoldDisplay receives this.
        //     Pack all per-NPC attention weights into a flat float[] so the signal
        //     stays a simple Variant-compatible type.
        //       layout: [npc0_w0, npc0_w1, ..., npc0_w3,  npc1_w0, ..., npcN_w3]
        //       stride: 4 floats per NPC (one weight per cognitive axis)
        EmitManifoldDiagnostics();
    }

    // ── diagnostics state — read by UniverseRoot HUD ─────────────────────────
    private float  _lastHAMGate         = 0f;
    private float  _lastEffectiveAlpha  = 0f;
    public  float  LastHAMGate          => _lastHAMGate;
    public  float  LastEffectiveAlpha   => _lastEffectiveAlpha;

    private float  _lastPlayerTorsion   = 0f;
    private float  _lastPlayerGate      = 1f;
    private string _lastPlayerAxis      = "n/a";
    public  float  LastPlayerTorsion    => _lastPlayerTorsion;
    public  float  LastPlayerGate       => _lastPlayerGate;
    public  string LastPlayerAxis       => _lastPlayerAxis;

    // Cached NPC list built in step 6b, reused in step 14 so we only iterate once
    private List<NpcMind> _lastNpcList  = new List<NpcMind>();

    /// <summary>
    /// Collect per-NPC attention-weight rows and emit the ManifoldDiagnostics signal.
    ///
    /// NPC weights are packed into a flat float[] with stride 4 (one weight per axis).
    /// If a given NpcMind has no AttentionWeights yet (first tick), four 0.25 placeholders
    /// are written (equal-distribution prior).
    /// </summary>
    private void EmitManifoldDiagnostics()
    {
        float  torsion = _playerH  != null ? _playerH.TotalTorsion  : 0f;
        string axis    = _playerH  != null ? _playerH.DominantAxis  : "n/a";

        // Pack NPC weights: 4 floats per NPC (HAM attention row for dominant axis)
        var weights = new float[_lastNpcList.Count * 4];
        for (int i = 0; i < _lastNpcList.Count; i++)
        {
            var npc = _lastNpcList[i];
            // Use the NPC's own HAM dominant-axis attention row if available
            var (di, _) = npc.Attention.DominantAxis();
            float[] row = di >= 0
                ? npc.Attention.AttentionRow(di)
                : new float[] { 0.25f, 0.25f, 0.25f, 0.25f };

            int offset = i * 4;
            for (int k = 0; k < 4 && k < row.Length; k++)
                weights[offset + k] = row[k];
        }

        EmitSignal(SignalName.ManifoldDiagnostics, torsion, axis, weights);
    }

    public void Start(double delta) => _PhysicsProcess(delta);

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Collapses a vertex field to a single scalar (mean).
    /// Used to map MagicFieldEngine float[] arrays to the scalar exports on OM4 nodes.
    /// Returns 0 for null or empty arrays.
    /// </summary>
    private static float FieldMean(float[]? field)
    {
        if (field == null || field.Length == 0) return 0f;
        float sum = 0f;
        foreach (float v in field) sum += v;
        return sum / field.Length;
    }

    /// <summary>
    /// Returns all NpcMind nodes currently in the scene tree.
    /// Uses GetTree().GetNodesInGroup("npc_mind") first; falls back to a full tree
    /// scan on the first call if the group is empty.
    ///
    /// To opt-in automatically, add NpcMind nodes to the "npc_mind" Godot group in
    /// the Inspector (Node tab → Groups).  Ungrouped nodes are still discovered by
    /// the fallback scan but at O(n) cost — fine for ≤ 100 NPCs per scene.
    /// </summary>
    private IEnumerable<NpcMind> GetNpcMinds()
    {
        var grouped = GetTree().GetNodesInGroup("npc_mind");
        if (grouped.Count > 0)
        {
            foreach (var node in grouped)
                if (node is NpcMind nm) yield return nm;
            yield break;
        }

        // Fallback: scan the whole tree
        foreach (var node in GetTree().GetNodesInGroup(""))
            if (node is NpcMind nm) yield return nm;
    }
}
