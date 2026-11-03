using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SoftTime.Api.Controllers;
using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Infrastructure.Excel;
using SoftTime.Tests.Http;

namespace SoftTime.Tests;

/// <summary>
/// Contrôleurs réduits à la délégation (convention §3.5, §4.5) : dépendances abstraites seulement, et la logique
/// déplacée des contrôleurs vers les services reste couverte ici.
/// </summary>
public class ThinControllersTests
{
    public static TheoryData<Type> Controllers()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(AuthController).Assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(Controllers))]
    public void Constructor_AnyController_DependsOnlyOnAbstractions(Type controller)
    {
        // Act
        var concrete = controller.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => !p.ParameterType.IsInterface)
            .Select(p => $"{p.ParameterType.Name} {p.Name}")
            .ToList();

        // Assert
        Assert.Empty(concrete);
    }

    [Fact]
    public async Task Schedule_JobAlreadyRunning_ThrowsConflict()
    {
        // Arrange
        var sut = Predictions(new Jobs(accept: false));

        // Act
        var act = () => Task.FromResult(sut.Schedule(PredictionJob.Train));

        // Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(act);
        Assert.Equal(PredictionsService.JobAlreadyRunningMessage, ex.Message);
    }

    [Fact]
    public void Schedule_IdleCoordinator_ReturnsConfirmationAndQueuesFullRefresh()
    {
        // Arrange
        var jobs = new Jobs(accept: true);
        var sut = Predictions(jobs);

        // Act
        var message = sut.Schedule(PredictionJob.Backfill);

        // Assert
        Assert.Equal("Reconstruction complète de la base analytique programmée.", message);
        Assert.True(jobs.Last is { FullRefresh: true, Train: true });
    }

    [Fact]
    public void Schedule_NonAdministrator_ThrowsForbidden()
    {
        // Arrange
        var sut = Predictions(new Jobs(accept: true), new TestCurrentUser(10034, 10034, 6));

        // Act
        var act = () => sut.Schedule(PredictionJob.Refresh);

        // Assert
        Assert.Throws<ForbiddenException>(act);
    }

    [Theory]
    [InlineData("inconnu")]
    [InlineData("")]
    public async Task PagedAsync_UnknownReportType_ThrowsDomainException(string type)
    {
        // Arrange
        var sut = new ReportService(null!, null!, null!, null!, NullLogger<ReportService>.Instance);

        // Act
        var ex = await Assert.ThrowsAsync<DomainException>(() => sut.PagedAsync(type, new ReportFilter(), 1, 20));

        // Assert
        Assert.StartsWith("Type de rapport inconnu", ex.Message);
    }

    [Fact]
    public async Task ImportExcelAsync_FileWithoutHeaders_ThrowsDomainException()
    {
        // Arrange
        var sut = new PlanningService(null!, null!, null!);

        // Act
        var act = () => sut.ImportExcelAsync(new List<Dictionary<string, string>>());

        // Assert
        await Assert.ThrowsAsync<DomainException>(act);
    }

    [Fact]
    public async Task ReadCsvLinesAsync_SemicolonOrCommaLines_ReturnsTrimmedCells()
    {
        // Arrange
        var sut = new ImportFileReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"A\" ; B\r\n\r\nC,D\n"));

        // Act
        var lines = await sut.ReadCsvLinesAsync(stream);

        // Assert
        Assert.Equal(new[] { new[] { "A", "B" }, new[] { "C", "D" } }, lines);
    }

    [Theory]
    [InlineData(null, "export.xlsx")]
    [InlineData("rapport", "rapport.xlsx")]
    [InlineData("rapport.XLSX", "rapport.XLSX")]
    public void Export_AnyFileName_EndsWithXlsx(string? fileName, string expected)
    {
        // Arrange
        var sut = new ExcelExportService();

        // Act
        var file = sut.Export(new ExcelExportRequest { FileName = fileName! });

        // Assert
        Assert.Equal(expected, file.FileName);
        Assert.Equal(GeneratedFile.ExcelContentType, file.ContentType);
        Assert.NotEmpty(file.Content);
    }

    [Fact]
    public void Download_UnknownTemplateKey_ThrowsNotFound()
    {
        // Arrange
        var sut = new ImportTemplateService();

        // Act
        var act = () => sut.Download("cle-inconnue");

        // Assert
        Assert.Throws<NotFoundException>(act);
    }

    [Fact]
    public void RequireUserId_TokenWithoutUser_ThrowsUnauthorized()
    {
        // Arrange
        ICurrentUser sut = NullObject.Create<ICurrentUser>();

        // Act
        Action act = () => sut.RequireUserId();

        // Assert
        Assert.Throws<UnauthorizedException>(act);
    }

    private static PredictionsService Predictions(IMlJobCoordinator jobs, ICurrentUser? user = null)
        => new(NullObject.Create<IMlPredictionService>(), jobs, user ?? new AdminCurrentUser(),
            NullObject.Create<ICompanyContext>(), NullObject.Create<IEffectiveRightsService>());

    private sealed class Jobs : IMlJobCoordinator
    {
        private readonly bool _accept;
        public Jobs(bool accept) => _accept = accept;
        public MlJobRequest? Last { get; private set; }
        public System.Threading.Channels.ChannelReader<MlJobRequest> Reader
            => System.Threading.Channels.Channel.CreateUnbounded<MlJobRequest>().Reader;
        public MlJobState State => new(false, string.Empty, null, null, null);

        public bool TryEnqueue(MlJobRequest job)
        {
            Last = job;
            return _accept;
        }

        public void SetRunning(string message) { }
        public void SetIdle(string message, string? error = null) { }
    }
}
