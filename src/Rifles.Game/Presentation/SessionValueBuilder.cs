using System.Text;
using Rusty.Engine;

namespace Rifles.Game.Presentation;

internal sealed class SessionValueBuilder
{
    private readonly List<StructuredValueNode> _nodes = [];
    private readonly List<uint> _edges = [];
    private readonly List<byte> _utf8 = [];

    internal uint Number(double value) => Add(StructuredValueKind.Number, numberValue: value);

    internal uint String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        (uint offset, uint length) = Bytes(value);
        return Add(StructuredValueKind.String, textOffset: offset, textLength: length);
    }

    internal uint Object(params (string Key, uint Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        uint firstEdge = checked((uint)_edges.Count);
        foreach ((string key, uint value) in fields)
        {
            if (value >= (uint)_nodes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(fields));
            }

            (uint offset, uint length) = Bytes(key);
            uint keyedValue = checked((uint)_nodes.Count);
            _nodes.Add(_nodes[checked((int)value)] with { KeyOffset = offset, KeyLen = length });
            _edges.Add(keyedValue);
        }

        return Add(StructuredValueKind.Object, firstEdge: firstEdge, childCount: checked((uint)fields.Length));
    }

    internal UiValue Build(uint root)
    {
        if (root >= (uint)_nodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(root));
        }

        return new UiValue(_nodes.ToArray(), _edges.ToArray(), root, _utf8.ToArray());
    }

    private uint Add(
        StructuredValueKind kind,
        double numberValue = 0,
        uint textOffset = 0,
        uint textLength = 0,
        uint firstEdge = 0,
        uint childCount = 0)
    {
        uint index = checked((uint)_nodes.Count);
        _nodes.Add(new StructuredValueNode(kind, 0, numberValue, 0, 0, textOffset, textLength, firstEdge, childCount));
        return index;
    }

    private (uint Offset, uint Length) Bytes(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        uint offset = checked((uint)_utf8.Count);
        _utf8.AddRange(bytes);
        return (offset, checked((uint)bytes.Length));
    }
}
