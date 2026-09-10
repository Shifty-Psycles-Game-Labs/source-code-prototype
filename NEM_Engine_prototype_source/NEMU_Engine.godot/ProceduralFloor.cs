using Godot;

/// <summary>
/// ProceduralFloor — spawns a flat StaticBody3D floor and auto-aligns it
/// below the lowest vertex of the simplicial complex.
/// Also repositions the PlayerController above the wireframe so the player
/// spawns standing on the floor rather than falling through it.
///
/// Collision guarantees (Godot 4):
///   • Floor StaticBody3D is placed on collision layer 1, mask 1.
///   • BoxShape3D.Size is the FULL extent (width × height × depth).
///     Size.Y = FloorThickness.  The collider is NOT offset — it sits centred
///     on floor.Position.Y, so the top surface is at floorY + FloorThickness/2.
///   • Player spawn Y is set to (top-of-floor + capsule half-height + margin)
///     so the capsule bottom clears the floor surface at frame 0.
///
/// Exports:
///   ComplexPath    — path to SimplicialComplexNode (default: sibling "SimplicialComplex")
///   PlayerPath     — path to PlayerController      (default: sibling "PlayerController")
///   FloorSize      — XZ footprint of the floor plane
///   FloorThickness — Y extent of the collision box (default: 0.4 — generous to avoid tunnelling)
///   FloorMargin    — gap between lowest wireframe node and floor top face (default: 1.0)
///   GeometryScale  — must match WireframeFieldVisualizer.GeometryScale (default: 3.0)
///   PlayerCapsuleHalfHeight — half-height of PlayerController's CapsuleShape3D (default: 0.9)
/// </summary>
public partial class ProceduralFloor : Node3D
{
    [Export] public NodePath ComplexPath             = "../SimplicialComplex";
    [Export] public NodePath PlayerPath              = "../PlayerController";
    [Export] public Vector2  FloorSize               = new Vector2(50f, 50f);
    [Export] public float    FloorThickness          = 0.4f;
    [Export] public float    FloorMargin             = 1.0f;
    [Export] public float    GeometryScale           = 3.0f;
    [Export] public float    PlayerCapsuleHalfHeight = 0.9f;   // matches CollisionShape3D offset in scene

    public override void _Ready()
    {
        // ── build the floor body ───────────────────────────────────────────────
        var floor = new StaticBody3D { Name = "Floor" };

        // Explicit collision layer 1 and mask 1 — player must be on the same mask.
        floor.CollisionLayer = 1;
        floor.CollisionMask  = 1;

        AddChild(floor);

        // Visual mesh — PlaneMesh is centred on Y=0 of the parent node.
        var planeMesh = new PlaneMesh { Size = FloorSize };
        floor.AddChild(new MeshInstance3D { Mesh = planeMesh, Name = "FloorMesh" });

        // Collision box — Size is FULL extent in Godot 4.
        // No position offset: the box is centred on floor.Position.Y, so its
        // top face is at floorY + FloorThickness/2.
        // We account for this half-offset when computing the player spawn Y.
        var shape    = new BoxShape3D { Size = new Vector3(FloorSize.X, FloorThickness, FloorSize.Y) };
        var collider = new CollisionShape3D { Shape = shape, Name = "FloorCollider" };
        floor.AddChild(collider);

        // ── find the lowest world-space vertex ────────────────────────────────
        float minY = FindMinY();

        // Floor body centre sits FloorMargin below the lowest wireframe vertex.
        // Top face of the collision box is therefore at:
        //   floorY + FloorThickness/2
        float floorY    = minY - FloorMargin;
        float floorTop  = floorY + FloorThickness * 0.5f;

        // Player spawn: capsule bottom (origin − half-height) must clear floorTop.
        // A small extra gap (0.1 m) avoids the capsule starting exactly at the surface.
        float spawnY = floorTop + PlayerCapsuleHalfHeight + 0.1f;

        floor.Position = new Vector3(0f, floorY, 0f);
        GD.Print($"[ProceduralFloor] minY={minY:F3}  floorCentre={floorY:F3}  floorTop={floorTop:F3}  playerSpawn={spawnY:F3}");

        // ── reposition the player above the floor ─────────────────────────────
        var player = GetNodeOrNull<CharacterBody3D>(PlayerPath);
        if (player != null)
        {
            var pos  = player.Position;
            pos.Y    = spawnY;
            player.Position = pos;
        }
        else
        {
            GD.PushWarning($"[ProceduralFloor] PlayerController not found at '{PlayerPath}'.");
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the minimum Y of all scaled simplex vertices, or 0 if the complex
    /// is unavailable (so the floor still appears at a sensible position).
    /// </summary>
    private float FindMinY()
    {
        var complex = GetNodeOrNull<SimplicialComplexNode>(ComplexPath);
        if (complex == null || complex.VertexCount == 0)
        {
            GD.PushWarning($"[ProceduralFloor] SimplicialComplexNode not found at '{ComplexPath}' — floor placed at Y=0.");
            return 0f;
        }

        float min = float.MaxValue;
        foreach (var v in complex.Vertices)
        {
            float worldY = v.Y * GeometryScale;
            if (worldY < min) min = worldY;
        }
        return min;
    }
}
