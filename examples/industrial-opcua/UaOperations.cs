using Opc.Ua;
using Opc.Ua.Client;

namespace IndustrialOpcUa;

internal static class UaOperations
{
    public static NodeId Resolve(ManagedSession session, string name)
    {
        int index = session.NamespaceUris.GetIndex(DemoModel.NamespaceUri);
        if (index < 0) throw new InvalidOperationException("Server does not expose the contracted namespace URI.");
        return new NodeId($"Demo.{name}", checked((ushort)index));
    }

    public static double RequireGoodDouble(DataValue value)
    {
        if (!StatusCode.IsGood(value.StatusCode))
            throw new InvalidDataException($"Unusable quality: {value.StatusCode}");
        if (!value.WrappedValue.TryGetValue(out double number) || !double.IsFinite(number))
            throw new InvalidDataException("Expected a finite UA Double, not a conversion from another type.");
        return number;
    }

    // One write attempt. Good is a UA write result, NOT a machine-completed acknowledgement.
    public static async Task<StatusCode> WriteAsync(ManagedSession session, NodeId node, Variant value, CancellationToken ct)
    {
        WriteResponse response = await session.WriteAsync(null, [new WriteValue
        {
            NodeId = node,
            AttributeId = Attributes.Value,
            Value = new DataValue(value)
        }], ct);
        if (response.Results.Count != 1) throw new InvalidDataException("Unexpected write result count.");
        return response.Results[0];
    }
}
