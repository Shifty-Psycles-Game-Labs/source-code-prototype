using Godot;

public partial class CityVerseNode : Node
{
    public override void _Ready()
    {
        var res = ResourceLoader.Load("res://assets/test.nmu");
        GD.Print(res);
    }
}
