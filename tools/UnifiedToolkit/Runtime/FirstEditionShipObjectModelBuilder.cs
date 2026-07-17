namespace UnifiedToolkit.Runtime;

public static class FirstEditionShipObjectModelBuilder
{
    public static FirstEditionShipObjectModelDocument Build(
        string hybridDefinitionsPath,
        string constructionRecipesPath,
        string targetShip)
    {
        var recipeDocument = FirstEditionShipRecipeBuilder.Build(
            hybridDefinitionsPath,
            constructionRecipesPath,
            targetShip);

        var document = new FirstEditionShipObjectModelDocument
        {
            HybridDefinitionsPath = Path.GetFullPath(hybridDefinitionsPath),
            ConstructionRecipesPath = Path.GetFullPath(constructionRecipesPath),
            TargetShip = targetShip
        };

        if (recipeDocument.Recipe is null)
        {
            document.ValidationErrors.Add($"No First Edition construction recipe was produced for '{targetShip}'.");
            FinalizeSummary(document);
            return document;
        }

        var recipe = recipeDocument.Recipe;
        if (!TryParseBaseSize(recipe.FirstEditionBaseSize, out var baseSize))
        {
            document.ValidationErrors.Add($"First Edition base size '{recipe.FirstEditionBaseSize}' cannot be represented by the strongly typed model.");
            baseSize = FirstEditionBaseSize.Small;
        }

        var model = new FirstEditionShipObjectModel
        {
            ShipId = recipe.ShipId,
            ShipName = recipe.ShipName,
            PilotId = recipe.SelectedPilotId,
            PilotName = recipe.SelectedPilotName,
            Factions = recipe.Factions.ToList()
        };

        model.Base = BuildBase(recipe, baseSize, document);
        model.Peg = BuildPeg(recipe, model.Base, document);
        model.ShipModel = BuildShipModel(recipe, document);
        model.Identifier = BuildIdentifier(model.Base, recipe, document);
        model.PilotDial = BuildPilotDial(recipe, document);

        document.ObjectModel = model;
        document.ValidationErrors.AddRange(recipe.ValidationErrors.Select(x => "Recipe: " + x));
        document.ReviewNotes.Add("This is an in-memory component model only; no Tabletop Simulator JSON has been generated.");
        document.ReviewNotes.Add("Review the audit CSV from top to bottom. Every base-size stage must remain Small for the T-65 X-Wing.");
        document.ReviewNotes.Add("The next phase may serialize this object model only after all five components pass validation.");
        FinalizeSummary(document);
        return document;
    }

