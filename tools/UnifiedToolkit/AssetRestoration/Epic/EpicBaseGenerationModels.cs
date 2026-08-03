namespace UnifiedToolkit.AssetRestoration.Epic;

public sealed class EpicBaseCalibrationTextureResult
{
    public string TemplatePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class EpicBaseValidationSaveManifest
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public DateTimeOffset GeneratedUtc { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string ReferenceSave { get; set; } = string.Empty;
    public string BaseTemplate { get; set; } = string.Empty;
    public string CalibrationTexture { get; set; } = string.Empty;
    public string BaseMesh { get; set; } = string.Empty;
    public string AssetBaseUrl { get; set; } = string.Empty;
    public string SavePath { get; set; } = string.Empty;
    public int ObjectCount { get; set; }
}
