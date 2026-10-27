using Opc.Ua;
using Opc.Ua.Server.Fluent;

namespace IndustrialOpcUa;

internal static class DemoModel
{
    public const string NamespaceUri = "urn:hnova:industrial:opcua:demo";

    public static void Configure(INodeManagerBuilder builder)
    {
        ushort ns = builder.DefaultNamespaceIndex;
        var folder = new FolderState(null)
        {
            NodeId = new NodeId("Demo", ns),
            BrowseName = new QualifiedName("Demo", ns),
            DisplayName = new LocalizedText("Demo"),
            TypeDefinitionId = ObjectTypeIds.FolderType
        };
        builder.Add(folder);
        builder.Add(Variable("Temperature", new Variant(23.5), DataTypeIds.Double, false, ns), folder.NodeId);
        builder.Add(Variable("Ready", new Variant(true), DataTypeIds.Boolean, false, ns), folder.NodeId);
        builder.Add(Variable("Setpoint", new Variant(25.0), DataTypeIds.Double, true, ns), folder.NodeId)
            .OnWrite(ValidateSetpoint);
    }

    private static BaseDataVariableState Variable(string name, Variant initial, NodeId dataType, bool writable, ushort ns)
    {
        return new BaseDataVariableState(null)
        {
            NodeId = new NodeId($"Demo.{name}", ns),
            BrowseName = new QualifiedName(name, ns),
            DisplayName = new LocalizedText(name),
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = writable ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead,
            UserAccessLevel = writable ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead,
            Value = initial,
            Timestamp = DateTime.UtcNow,
            StatusCode = StatusCodes.Good
        };
    }

    private static ServiceResult ValidateSetpoint(ISystemContext context, NodeState node, ref Variant value)
    {
        if (!value.TryGetValue(out double target)) return StatusCodes.BadTypeMismatch;
        return double.IsFinite(target) && target is >= 0 and <= 100
            ? ServiceResult.Good : StatusCodes.BadOutOfRange;
    }
}
