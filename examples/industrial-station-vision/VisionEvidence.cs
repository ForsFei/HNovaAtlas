using IndustrialStation;
using IndustrialVision;
namespace IndustrialStationVision;
// Immutable managed pixels; shares original owned image, never a camera lease or mutable byte buffer.
public sealed record VisionEvidence(StationTerminal Terminal, Guid FrameId, OwnedFrame Frame,
    InspectionResult Inspection, VisionRecipe Recipe);
