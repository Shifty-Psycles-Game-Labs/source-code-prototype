using System;
using System.Collections.Generic;

public sealed class NmuFile
{
    public NmuFile(NmuHeader header, IReadOnlyList<NmuBlock> blocks, IReadOnlyList<NmuExportDirective> exports)
    {
        Header = header;
        Blocks = blocks;
        Exports = exports;
    }

    public NmuHeader Header { get; }
    public IReadOnlyList<NmuBlock> Blocks { get; }
    public IReadOnlyList<NmuExportDirective> Exports { get; }
}

public sealed class NmuHeader
{
    public NmuHeader(IReadOnlyDictionary<string, string> attributes)
    {
        Attributes = attributes;
    }

    public IReadOnlyDictionary<string, string> Attributes { get; }
}

public abstract class NmuBlock
{
    protected NmuBlock(string name, IReadOnlyDictionary<string, string> attributes, string body)
    {
        Name = name;
        Attributes = attributes;
        Body = body;
    }

    public string Name { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public string Body { get; }
}

public sealed class NmuTierBlock : NmuBlock
{
    public NmuTierBlock(string name, IReadOnlyDictionary<string, string> attributes, string body)
        : base(name, attributes, body) { }
}

public sealed class NmuMathpearlBlock : NmuBlock
{
    public NmuMathpearlBlock(IReadOnlyDictionary<string, string> attributes, string body)
        : base("mathpearl", attributes, body) { }
}

public sealed class NmuPrivateBlock : NmuBlock
{
    public NmuPrivateBlock(IReadOnlyDictionary<string, string> attributes, string body)
        : base("private", attributes, body) { }
}

public sealed class NmuExportDirective
{
    public NmuExportDirective(string target)
    {
        Target = target;
    }

    public string Target { get; }
}

public sealed class NmuParseException : Exception
{
    public NmuParseException(string message) : base(message) { }
}
