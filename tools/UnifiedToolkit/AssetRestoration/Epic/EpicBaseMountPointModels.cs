namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicBaseMountPointDatabase
{
    public string SchemaVersion { get; set; } = "1.1.0";
    public string ImplementationVersion { get; set; } = "15B-R5";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string Status { get; set; } =
        "DerivedFromSpawnedRuntimeAssemblyAndPegGeometry";
    public string SourceSave { get; set; } = string.Empty;
    public string SourceBaseGuid { get; set; } = string.Empty;
    public string BaseMesh { get; set; } = string.Empty;
    public string PegMesh { get; set; } = string.Empty;
    public EpicMountPointProjection Projection { get; set; } = new();
    public List<EpicBaseMountPoint> MountPoints { get; set; } = new();
    public List<string> ValidationWarnings { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class EpicMountPointProjection
{
    public string Method { get; set; } =
        "PegShaftAxisCentroidThenExactBarycentricProjection";
    public double PointInTriangleTolerance { get; set; } = 0.000001;
    public string PegAxisMethod { get; set; } =
        "CentroidOfHighestHorizontalVertexRing";
    public double HighestRingYTolerance { get; set; } = 0.0005;
    public int BaseTrianglesExamined { get; set; }
    public int PegConnectedComponents { get; set; }
}

public sealed class EpicBaseMountPoint
{
    public string Id { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string SourceRuntimeKey { get; set; } = string.Empty;

    public EpicLocalMountPosition RuntimeLocalPosition { get; set; } = new();
    public EpicLocalMountPosition PegGeometryLocalPosition { get; set; } = new();
    public EpicLocalMountPosition GeometryMinusRuntime { get; set; } = new();

    public EpicTokenOptionalPoint TextureUv { get; set; } = new();
    public EpicBarycentricProjectionDetails ProjectionDetails { get; set; } =
        new();
}

public sealed class EpicLocalMountPosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

public sealed class EpicBarycentricProjectionDetails
{
    public int BaseFaceIndex { get; set; }
    public double TriangleAverageY { get; set; }
    public EpicBarycentricWeights Weights { get; set; } = new();
    public List<EpicLocalMountPosition> TriangleVertices { get; set; } = new();
    public List<EpicTokenOptionalPoint> TriangleUvs { get; set; } = new();
}

public sealed class EpicBarycentricWeights
{
    public double A { get; set; }
    public double B { get; set; }
    public double C { get; set; }
}

public sealed class EpicBaseMountPointBuildResult
{
    public EpicBaseMountPointDatabase Database { get; set; } = new();
    public string OutputPath { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
}
