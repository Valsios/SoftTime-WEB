using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Application.Abstractions;

/// <summary>Contrat de <c>CatalogService</c>.</summary>
public interface ICatalogService
{
    Task<IReadOnlyList<SageDbDto>> ListSageAsync(CancellationToken ct = default);

    Task<SageDbDto?> GetSageByIdAsync(int id, CancellationToken ct = default);

    Task<SageDbDto> SaveSageAsync(SageDbDto dto, CancellationToken ct = default);

    Task<ConnectionTestResultDto> TestSageConnectionAsync(TestSageConnectionDto dto, CancellationToken ct = default);

    Task DeleteSageAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<PointeuseDbDto>> ListPointeuseAsync(CancellationToken ct = default);

    Task<PointeuseDbDto?> GetPointeuseByIdAsync(int id, CancellationToken ct = default);

    Task<PointeuseDbDto> SavePointeuseAsync(PointeuseDbDto dto, CancellationToken ct = default);

    Task<ConnectionTestResultDto> TestPointeuseConnectionAsync(TestPointeuseConnectionDto dto, CancellationToken ct = default);

    Task DeletePointeuseAsync(int id, CancellationToken ct = default);

    Task<ClockParamDto> GetClockAsync(CancellationToken ct = default);

    Task<ClockParamDto> SaveClockAsync(ClockParamDto dto, CancellationToken ct = default);

    Task<DepartementServiceDto> GetDepartementServiceAsync(string matricule, CancellationToken ct = default);

    Task<IReadOnlyList<DepartementServiceDto>> GetDepartementServiceBatchAsync(IEnumerable<string> matricules, CancellationToken ct = default);

    /// <summary>
    /// Options de departement / service proposees pour une base AUTRE.
    /// Adapte au nouveau schema : elles proviennent du mapping de l'entite AFFECTATION
    /// (roles SAGE_AFF_DEPARTEMENT / SAGE_AFF_SERVICE) et non plus des colonnes MAP_*.
    /// </summary>
    Task<DepartementServiceOptionsDto> GetDepartementServiceOptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PayrollWriteConfigurationDto>> ListPayrollWriteConfigurationsAsync(CancellationToken ct = default);

    Task<PayrollWriteConfigurationDto> SavePayrollWriteConfigurationAsync(PayrollWriteConfigurationDto dto, CancellationToken ct = default);

    Task DeletePayrollWriteConfigurationAsync(int id, CancellationToken ct = default);

    Task<ConnectionTestResultDto> TestPayrollWriteConfigurationAsync(CancellationToken ct = default);

    /// <summary>
    /// Tables candidates pour le choix d'un code constante sur une base AUTRE.
    /// Adapte au nouveau schema : la table provient du mapping CONSTANT (role SAGE_CST_CODE).
    /// </summary>
    Task<IReadOnlyList<string>> ListOtherCodeConstantTablesAsync(CancellationToken ct = default);

    /// <summary>
    /// Valeurs distinctes de la colonne de code constante pour une base AUTRE.
    /// Adapte au nouveau schema : table et colonne proviennent du mapping CONSTANT
    /// (le parametre <paramref name="table"/> n'est conserve que pour la compatibilite de l'API).
    /// </summary>
    Task<IReadOnlyList<string>> ListOtherCodeConstantValuesAsync(string table, CancellationToken ct = default);

