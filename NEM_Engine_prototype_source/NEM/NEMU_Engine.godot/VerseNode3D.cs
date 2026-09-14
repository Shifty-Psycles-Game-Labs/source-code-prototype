using Godot;

/// <summary>
/// VerseNode3D — root operator coupler for the NEM-U layered manifold.
///
/// Formally:  V(t) = Ω( {L_i(t)}, λ(t) )
///
/// Instantiates each operator layer as an independent PackedScene child, mirroring
/// the mathematical structure:
///   SceneTree = V ⊕ ⊕ᵢ Lᵢ
///
/// Layers are loaded from res://res/scenes/Layers/.  Missing layer paths emit a
/// warning but do not abort startup — the engine degrades gracefully.
///
/// City generation:
///   When SpawnCity is true (default), a TheCityNode is instantiated at runtime
///   and added as a child named "TheCityNode" before the player is positioned.
///   Use PlayerPath to point at the CharacterBody3D that should be placed inside
///   the city at spawn.  All city parameters are Inspector-overridable.
/// </summary>
public partial class VerseNode3D : Node3D
{
    // ── layer paths — override in Inspector or leave as defaults ──────────────
    [Export] public string EMFieldLayerPath     = "res://res/scenes/Layers/EMFieldLayer.tscn";
    [Export] public string CognitiveLayerPath   = "res://res/scenes/Layers/CognitiveLayer.tscn";
    [Export] public string MagicLayerPath       = "res://res/scenes/Layers/MagicLayer.tscn";
    [Export] public string EngineLayerPath      = "res://res/scenes/Layers/EngineLayer.tscn";
    [Export] public string NPCMindLayerPath     = "res://res/scenes/Layers/NPCMindLayer.tscn";
    [Export] public string PlayerLayerPath      = "res://res/scenes/Layers/PlayerLayer.tscn";
    [Export] public string FloorLayerPath       = "res://res/scenes/Layers/FloorGeometryLayer.tscn";
    [Export] public string HUDLayerPath         = "res://res/scenes/Layers/HUDLayer.tscn";

    /// <summary>
    /// When true, each layer is loaded synchronously in _Ready().
    /// Set false and call LoadLayers() manually for deferred / async loading.
    /// </summary>
    [Export] public bool AutoLoadLayers = true;

    // ── city parameters ───────────────────────────────────────────────────────

    /// <summary>When true, a TheCityNode is spawned and added as a child.</summary>
    [Export] public bool SpawnCity = true;

    /// <summary>Optional: load TheCityNode from a .tscn instead of C# new().</summary>
    [Export] public PackedScene CityScene;

    /// <summary>Number of buildings per axis.</summary>
    [Export] public int   CityGridSize    = 20;

    /// <summary>World-space distance between building centres (metres).</summary>
    [Export] public float CitySpacing     = 4.0f;

    /// <summary>Peak building height at the Gaussian centre (metres).</summary>
    [Export] public float CityHeightScale = 20.0f;

    /// <summary>
    /// NodePath of the CharacterBody3D to reposition above the city floor.
    /// Leave empty to skip player positioning.
    /// </summary>
    [Export] public NodePath PlayerPath;

    // ── internal refs ──────────────────────────────────────────────────────────
    private TheCityNode _city;

    public override void _Ready()
    {
        if (AutoLoadLayers)
            LoadLayers();

        if (SpawnCity)
        {
            _city = SpawnCityNode();
            AddChild(_city);
            PositionPlayerInCity();
        }
    }

    /// <summary>
    /// Instantiates all operator layers and attaches them as children.
    /// Order matters: physics layers before cognitive, cognitive before engine coupler.
    /// </summary>
    public void LoadLayers()
    {
        // Physical field layers
        LoadLayer(EMFieldLayerPath,   "EMFieldLayer");

        // Cognitive / OM4 layers
        LoadLayer(CognitiveLayerPath, "CognitiveLayer");

        // Metaphysical / OM4 magic operator
        LoadLayer(MagicLayerPath,     "MagicLayer");

        // Unified operator coupler — must come after field layers are in the tree
        LoadLayer(EngineLayerPath,    "EngineLayer");

        // Entity layers
        LoadLayer(NPCMindLayerPath,   "NPCMindLayer");
        LoadLayer(PlayerLayerPath,    "PlayerLayer");

        // Geometry layer
        LoadLayer(FloorLayerPath,     "FloorGeometryLayer");

        // HUD — loaded last so it can resolve paths to engine nodes
        LoadLayer(HUDLayerPath,       "HUDLayer");
    }

    /// <summary>
    /// Loads a single layer scene and adds it as a named child.
    /// Emits a warning (non-fatal) if the scene cannot be loaded.
    /// </summary>
    private void LoadLayer(string path, string childName)
    {
        var scene = GD.Load<PackedScene>(path);
        if (scene == null)
        {
            GD.PushWarning($"VerseNode3D: layer scene not found at '{path}'. Skipping.");
            return;
        }
        var instance = scene.Instantiate();
        instance.Name = childName;
        AddChild(instance);
    }

    // ── city helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the TheCityNode either from a PackedScene (if CityScene is set)
    /// or via direct C# instantiation with the exported parameters.
    /// </summary>
    private TheCityNode SpawnCityNode()
    {
        if (CityScene != null)
        {
            var node = CityScene.Instantiate<TheCityNode>();
            node.Name = "TheCityNode";
            return node;
        }

        return new TheCityNode
        {
            Name        = "TheCityNode",
            GridSize    = CityGridSize,
            Spacing     = CitySpacing,
            HeightScale = CityHeightScale,
        };
    }

    /// <summary>
    /// Moves the player to a safe spawn point above the city floor centre.
    /// Spawn Y = 2 m — clears the street level for any reasonable city scale.
    /// Does nothing (with a warning) if PlayerPath is empty or resolves to null.
    /// </summary>
    private void PositionPlayerInCity()
    {
        if (PlayerPath == null || PlayerPath.IsEmpty)
            return;

        var player = GetNodeOrNull<CharacterBody3D>(PlayerPath);
        if (player == null)
        {
            GD.PushWarning($"VerseNode3D: PlayerPath '{PlayerPath}' did not resolve to a CharacterBody3D. Skipping player spawn.");
            return;
        }

        player.GlobalPosition = new Vector3(0f, 2f, 0f);
    }
}
