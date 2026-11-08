using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

using SoftTime.Infrastructure.Persistence;

namespace SoftTime.Tests.Persistence;

/// <summary>
/// Fige le mapping EF des quatre contextes (tables, colonnes, types, nullabilité, clés, index, relations).
/// Une modification d'une IEntityTypeConfiguration qui change le schéma attendu fait échouer ce test :
/// si elle est voulue, ajouter le script SQL versionné correspondant puis régénérer l'instantané
/// (fichier « ef-model.actual.txt » écrit à côté des binaires du test).
/// </summary>
public class EfModelSnapshotTests
{
    private const string SnapshotFile = "Persistence/ef-model.snapshot.txt";

    [Fact]
    public void Model_AllContexts_MatchesTheSnapshot()
    {
        // Arrange
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SnapshotFile)).ReplaceLineEndings("\n");

        // Act
        var actual = new StringBuilder()
            .Append(Describe("SoftTime", new SoftTimeDbContext(Options<SoftTimeDbContext>())))
            .Append(Describe("Sage", new SageDbContext(Options<SageDbContext>())))
            .Append(Describe("Pointeuse", new PointeuseDbContext(Options<PointeuseDbContext>())))
            .Append(Describe("Ml", new MlDbContext(Options<MlDbContext>())))
            .ToString();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ef-model.actual.txt"), actual);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void MlModel_ComparedToTheMigrationSnapshot_HasNoPendingChange()
    {
        // Arrange
        using var db = new MlDbContext(Options<MlDbContext>());
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutable)
            snapshot = mutable.FinalizeModel();
        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var current = db.GetService<IDesignTimeModel>().Model;

        // Act
        var pending = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel())
            .Select(Describe)
            .ToList();

        // Assert : une modification du modèle ML exige une nouvelle migration EF.
        pending.Should().BeEmpty();
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        ColumnOperation c => $"{operation.GetType().Name} {c.Table}.{c.Name} {c.ColumnType} null={c.IsNullable}",
        RenameColumnOperation r => $"RenameColumn {r.Table}.{r.Name} -> {r.NewName}",
        _ => operation.GetType().Name
    };

    private static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseSqlServer("Server=.;Database=snapshot", sql => sql.UseCompatibilityLevel(120))
            .Options;

    private static string Describe(string name, DbContext db)
    {
        using (db)
        {
            var model = db.GetService<IDesignTimeModel>().Model;
            var sb = new StringBuilder($"##### {name} schema={model.GetDefaultSchema()}\n");
            foreach (var entity in model.GetEntityTypes().OrderBy(e => e.Name))
                DescribeEntity(sb, entity);
            return sb.ToString();
        }
    }

    private static void DescribeEntity(StringBuilder sb, IEntityType e)
    {
        var table = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
        var key = e.FindPrimaryKey()?.Properties.Select(p => p.Name) ?? Array.Empty<string>();
        sb.Append($"ENTITY {e.ClrType.Name} table={e.GetSchema()}.{e.GetTableName()} key=[{string.Join(",", key)}]\n");
        foreach (var p in e.GetProperties().OrderBy(p => p.Name))
            sb.Append($"  P {p.Name} col={p.GetColumnName(table)} type={p.GetColumnType()} null={p.IsNullable} "
                + $"max={p.GetMaxLength()} fixed={p.IsFixedLength()} unicode={p.IsUnicode()} "
                + $"prec={p.GetPrecision()},{p.GetScale()} gen={p.ValueGenerated}\n");
        foreach (var i in e.GetIndexes().OrderBy(i => i.GetDatabaseName()))
            sb.Append($"  I {i.GetDatabaseName()} [{string.Join(",", i.Properties.Select(p => p.Name))}] "
                + $"unique={i.IsUnique} filter={i.GetFilter()} desc={string.Join(",", i.IsDescending ?? Array.Empty<bool>())}\n");
        foreach (var fk in e.GetForeignKeys().OrderBy(f => f.GetConstraintName()))
            sb.Append($"  FK {fk.GetConstraintName()} [{string.Join(",", fk.Properties.Select(p => p.Name))}] "
                + $"-> {fk.PrincipalEntityType.ClrType.Name} required={fk.IsRequired} delete={fk.DeleteBehavior}\n");
    }
}