    private static FirstEditionBaseComponent BuildBase(
        FirstEditionShipRecipe recipe,
        FirstEditionBaseSize size,
        FirstEditionShipObjectModelDocument document)
    {
        Audit(document, "Semantic recipe", "FirstEditionBaseSize", recipe.FirstEditionBaseSize,
            "Small, Large, or Epic", IsAllowedText(recipe.FirstEditionBaseSize),
            "FirstEditionShipRecipe.FirstEditionBaseSize",
            "This is the authoritative First Edition base value.");

        Audit(document, "Hybrid conversion", "Source25BaseSize", recipe.Source25BaseSize,
            "Informational only", true,
            "FirstEditionShipRecipe.Source25BaseSize",
            "A 2.5 source value must never override the First Edition base value.");

        Audit(document, "Hybrid conversion", "MediumRemoved", recipe.MediumRemoved.ToString(),
            "True when a 2.5 medium base was converted; otherwise False", true,
            "FirstEditionShipRecipe.MediumRemoved", "Conversion metadata only.");

        var runtimeSize = size switch
        {
            FirstEditionBaseSize.Small => "small",
            FirstEditionBaseSize.Large => "large",
            FirstEditionBaseSize.Epic => "huge",
            _ => ""
        };
        var expectedMesh = $"assets/ships-v2/bases/{runtimeSize}/base.obj";

        Audit(document, "Object model", "Base.Size", size.ToString(),
            "Small, Large, or Epic", true,
            "FirstEditionBaseComponent.Size", "Strongly typed: Medium is not representable.");
        Audit(document, "Runtime translation", "RuntimeSize", runtimeSize,
            size == FirstEditionBaseSize.Epic ? "huge" : size.ToString().ToLowerInvariant(),
            runtimeSize == (size == FirstEditionBaseSize.Epic ? "huge" : size.ToString().ToLowerInvariant()),
            "FirstEditionBaseComponent.RuntimeSize", "Used to choose runtime base assets.");
        Audit(document, "Runtime translation", "BaseMeshPath", recipe.RuntimeParameters.BaseMeshPath,
            expectedMesh, recipe.RuntimeParameters.BaseMeshPath.Equals(expectedMesh, StringComparison.OrdinalIgnoreCase),
            "FirstEditionRuntimeParameters.BaseMeshPath", "Must agree with the strongly typed base size.");

        var valid = IsAllowedText(recipe.FirstEditionBaseSize) &&
                    !runtimeSize.Equals("medium", StringComparison.OrdinalIgnoreCase) &&
                    recipe.RuntimeParameters.BaseMeshPath.Equals(expectedMesh, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(recipe.RuntimeParameters.BasePrototypeGuid);

        if (!valid)
            document.ValidationErrors.Add("Base component validation failed. Review the base-size audit trail.");

        return new FirstEditionBaseComponent
        {
            Size = size,
            RuntimeSize = runtimeSize,
            PrototypeSymbol = recipe.RuntimeParameters.BasePrototypeSymbol,
            PrototypeGuid = recipe.RuntimeParameters.BasePrototypeGuid,
            MeshPath = recipe.RuntimeParameters.BaseMeshPath,
            TexturePattern = recipe.RuntimeParameters.BaseTexturePattern,
            Source25BaseSize = recipe.Source25BaseSize,
            ConversionRequired = recipe.BaseConversionRequired,
            MediumRemoved = recipe.MediumRemoved,
            IsValid = valid
        };
    }

    private static FirstEditionPegComponent BuildPeg(
        FirstEditionShipRecipe recipe,
        FirstEditionBaseComponent baseComponent,
        FirstEditionShipObjectModelDocument document)
    {
        var expectedType = baseComponent.Size switch
        {
            FirstEditionBaseSize.Small => "small",
            FirstEditionBaseSize.Large => "large",
            FirstEditionBaseSize.Epic => "huge",
            _ => ""
        };
        var expectedMesh = $"assets/ships-v2/bases/pegs/{expectedType}.obj";
        var valid = recipe.RuntimeParameters.DefaultPegType.Equals(expectedType, StringComparison.OrdinalIgnoreCase) &&
                    recipe.RuntimeParameters.PegMeshPath.Equals(expectedMesh, StringComparison.OrdinalIgnoreCase);

        Audit(document, "Peg builder", "PegType", recipe.RuntimeParameters.DefaultPegType,
            expectedType, recipe.RuntimeParameters.DefaultPegType.Equals(expectedType, StringComparison.OrdinalIgnoreCase),
            "FirstEditionRuntimeParameters.DefaultPegType", "Derived only from the validated First Edition base size.");
        Audit(document, "Peg builder", "PegMeshPath", recipe.RuntimeParameters.PegMeshPath,
            expectedMesh, recipe.RuntimeParameters.PegMeshPath.Equals(expectedMesh, StringComparison.OrdinalIgnoreCase),
            "FirstEditionRuntimeParameters.PegMeshPath", "Must match the selected peg type.");

        if (!valid) document.ValidationErrors.Add("Peg component validation failed.");
        return new FirstEditionPegComponent
        {
            PegType = recipe.RuntimeParameters.DefaultPegType,
            MeshPath = recipe.RuntimeParameters.PegMeshPath,
            IsValid = valid
        };
    }

    private static FirstEditionShipModelComponent BuildShipModel(
        FirstEditionShipRecipe recipe,
        FirstEditionShipObjectModelDocument document)
    {
        var appearance = recipe.SelectedAppearance;
        var valid = appearance is not null && appearance.HasMesh && appearance.HasDiffuse;
        Audit(document, "Ship model builder", "SelectedAppearance", appearance?.DisplayName ?? "",
            "A variant with mesh and diffuse URLs", valid,
            "FirstEditionShipRecipe.SelectedAppearance", "Colour variants remain independent of base size.");
        Audit(document, "Ship model builder", "MeshUrl", appearance?.MeshUrl ?? "",
            "Non-empty", !string.IsNullOrWhiteSpace(appearance?.MeshUrl),
            "FirstEditionRecipeAppearance.MeshUrl", "The ship model is a separate Custom_Model object.");
        Audit(document, "Ship model builder", "DiffuseUrl", appearance?.DiffuseUrl ?? "",
            "Non-empty", !string.IsNullOrWhiteSpace(appearance?.DiffuseUrl),
            "FirstEditionRecipeAppearance.DiffuseUrl", "The colour variant texture is selected separately from the mesh.");

        if (!valid) document.ValidationErrors.Add("Ship model component validation failed.");
        return new FirstEditionShipModelComponent
        {
            AppearanceVariantId = appearance?.VariantId ?? "",
            AppearanceName = appearance?.DisplayName ?? "",
            SourceGuid = appearance?.SourceGuid ?? "",
            MeshUrl = appearance?.MeshUrl ?? "",
            DiffuseUrl = appearance?.DiffuseUrl ?? "",
            NormalUrl = appearance?.NormalUrl ?? "",
            ColliderUrl = appearance?.ColliderUrl ?? "",
            IsValid = valid
        };
    }

    private static FirstEditionIdentifierComponent BuildIdentifier(
        FirstEditionBaseComponent baseComponent,
        FirstEditionShipRecipe recipe,
        FirstEditionShipObjectModelDocument document)
    {
        var functionsPresent = recipe.RequiredRuntimeFunctions.Contains("shipIdCustomObjectForSize") &&
                               recipe.RequiredRuntimeFunctions.Contains("spawnShipIdentifiersAndConfig");
        var valid = baseComponent.IsValid && functionsPresent;
        Audit(document, "Identifier builder", "BaseSize", baseComponent.Size.ToString(),
            "Validated First Edition base size", baseComponent.IsValid,
            "FirstEditionBaseComponent.Size", "Identifier geometry must use the same base size.");
        Audit(document, "Identifier builder", "RuntimeFunctions", "shipIdCustomObjectForSize; spawnShipIdentifiersAndConfig",
            "Both functions available", functionsPresent,
            "FirstEditionShipRecipe.RequiredRuntimeFunctions", "Required for colour identifiers and configuration attachments.");
        if (!valid) document.ValidationErrors.Add("Identifier component validation failed.");
        return new FirstEditionIdentifierComponent
        {
            BaseSize = baseComponent.Size.ToString(),
            IsValid = valid
        };
    }

    private static FirstEditionPilotDialComponent BuildPilotDial(
        FirstEditionShipRecipe recipe,
        FirstEditionShipObjectModelDocument document)
    {
        var hasPilot = !string.IsNullOrWhiteSpace(recipe.SelectedPilotId);
        var hasDial = recipe.EditionAssets.Dials.Count > 0;
        var functionPresent = recipe.RequiredRuntimeFunctions.Contains("spawnPilotCardAndDial");
        var valid = hasPilot && hasDial && functionPresent;
        Audit(document, "Pilot/dial builder", "Pilot", recipe.SelectedPilotName,
            "Selected First Edition pilot", hasPilot,
            "FirstEditionShipRecipe.SelectedPilotName", "Pilot selection is deterministic for review.");
        Audit(document, "Pilot/dial builder", "DialAssetCount", recipe.EditionAssets.Dials.Count.ToString(),
            "At least 1", hasDial,
            "FirstEditionEditionAssets.Dials", "T-70 references must not appear in the T-65 result.");
        Audit(document, "Pilot/dial builder", "RuntimeFunction", "spawnPilotCardAndDial",
            "Available", functionPresent,
            "FirstEditionShipRecipe.RequiredRuntimeFunctions", "Card and dial construction remains separate from the physical ship.");
        if (!valid) document.ValidationErrors.Add("Pilot/dial component validation failed.");
        return new FirstEditionPilotDialComponent
        {
            PilotId = recipe.SelectedPilotId,
            PilotName = recipe.SelectedPilotName,
            DialAssets = recipe.EditionAssets.Dials.ToList(),
            PilotCardAssets = recipe.EditionAssets.Cards.ToList(),
            ShipReferenceAssets = recipe.EditionAssets.ShipReferences.ToList(),
            PhysicalBaseTokenAssets = recipe.EditionAssets.PhysicalBaseTokens.ToList(),
            IsValid = valid
        };
    }

    private static void FinalizeSummary(FirstEditionShipObjectModelDocument document)
    {
        var model = document.ObjectModel;
        document.Summary = new FirstEditionShipObjectModelSummary
        {
            RecipeAvailable = model is not null,
            BaseComponentValid = model?.Base.IsValid == true,
            PegComponentValid = model?.Peg.IsValid == true,
            ShipModelComponentValid = model?.ShipModel.IsValid == true,
            IdentifierComponentValid = model?.Identifier.IsValid == true,
            PilotDialComponentValid = model?.PilotDial.IsValid == true,
            MediumRejected = model is null || !model.Base.RuntimeSize.Equals("medium", StringComparison.OrdinalIgnoreCase),
            AuditEntryCount = document.AuditTrail.Count,
            ErrorCount = document.ValidationErrors.Count
        };
        document.Summary.ReadyForSerializationReview = document.Summary.RecipeAvailable &&
            document.Summary.BaseComponentValid && document.Summary.PegComponentValid &&
            document.Summary.ShipModelComponentValid && document.Summary.IdentifierComponentValid &&
            document.Summary.PilotDialComponentValid && document.Summary.MediumRejected &&
            document.Summary.ErrorCount == 0;
    }

    private static bool TryParseBaseSize(string value, out FirstEditionBaseSize size)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "small": size = FirstEditionBaseSize.Small; return true;
            case "large": size = FirstEditionBaseSize.Large; return true;
            case "epic":
            case "huge": size = FirstEditionBaseSize.Epic; return true;
            default: size = FirstEditionBaseSize.Small; return false;
        }
    }

    private static bool IsAllowedText(string value) =>
        value.Equals("Small", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Large", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Epic", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Huge", StringComparison.OrdinalIgnoreCase);

    private static void Audit(
        FirstEditionShipObjectModelDocument document,
        string stage,
        string property,
        string value,
        string expected,
        bool valid,
        string source,
        string note)
    {
        document.AuditTrail.Add(new FirstEditionValueAuditEntry
        {
            Sequence = document.AuditTrail.Count + 1,
            Stage = stage,
            Property = property,
            Value = value,
            Expected = expected,
            Valid = valid,
            Source = source,
            Note = note
        });
    }
}
