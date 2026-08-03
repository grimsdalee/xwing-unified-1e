namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicCommonArtworkTextureResult
{
    public string ImplementationVersion { get; set; } = "15C-R2";
    public string TemplatePath { get; set; } = string.Empty;
    public string MountPointDatabasePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string ReportPath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int StarCount { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
