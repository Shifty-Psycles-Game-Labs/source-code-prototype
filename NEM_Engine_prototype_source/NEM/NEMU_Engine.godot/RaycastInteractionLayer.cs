using Godot;
using Godot.Collections;

/// <summary>
/// RaycastInteractionLayer — fires a ray from the player camera into the world
/// each physics frame (README §7534–7597).
///
/// When the ray hits a collider that:
///   • has a "node_id" meta (Area3D markers placed by WireframeFieldVisualizer), OR
///   • implements GetDebugInfo() via a script method
/// …it instantiates (or repositions) a floating Label3D above the hit point
/// showing node ID, torsion, and semantic tags.
///
/// The label disappears automatically when the ray stops hitting anything.
///
/// Scene layout:
///   PlayerController
///   └─ Camera3D
///      └─ RaycastInteractionLayer   ← attach this script here
///
/// Alternatively attach to any Node3D that has the camera as parent.
/// </summary>
public partial class RaycastInteractionLayer : Node3D
{
    [Export] public float RayLength = 50.0f;

    /// Optional: assign a PackedScene containing a Label3D.
    /// If left null the script creates a plain Label3D at runtime.
    [Export] public PackedScene LabelScene;

    private Node3D _currentLabel;

    public override void _PhysicsProcess(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null) return;

        var spaceState = GetWorld3D().DirectSpaceState;

        // Ray origin = camera position; direction = camera's -Z (forward).
        var from = camera.GlobalTransform.Origin;
        var to   = from + (-camera.GlobalTransform.Basis.Z * RayLength);

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas  = true;
        query.CollideWithBodies = true;

        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            var collider = result["collider"].As<Node>();
            var hitPos   = result["position"].As<Vector3>();

            var info = GatherDebugInfo(collider);
            ShowLabel(hitPos, info);
        }
        else
        {
            ClearLabel();
        }
    }

    // ── label management ───────────────────────────────────────────────────────

    private void ShowLabel(Vector3 position, Dictionary info)
    {
        if (_currentLabel == null)
        {
            _currentLabel = LabelScene != null
                ? LabelScene.Instantiate<Node3D>()
                : CreateDefaultLabel();

            GetTree().CurrentScene.AddChild(_currentLabel);
        }

        _currentLabel.GlobalPosition = position + Vector3.Up * 0.5f;

        // Support either a Label3D or a node with a SetText method.
        var text = string.Format("Node: {0}\nTorsion: {1}\nTags: {2}",
            info.ContainsKey("id")      ? info["id"].ToString()      : "unknown",
            info.ContainsKey("torsion") ? info["torsion"].ToString() : "n/a",
            info.ContainsKey("tags")    ? info["tags"].ToString()    : "none");

        if (_currentLabel is Label3D lbl)
            lbl.Text = text;
        else if (_currentLabel.HasMethod("SetText"))
            _currentLabel.Call("SetText", text);
    }

    private void ClearLabel()
    {
        if (_currentLabel != null)
        {
            _currentLabel.QueueFree();
            _currentLabel = null;
        }
    }

    /// <summary>Creates a minimal billboard Label3D when no LabelScene is assigned.</summary>
    private static Label3D CreateDefaultLabel()
    {
        return new Label3D
        {
            Billboard   = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize    = 32,
            Modulate    = new Color(0f, 1f, 1f),   // cyan
            OutlineSize = 4,
            PixelSize   = 0.005f,
        };
    }

    // ── debug info collection ──────────────────────────────────────────────────

    /// <summary>
    /// Tries multiple strategies to collect debug info from a collider:
    ///   1. GDScript method get_debug_info()
    ///   2. Meta keys "node_id" / "torsion" / "tags" (set by WireframeFieldVisualizer)
    ///   3. Fallback with just the node name.
    /// </summary>
    private static Dictionary GatherDebugInfo(Node collider)
    {
        // Strategy 1: GDScript / C# method
        if (collider != null && collider.HasMethod("get_debug_info"))
        {
            var raw = collider.Call("get_debug_info");
            if (raw.VariantType == Variant.Type.Dictionary)
                return raw.As<Dictionary>();
        }

        // Strategy 2: meta keys placed by WireframeFieldVisualizer area markers
        var dict = new Dictionary();
        if (collider != null)
        {
            dict["id"] = collider.HasMeta("node_id")
                ? collider.GetMeta("node_id").ToString()
                : collider.Name.ToString();

            dict["torsion"] = collider.HasMeta("torsion")
                ? collider.GetMeta("torsion").ToString()
                : "n/a";

            dict["tags"] = collider.HasMeta("tags")
                ? collider.GetMeta("tags").ToString()
                : "none";
        }
        return dict;
    }
}
