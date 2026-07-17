using System.Text;
using System.Text.Json;
using UnifiedToolkit.Runtime;

namespace UnifiedToolkit.Commands;

public static class CaptureRuntimeShipPrototypeCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: capture-runtime-ship-prototype <spawned-save.json> --guid <object-guid> [--output <folder>]");
            return 1;
        }

        var savePath = Path.GetFullPath(args[0]);
        var guid = Option(args, "--guid");
        if (string.IsNullOrWhiteSpace(guid))
        {
            Console.Error.WriteLine("A ship object GUID is required: --guid <object-guid>");
            return 1;
        }
        if (!File.Exists(savePath))
        {
            Console.Error.WriteLine($"Save file was not found: {savePath}");
            return 1;
        }

        var output = Option(args, "--output") ?? Path.Combine(Path.GetDirectoryName(savePath) ?? ".", "runtime-ship-prototype");
        output = Path.GetFullPath(output);

        Console.WriteLine("UnifiedToolkit Phase 6A Revision 1 - Runtime Ship Prototype Capture");
        Console.WriteLine("================================================================");
        Console.WriteLine();
        Console.WriteLine($"Spawned save: {savePath}");
        Console.WriteLine($"Ship GUID:    {guid}");
        Console.WriteLine($"Output:       {output}");
        Console.WriteLine();

        try
        {
            var document = RuntimeShipPrototypeCapture.Capture(savePath, guid);
            Directory.CreateDirectory(output);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(output, "runtime-ship-prototype.json"), JsonSerializer.Serialize(document, jsonOptions));
            WriteChildrenCsv(Path.Combine(output, "runtime-ship-prototype-children.csv"), document);
            WriteVisualCsv(Path.Combine(output, "runtime-ship-visual-definition.csv"), document);
            WriteReport(Path.Combine(output, "RUNTIME-SHIP-PROTOTYPE-REPORT.md"), document);

            var summary = document.Summary;
            Console.WriteLine($"Object found:                 {summary.ObjectFound}");
            Console.WriteLine($"Recognised as ship:           {summary.IsShipObject}");
            Console.WriteLine($"Lua state parsed:             {summary.LuaStateParsed}");
            Console.WriteLine($"Child objects:                {summary.ChildObjectCount}");
            Console.WriteLine($"Textures:                     {summary.TextureCount}");
            Console.WriteLine($"Configurations:               {summary.ConfigurationCount}");
            Console.WriteLine($"Primary mesh available:       {summary.PrimaryMeshAvailable}");
            Console.WriteLine($"Active configuration found:   {summary.ActiveConfigurationAvailable}");
            Console.WriteLine($"Validation errors:            {summary.ErrorCount}");
            Console.WriteLine($"Ready for prototype cloning:  {summary.ReadyForPrototypeCloning}");
            Console.WriteLine();
            Console.WriteLine("This command captures a working runtime prototype. It does not modify or generate a TTS save.");
            return summary.ReadyForPrototypeCloning ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static void WriteChildrenCsv(string path, RuntimeShipPrototypeDocument document)
    {
        var lines = new List<string> { "Index,GUID,Role,Nickname,Description,Visible,MeshURL,DiffuseURL,ColliderURL,PosX,PosY,PosZ,RotX,RotY,RotZ,ScaleX,ScaleY,ScaleZ" };
        foreach (var child in document.Prototype?.Children ?? [])
        {
            var t = child.Transform;
            lines.Add(string.Join(',', child.Index, Csv(child.Guid), Csv(child.InferredRole), Csv(child.Nickname), Csv(child.Description), child.VisibleByScale,
                Csv(child.MeshUrl), Csv(child.DiffuseUrl), Csv(child.ColliderUrl), t.PositionX, t.PositionY, t.PositionZ, t.RotationX, t.RotationY, t.RotationZ, t.ScaleX, t.ScaleY, t.ScaleZ));
        }
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteVisualCsv(string path, RuntimeShipPrototypeDocument document)
    {
        var lines = new List<string> { "Category,Key,Value,Active" };
        var visual = document.Prototype?.Visual;
        if (visual is not null)
        {
            lines.Add($"Base,Mesh,{Csv(visual.BaseMesh)},True");
            lines.Add($"Base,Diffuse,{Csv(visual.BaseDiffuse)},True");
            lines.Add($"Base,Collider,{Csv(visual.BaseCollider)},True");
            lines.Add($"Ship,PrimaryMesh,{Csv(visual.PrimaryMesh)},True");
            foreach (var texture in visual.Textures.OrderBy(x => x.Key))
                lines.Add($"Texture,{Csv(texture.Key)},{Csv(texture.Value)},{texture.Key.Equals(visual.SelectedTextureKey, StringComparison.OrdinalIgnoreCase)}");
            foreach (var config in visual.Configurations)
                lines.Add($"Configuration,{Csv(config.Name)},{Csv(config.Mesh)},{config.IsActive}");
        }
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static void WriteReport(string path, RuntimeShipPrototypeDocument document)
    {
        var p = document.Prototype;
        var s = document.Summary;
        var text = new StringBuilder();
        text.AppendLine("# Runtime Ship Prototype Report").AppendLine();
        text.AppendLine($"- Source GUID: `{document.RequestedGuid}`");
        text.AppendLine($"- Ship: `{p?.Nickname}`");
        text.AppendLine($"- Runtime ship ID: `{p?.ShipId}`");
        text.AppendLine($"- Runtime size: `{p?.RuntimeSize}`");
        text.AppendLine($"- Ready for prototype cloning: **{s.ReadyForPrototypeCloning}**").AppendLine();
        if (p is not null)
        {
            text.AppendLine("## Visual definition").AppendLine();
            text.AppendLine($"- Base mesh: `{p.Visual.BaseMesh}`");
            text.AppendLine($"- Base collider: `{p.Visual.BaseCollider}`");
            text.AppendLine($"- Primary mesh: `{p.Visual.PrimaryMesh}`");
            text.AppendLine($"- Selected texture: `{p.Visual.SelectedTextureKey}` → `{p.Visual.SelectedTextureUrl}`");
            foreach (var config in p.Visual.Configurations)
                text.AppendLine($"- Configuration {config.Index}: `{config.Name}` → `{config.Mesh}` (active: {config.IsActive})");
            text.AppendLine().AppendLine("## Saved child hierarchy").AppendLine();
            foreach (var child in p.Children)
                text.AppendLine($"- `{child.InferredRole}` — `{child.Nickname}` — visible: {child.VisibleByScale} — `{child.MeshUrl}`");
        }
        if (document.ValidationErrors.Count > 0)
        {
            text.AppendLine().AppendLine("## Validation errors").AppendLine();
            foreach (var error in document.ValidationErrors) text.AppendLine($"- {error}");
        }
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }

    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
