using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnifiedToolkit.AssetRestoration.Epic;

public static class EpicBaseMountPointBuilder
{
    private const double PointInTriangleTolerance = 0.000001;
    private const double HighestRingYTolerance = 0.0005;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EpicBaseMountPointBuildResult Build(
        string repositoryRoot,
        string spawnedSavePath,
        string? outputPath = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        spawnedSavePath = Path.GetFullPath(spawnedSavePath);

        ValidateFile(spawnedSavePath, "Spawned Epic ship save");

        var baseMeshPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "bases",
            "epic",
            "base.obj");
        var pegMeshPath = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "bases",
            "pegs",
            "epic.obj");

        ValidateFile(baseMeshPath, "Epic base mesh");
        ValidateFile(pegMeshPath, "Epic peg mesh");

        var root = JsonNode.Parse(
            File.ReadAllText(spawnedSavePath))
            ?.AsObject()
            ?? throw new InvalidDataException(
                "Could not parse the spawned Epic ship save.");

        var baseObject = FindSpawnedEpicBase(root)
            ?? throw new InvalidDataException(
                "Could not find a spawned Epic/Huge base object " +
                "with runtime mounting points.");

        var baseGuid = baseObject["GUID"]?.GetValue<string>()
            ?? string.Empty;
        var luaState = baseObject["LuaScriptState"]?.GetValue<string>()
            ?? throw new InvalidDataException(
                "The spawned Epic base has no LuaScriptState.");

        var runtimeState = JsonNode.Parse(luaState)?.AsObject()
            ?? throw new InvalidDataException(
                "Could not parse the Epic base LuaScriptState.");

        var mountingPoints = runtimeState["shipData"]?["mountingPoints"]
            ?.AsObject()
            ?? throw new InvalidDataException(
                "The spawned Epic base has no shipData.mountingPoints.");

        var runtimeFore = ReadRuntimePoint(mountingPoints, "front");
        var runtimeAft = ReadRuntimePoint(mountingPoints, "rear");

        ValidatePegChild(baseObject);

        var baseMesh = ReadObj(baseMeshPath);
        var pegMesh = ReadObj(pegMeshPath);
        var pegComponents = FindPegComponents(pegMesh);

        if (pegComponents.Count != 2)
        {
            throw new InvalidDataException(
                "Expected the combined Epic peg OBJ to contain exactly " +
                $"two disconnected peg components, but found " +
                $"{pegComponents.Count}.");
        }

        var forePeg = MatchPegComponent(runtimeFore, pegComponents);
        var aftPeg = MatchPegComponent(runtimeAft, pegComponents);

        if (ReferenceEquals(forePeg, aftPeg))
        {
            throw new InvalidDataException(
                "Fore and Aft runtime points matched the same peg component.");
        }

        var foreAxis = FindPegShaftAxis(
            pegMesh,
            forePeg);
        var aftAxis = FindPegShaftAxis(
            pegMesh,
            aftPeg);

        var foreProjection = ProjectExactly(
            baseMesh,
            foreAxis.X,
            foreAxis.Z);
        var aftProjection = ProjectExactly(
            baseMesh,
            aftAxis.X,
            aftAxis.Z);

        var database = new EpicBaseMountPointDatabase
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceSave = RelativeOrAbsolute(
                repositoryRoot,
                spawnedSavePath),
            SourceBaseGuid = baseGuid,
            BaseMesh = Relative(repositoryRoot, baseMeshPath),
            PegMesh = Relative(repositoryRoot, pegMeshPath),
            Projection = new EpicMountPointProjection
            {
                PointInTriangleTolerance = PointInTriangleTolerance,
                HighestRingYTolerance = HighestRingYTolerance,
                BaseTrianglesExamined = baseMesh.Faces.Count,
                PegConnectedComponents = pegComponents.Count
            },
            MountPoints = new List<EpicBaseMountPoint>
            {
                CreateMountPoint(
                    "fore",
                    "Fore",
                    "front",
                    runtimeFore,
                    foreAxis,
                    foreProjection),
                CreateMountPoint(
                    "aft",
                    "Aft",
                    "rear",
                    runtimeAft,
                    aftAxis,
                    aftProjection)
            },
            Notes = new List<string>
            {
                "The Unified 2.5 runtime mounting points identify which peg component is Fore and which is Aft.",
                "The printed marker centre is derived from the centroid of the highest horizontal vertex ring of each peg shaft in epic.obj.",
                "Each peg centre is projected onto the exact containing top-surface triangle in base.obj.",
                "UV coordinates are calculated with barycentric interpolation; no global affine approximation is used.",
                "No mount-marker position is estimated from a photograph or manually nudged."
            }
        };

        outputPath = outputPath is null
            ? Path.Combine(
                repositoryRoot,
                "assets",
                "source",
                "unified1e",
                "reference",
                "epic",
                "epic-base-mount-points.json")
            : Path.GetFullPath(outputPath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException(
                "Mount-point output has no parent directory."));

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(database, JsonOptions),
            new UTF8Encoding(false));

        var reportPath = Path.Combine(
            repositoryRoot,
            "_unifiedtoolkit_reports",
            "phase15",
            "epic-token-blueprints",
            "EPIC-BASE-MOUNT-POINTS-R5.md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        WriteReport(
            reportPath,
            database,
            outputPath,
            repositoryRoot);

        return new EpicBaseMountPointBuildResult
        {
            Database = database,
            OutputPath = outputPath,
            ReportPath = reportPath
        };
    }

    public static EpicBaseMountPointDatabase? TryLoad(
        string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            "assets",
            "source",
            "unified1e",
            "reference",
            "epic",
            "epic-base-mount-points.json");

        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<EpicBaseMountPointDatabase>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }

    private static EpicBaseMountPoint CreateMountPoint(
        string id,
        string section,
        string runtimeKey,
        (double X, double Z) runtime,
        PegShaftAxis axis,
        ProjectedUv projection) =>
        new()
        {
            Id = id,
            Section = section,
            SourceRuntimeKey = runtimeKey,
            RuntimeLocalPosition = new EpicLocalMountPosition
            {
                X = runtime.X,
                Y = 0,
                Z = runtime.Z
            },
            PegGeometryLocalPosition = new EpicLocalMountPosition
            {
                X = axis.X,
                Y = axis.Y,
                Z = axis.Z
            },
            GeometryMinusRuntime = new EpicLocalMountPosition
            {
                X = axis.X - runtime.X,
                Y = axis.Y,
                Z = axis.Z - runtime.Z
            },
            TextureUv = new EpicTokenOptionalPoint
            {
                U = projection.U,
                V = projection.V
            },
            ProjectionDetails = new EpicBarycentricProjectionDetails
            {
                BaseFaceIndex = projection.FaceIndex,
                TriangleAverageY = projection.AverageY,
                Weights = new EpicBarycentricWeights
                {
                    A = projection.WeightA,
                    B = projection.WeightB,
                    C = projection.WeightC
                },
                TriangleVertices = projection.Vertices
                    .Select(vertex => new EpicLocalMountPosition
                    {
                        X = vertex.X,
                        Y = vertex.Y,
                        Z = vertex.Z
                    })
                    .ToList(),
                TriangleUvs = projection.Uvs
                    .Select(uv => new EpicTokenOptionalPoint
                    {
                        U = uv.U,
                        V = uv.V
                    })
                    .ToList()
            }
        };

    private static PegShaftAxis FindPegShaftAxis(
        ObjMesh pegMesh,
        PegComponent component)
    {
        var componentVertices = component.VertexIndices
            .Select(index => pegMesh.Vertices[index])
            .ToList();

        if (componentVertices.Count == 0)
        {
            throw new InvalidDataException(
                "A peg component contains no vertices.");
        }

        var maximumY = componentVertices.Max(vertex => vertex.Y);

        var highestRing = componentVertices
            .Where(vertex =>
                maximumY - vertex.Y <= HighestRingYTolerance)
            .ToList();

        if (highestRing.Count < 3)
        {
            throw new InvalidDataException(
                "Could not identify a highest vertex ring for a peg shaft.");
        }

        return new PegShaftAxis(
            highestRing.Average(vertex => vertex.X),
            maximumY,
            highestRing.Average(vertex => vertex.Z),
            highestRing.Count);
    }

    private static ProjectedUv ProjectExactly(
        ObjMesh mesh,
        double pointX,
        double pointZ)
    {
        var candidates = new List<ProjectedUv>();

        for (var faceIndex = 0;
             faceIndex < mesh.Faces.Count;
             faceIndex++)
        {
            var face = mesh.Faces[faceIndex];
            if (face.Vertices.Count != 3)
                continue;

            var vertices = face.Vertices
                .Select(reference => mesh.Vertices[reference.VertexIndex])
                .ToArray();
            var uvs = face.Vertices
                .Select(reference => mesh.TextureCoordinates[
                    reference.TextureIndex])
                .ToArray();

            var weights = Barycentric(
                pointX,
                pointZ,
                vertices[0].X,
                vertices[0].Z,
                vertices[1].X,
                vertices[1].Z,
                vertices[2].X,
                vertices[2].Z);

            if (weights is null)
                continue;

            var (a, b, c) = weights.Value;
            if (a < -PointInTriangleTolerance
                || b < -PointInTriangleTolerance
                || c < -PointInTriangleTolerance)
            {
                continue;
            }

            var averageY = vertices.Average(vertex => vertex.Y);
            var u = a * uvs[0].U
                + b * uvs[1].U
                + c * uvs[2].U;
            var v = a * uvs[0].V
                + b * uvs[1].V
                + c * uvs[2].V;

            candidates.Add(
                new ProjectedUv(
                    faceIndex,
                    u,
                    v,
                    averageY,
                    a,
                    b,
                    c,
                    vertices,
                    uvs));
        }

        if (candidates.Count == 0)
        {
            throw new InvalidDataException(
                "No base triangle contains peg centre " +
                $"({pointX:F6}, {pointZ:F6}).");
        }

        // A vertical ray can intersect the lower and upper surface.
        // The visually printed marker belongs to the highest containing face.
        return candidates
            .OrderByDescending(candidate => candidate.AverageY)
            .First();
    }

    private static (double A, double B, double C)? Barycentric(
        double pointX,
        double pointZ,
        double ax,
        double az,
        double bx,
        double bz,
        double cx,
        double cz)
    {
        var denominator =
            (bz - cz) * (ax - cx)
            + (cx - bx) * (az - cz);

        if (Math.Abs(denominator) < 0.000000000001)
            return null;

        var a =
            ((bz - cz) * (pointX - cx)
             + (cx - bx) * (pointZ - cz))
            / denominator;
        var b =
            ((cz - az) * (pointX - cx)
             + (ax - cx) * (pointZ - cz))
            / denominator;
        var c = 1.0 - a - b;

        return (a, b, c);
    }

    private static List<PegComponent> FindPegComponents(
        ObjMesh pegMesh)
    {
        var unionFind = new UnionFind(pegMesh.Vertices.Count);
        var usedVertices = new HashSet<int>();

        foreach (var face in pegMesh.Faces)
        {
            var indices = face.Vertices
                .Select(reference => reference.VertexIndex)
                .Distinct()
                .ToArray();

            foreach (var index in indices)
                usedVertices.Add(index);

            for (var index = 1; index < indices.Length; index++)
                unionFind.Union(indices[0], indices[index]);
        }

        var groups = usedVertices
            .GroupBy(unionFind.Find)
            .Select(group =>
            {
                var vertices = group
                    .Select(index => pegMesh.Vertices[index])
                    .ToList();

                return new PegComponent(
                    vertices.Min(vertex => vertex.X),
                    vertices.Max(vertex => vertex.X),
                    vertices.Min(vertex => vertex.Y),
                    vertices.Max(vertex => vertex.Y),
                    vertices.Min(vertex => vertex.Z),
                    vertices.Max(vertex => vertex.Z),
                    vertices.Count,
                    group.ToArray());
            })
            .OrderBy(component => component.CentreZ)
            .ToList();

        return groups;
    }

    private static PegComponent MatchPegComponent(
        (double X, double Z) runtimePoint,
        IReadOnlyList<PegComponent> components) =>
        components
            .OrderBy(component =>
                SquaredDistance(
                    runtimePoint.X,
                    runtimePoint.Z,
                    component.CentreX,
                    component.CentreZ))
            .First();

    private static double SquaredDistance(
        double ax,
        double az,
        double bx,
        double bz) =>
        Math.Pow(ax - bx, 2)
        + Math.Pow(az - bz, 2);

    private static ObjMesh ReadObj(string path)
    {
        var mesh = new ObjMesh();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                var parts = Split(line);
                mesh.Vertices.Add(
                    new ObjVertex(
                        Parse(parts[1]),
                        Parse(parts[2]),
                        Parse(parts[3])));
                continue;
            }

            if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                var parts = Split(line);
                mesh.TextureCoordinates.Add(
                    new ObjUv(
                        Parse(parts[1]),
                        Parse(parts[2])));
                continue;
            }

            if (!line.StartsWith("f ", StringComparison.Ordinal))
                continue;

            var face = new ObjFace();
            foreach (var token in Split(line).Skip(1))
            {
                var indices = token.Split('/');
                if (!int.TryParse(indices[0], out var vertexIndex))
                {
                    throw new InvalidDataException(
                        $"Invalid OBJ vertex reference '{token}'.");
                }

                var textureIndex = -1;
                if (indices.Length > 1
                    && indices[1].Length > 0
                    && !int.TryParse(indices[1], out textureIndex))
                {
                    throw new InvalidDataException(
                        $"Invalid OBJ texture reference '{token}'.");
                }

                face.Vertices.Add(
                    new ObjFaceVertex(
                        ResolveObjIndex(
                            vertexIndex,
                            mesh.Vertices.Count),
                        textureIndex == -1
                            ? -1
                            : ResolveObjIndex(
                                textureIndex,
                                mesh.TextureCoordinates.Count)));
            }

            if (face.Vertices.Count >= 3)
            {
                foreach (var triangle in Triangulate(face))
                    mesh.Faces.Add(triangle);
            }
        }

        return mesh;
    }

    private static IEnumerable<ObjFace> Triangulate(ObjFace face)
    {
        for (var index = 1;
             index < face.Vertices.Count - 1;
             index++)
        {
            var triangle = new ObjFace();
            triangle.Vertices.Add(face.Vertices[0]);
            triangle.Vertices.Add(face.Vertices[index]);
            triangle.Vertices.Add(face.Vertices[index + 1]);
            yield return triangle;
        }
    }

    private static int ResolveObjIndex(
        int index,
        int count) =>
        index > 0
            ? index - 1
            : count + index;

    private static string[] Split(string line) =>
        line.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

    private static JsonObject? FindSpawnedEpicBase(
        JsonObject root)
    {
        if (root["ObjectStates"] is not JsonArray objects)
            return null;

        foreach (var candidate in EnumerateObjects(objects))
        {
            var meshUrl = candidate["CustomMesh"]?["MeshURL"]
                ?.GetValue<string>()
                ?? string.Empty;
            var state = candidate["LuaScriptState"]?.GetValue<string>()
                ?? string.Empty;

            if ((meshUrl.Contains(
                    "/bases/huge/base.obj",
                    StringComparison.OrdinalIgnoreCase)
                 || meshUrl.Contains(
                    "/bases/epic/base.obj",
                    StringComparison.OrdinalIgnoreCase))
                && state.Contains(
                    "\"mountingPoints\"",
                    StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<JsonObject> EnumerateObjects(
        JsonArray objects)
    {
        foreach (var node in objects)
        {
            if (node is not JsonObject current)
                continue;

            yield return current;

            if (current["ContainedObjects"] is JsonArray contained)
            {
                foreach (var child in EnumerateObjects(contained))
                    yield return child;
            }
        }
    }

    private static (double X, double Z) ReadRuntimePoint(
        JsonObject mountingPoints,
        string key)
    {
        if (mountingPoints[key] is not JsonArray values
            || values.Count < 2)
        {
            throw new InvalidDataException(
                $"Runtime mounting point '{key}' is invalid.");
        }

        return (
            values[0]?.GetValue<double>() ?? 0,
            values[1]?.GetValue<double>() ?? 0);
    }

    private static void ValidatePegChild(
        JsonObject baseObject)
    {
        if (baseObject["ChildObjects"] is not JsonArray children)
        {
            throw new InvalidDataException(
                "The spawned Epic base has no attached child objects.");
        }

        var peg = children
            .OfType<JsonObject>()
            .FirstOrDefault(child =>
            {
                var mesh = child["CustomMesh"]?["MeshURL"]
                    ?.GetValue<string>()
                    ?? string.Empty;
                return mesh.Contains(
                    "/bases/pegs/huge.obj",
                    StringComparison.OrdinalIgnoreCase)
                    || mesh.Contains(
                        "/bases/pegs/epic.obj",
                        StringComparison.OrdinalIgnoreCase);
            })
            ?? throw new InvalidDataException(
                "The spawned Epic base has no attached Epic peg mesh.");

        var transform = peg["Transform"]?.AsObject()
            ?? throw new InvalidDataException(
                "The attached Epic peg has no transform.");

        var values = new[]
        {
            ReadDouble(transform, "posX"),
            ReadDouble(transform, "posY"),
            ReadDouble(transform, "posZ"),
            ReadDouble(transform, "rotX"),
            ReadDouble(transform, "rotY"),
            ReadDouble(transform, "rotZ")
        };

        if (values.Any(value => Math.Abs(value) > 0.001))
        {
            throw new InvalidDataException(
                "The attached Epic peg mesh is not at the base local origin.");
        }
    }

    private static void WriteReport(
        string path,
        EpicBaseMountPointDatabase database,
        string outputPath,
        string repositoryRoot)
    {
        var lines = new List<string>
        {
            "# Epic Base Mount Points — Phase 15B-R5",
            "",
            $"Source save: `{database.SourceSave}`",
            $"Source base GUID: `{database.SourceBaseGuid}`",
            $"Base mesh: `{database.BaseMesh}`",
            $"Peg mesh: `{database.PegMesh}`",
            $"Projection: `{database.Projection.Method}`",
            $"Peg components: {database.Projection.PegConnectedComponents}",
            $"Output: `{Relative(repositoryRoot, outputPath)}`",
            "",
            "## Exact mount points",
            ""
        };

        foreach (var point in database.MountPoints)
        {
            lines.Add(
                $"- {point.Section}: runtime " +
                $"({point.RuntimeLocalPosition.X:F6}, " +
                $"{point.RuntimeLocalPosition.Z:F6}); " +
                $"peg shaft axis " +
                $"({point.PegGeometryLocalPosition.X:F6}, " +
                $"{point.PegGeometryLocalPosition.Z:F6}); " +
                $"delta " +
                $"({point.GeometryMinusRuntime.X:F6}, " +
                $"{point.GeometryMinusRuntime.Z:F6}); " +
                $"UV ({point.TextureUv.U:F6}, " +
                $"{point.TextureUv.V:F6}); " +
                $"base face {point.ProjectionDetails.BaseFaceIndex}");
        }

        lines.Add("");
        lines.Add(
            "The printed marker UVs use the highest peg-shaft ring centroid and exact barycentric interpolation on the highest containing base triangle.");

        File.WriteAllLines(
            path,
            lines,
            new UTF8Encoding(false));
    }

    private static double ReadDouble(
        JsonObject source,
        string propertyName) =>
        source[propertyName]?.GetValue<double>() ?? 0;

    private static double Parse(string value) =>
        double.Parse(
            value,
            CultureInfo.InvariantCulture);

    private static string Relative(
        string repositoryRoot,
        string path) =>
        Path.GetRelativePath(repositoryRoot, path)
            .Replace('\\', '/');

    private static string RelativeOrAbsolute(
        string repositoryRoot,
        string path)
    {
        var relative = Relative(repositoryRoot, path);
        return relative.StartsWith("../", StringComparison.Ordinal)
            ? path.Replace('\\', '/')
            : relative;
    }

    private static void ValidateFile(
        string path,
        string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{description} was not found.",
                path);
        }
    }

    private sealed class ObjMesh
    {
        public List<ObjVertex> Vertices { get; } = new();
        public List<ObjUv> TextureCoordinates { get; } = new();
        public List<ObjFace> Faces { get; } = new();
    }

    private sealed class ObjFace
    {
        public List<ObjFaceVertex> Vertices { get; } = new();
    }

    private sealed record ObjFaceVertex(
        int VertexIndex,
        int TextureIndex);

    private sealed record ObjVertex(
        double X,
        double Y,
        double Z);

    private sealed record ObjUv(
        double U,
        double V);

    private sealed record PegComponent(
        double MinimumX,
        double MaximumX,
        double MinimumY,
        double MaximumY,
        double MinimumZ,
        double MaximumZ,
        int VertexCount,
        IReadOnlyList<int> VertexIndices)
    {
        public double CentreX => (MinimumX + MaximumX) / 2.0;
        public double CentreZ => (MinimumZ + MaximumZ) / 2.0;
    }

    private sealed record PegShaftAxis(
        double X,
        double Y,
        double Z,
        int RingVertexCount);

    private sealed record ProjectedUv(
        int FaceIndex,
        double U,
        double V,
        double AverageY,
        double WeightA,
        double WeightB,
        double WeightC,
        IReadOnlyList<ObjVertex> Vertices,
        IReadOnlyList<ObjUv> Uvs);

    private sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;

        public UnionFind(int count)
        {
            _parent = Enumerable.Range(0, count).ToArray();
            _rank = new byte[count];
        }

        public int Find(int value)
        {
            if (_parent[value] != value)
                _parent[value] = Find(_parent[value]);

            return _parent[value];
        }

        public void Union(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);

            if (leftRoot == rightRoot)
                return;

            if (_rank[leftRoot] < _rank[rightRoot])
            {
                _parent[leftRoot] = rightRoot;
            }
            else if (_rank[leftRoot] > _rank[rightRoot])
            {
                _parent[rightRoot] = leftRoot;
            }
            else
            {
                _parent[rightRoot] = leftRoot;
                _rank[leftRoot]++;
            }
        }
    }
}
