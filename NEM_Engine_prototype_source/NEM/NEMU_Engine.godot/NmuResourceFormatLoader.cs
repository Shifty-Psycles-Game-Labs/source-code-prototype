using Godot;

public partial class NmuResourceFormatLoader : ResourceFormatLoader
{
    public override string[] _GetRecognizedExtensions() => new[] { "nmu", "nmd" };

    public override bool _HandlesType(StringName type)
    {
        return type == "Resource" || type == "NmuResource";
    }

    public override string _GetResourceType(string path)
    {
        return "NmuResource";
    }

    public override Variant _Load(string path, string originalPath, bool useSubThreads, int cacheMode)
    {
        try
        {
            var resource = new NmuResource
            {
                File = NmuParser.ParseFile(path)
            };
            return Variant.From(resource);
        }
        catch (NmuParseException exception)
        {
            GD.PushError($"NEMU: unable to parse '{path}': {exception.Message}");
            return default;
        }
        catch (System.Exception exception)
        {
            GD.PushError($"NEMU: unable to load '{path}': {exception.Message}");
            return default;
        }
    }
}
