using Godot;

[GlobalClass]
public partial class NmuResource : Resource
{
    public NmuFile? File { get; internal set; }
}
