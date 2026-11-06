using System.Reflection;
using System.Text;

namespace SoftTime.Infrastructure.SchemaMigrations;

/// <summary>Scripts embarqués comme ressources de SoftTime.Infrastructure (voir le .csproj).</summary>
public sealed class EmbeddedMigrationScriptSource : IMigrationScriptSource
{
    public const string ResourcePrefix = "SoftTime.Migrations.Versions.";
    private readonly Assembly _assembly;

    public EmbeddedMigrationScriptSource(Assembly? assembly = null)
        => _assembly = assembly ?? typeof(EmbeddedMigrationScriptSource).Assembly;

    public IReadOnlyList<MigrationScript> Load()
    {
        var scripts = new List<MigrationScript>();
        foreach (var resource in _assembly.GetManifestResourceNames().Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var fileName = resource[ResourcePrefix.Length..];
            if (!MigrationScriptNames.TryParseVersion(fileName, out var version))
                throw new InvalidOperationException(
                    $"Le script de migration « {fileName} » ne respecte pas le nommage NNN_description.sql.");
            using var stream = _assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            scripts.Add(new MigrationScript(version, fileName, reader.ReadToEnd()));
        }
        return MigrationScriptNames.Order(scripts);
    }
}
