using Godot;

public partial class NmuResourceLoaderNode : Node
{
    private NmuResourceFormatLoader? _loader;

    public override void _EnterTree()
    {
        _loader = new NmuResourceFormatLoader();
        ResourceLoader.AddResourceFormatLoader(_loader);
    }

    public override void _ExitTree()
    {
        if (_loader != null)
            ResourceLoader.RemoveResourceFormatLoader(_loader);
        _loader = null;
    }
}
