using UnifiedToolkit.KnowledgeBase.PilotAssetLinking;

namespace UnifiedToolkit.KnowledgeBase;

public sealed class PilotAssetLinker
{
    public PilotAssetLinkResult Link(string repositoryRoot, string? pilotsFile = null, string? outputFolder = null, int candidatesPerRole = 8)
    {
        var options = PilotAssetLinkingOptions.Create(repositoryRoot, pilotsFile, outputFolder, candidatesPerRole);
        return new PilotAssetLinkingService().Link(options);
    }
}