    Task<IReadOnlyList<string>> DiscoverServersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> DiscoverDatabasesAsync(DiscoverDatabasesDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<string>> DiscoverTablesAsync(DiscoverTablesDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<string>> DiscoverColumnsAsync(DiscoverColumnsDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<FieldRoleDto>> GetFieldRolesAsync(string? systemType = null, CancellationToken ct = default);

    Task<CorrespondenceModeDto> GetCorrespondenceAsync(CancellationToken ct = default);

    Task<CorrespondenceModeDto> SetCorrespondenceAsync(bool active, CancellationToken ct = default);

    Task<PagedResult<SageDbDto>> ListSagePagedAsync(IReadOnlyCollection<string>? allowedNames, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<PagedResult<PointeuseDbDto>> ListPointeusePagedAsync(IReadOnlyCollection<string>? allowedNames, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<PagedResult<CardPaieDto>> ListCardsPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<PagedResult<CategoryDto>> ListCategoriesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Liste paginée des affectations. Sans filtre département/service, la page est découpée en SQL
    /// puis enrichie (carte + département/service) pour les seuls matricules de la page. Avec un
    /// filtre, le jeu complet est enrichi avant le découpage, comme pour une liste calculée.
    /// </summary>
    Task<PagedResult<AffectationRowDto>> ListAffectationsPagedAsync(int? categoryId, string? departement, string? service, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    /// <summary>Jeu complet enrichi, pour l'export Excel (jamais la page affichée).</summary>
    Task<IReadOnlyList<AffectationRowDto>> ListAllAffectationsAsync(int? categoryId, string? departement, string? service, CancellationToken ct = default);

    /// <summary>Options des filtres département/service, calculées une fois au montage de l'écran.</summary>
    Task<AffectationOptionsDto> GetAffectationOptionsAsync(CancellationToken ct = default);

    Task<PagedResult<ToleranceDto>> ListTolerancesPagedAsync(int? categoryId, int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<PagedResult<HolidayDto>> ListHolidaysPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<PagedResult<MajorationDto>> ListMajorationsPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<PagedResult<AbsenceCodeDto>> ListAbsenceCodesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Sept catégories ordonnées selon <see cref="OvertimeCodeKeys.Defaults"/> : l'ordre métier n'est
    /// pas une colonne SQL, on pagine donc en mémoire après le tri (volume négligeable).
    /// </summary>
    Task<PagedResult<CodeConstanteDto>> ListCodeConstantesPagedAsync(int? page, int? pageSize, string? search = null, CancellationToken ct = default);

    Task<IReadOnlyList<CardPaieDto>> ListCardsAsync(CancellationToken ct = default);

    Task<CardPaieDto> SaveCardAsync(CardPaieDto dto, CancellationToken ct = default);

    Task DeleteCardAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Supprime la correspondance SAGE ↔ badge identifiée par le couple de matricules
    /// (portage de <c>SupprimerParCorrespondance</c> du desktop). La comparaison des matricules est
    /// insensible au remplissage par des zéros. Le badge est optionnel : sans lui, toutes les
    /// correspondances du matricule SAGE du tenant courant sont supprimées.
    /// </summary>
    Task<int> DeleteCardByCorrespondenceAsync(string matriculeSage, string? matriculePointeuse, CancellationToken ct = default);

    Task<SyncResultDto> AutoMapCardsAsync(CancellationToken ct = default);

    Task<ActivationResultDto> ActivateSagePairAsync(CancellationToken ct = default);

    Task<ImportResultDto> ImportCorrespondencesCsvAsync(IReadOnlyList<string[]> lines, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default);

    Task<CategoryDto> SaveCategoryAsync(CategoryDto dto, CancellationToken ct = default);

    Task DeleteCategoryAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<AffectationDto>> ListAffectationsAsync(int? categoryId, CancellationToken ct = default);

    Task<AffectationDto> SaveAffectationAsync(AffectationDto dto, CancellationToken ct = default);

    Task<ImportResultDto> ImportAffectationsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);

    Task DeleteAffectationAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<ToleranceDto>> ListTolerancesAsync(int? categoryId, CancellationToken ct = default);

    Task<ToleranceDto> SaveToleranceAsync(ToleranceDto dto, CancellationToken ct = default);

    Task DeleteToleranceAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct = default);

    Task<HolidayDto> SaveHolidayAsync(HolidayDto dto, CancellationToken ct = default);

    Task DeleteHolidayAsync(int id, CancellationToken ct = default);

    Task ImportSageHolidaysAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MajorationDto>> ListMajorationsAsync(CancellationToken ct = default);

    Task<MajorationDto> SaveMajorationAsync(MajorationDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<AbsenceCodeDto>> ListAbsenceCodesAsync(CancellationToken ct = default);

    Task<AbsenceCodeDto> SaveAbsenceCodeAsync(AbsenceCodeDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<CodeConstanteDto>> ListCodeConstantesAsync(CancellationToken ct = default);

    Task<CodeConstanteDto> SaveCodeConstanteAsync(CodeConstanteDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<SageConstantOptionDto>> ListSageConstantOptionsAsync(CancellationToken ct = default);

    Task<OvertimeSageCodes> GetOvertimeSageCodesAsync(CancellationToken ct = default);

    Task SyncAbsenceCodesFromSageAsync(CancellationToken ct = default);
}
