using Godot;

public partial class FieldProbe : Node3D
{
    public override void _Process(double delta)
    {
        var engine = GetNode<GREngine>("../GREngine");
        var coupler = GetNode<FieldCoupler>("../FieldCoupler");

        // Move randomly
        GlobalPosition += new Vector3(
            (float)(GD.Randf() - 0.5),
            (float)(GD.Randf() - 0.5),
            (float)(GD.Randf() - 0.5)
        );

        // Find nearest vertex
        int nearest = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < engine.Vertices.Count; i++)
        {
            float d = GlobalPosition.DistanceTo(engine.Vertices[i]);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = i;
            }
        }

        float value = coupler.Field[nearest];

        // Visualize field value by color
        Modulate = new Color(1f, 1f - Mathf.Abs(value), 1f - Mathf.Abs(value));
    }
}
