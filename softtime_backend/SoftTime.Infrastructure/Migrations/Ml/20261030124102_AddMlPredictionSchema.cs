using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftTime.Infrastructure.Migrations.Ml
{
    /// <inheritdoc />
    public partial class AddMlPredictionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ml");

            migrationBuilder.CreateTable(
                name: "Modele",
                schema: "ml",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BddSage = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TypeModele = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Algorithme = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Statut = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EstActif = table.Column<bool>(type: "bit", nullable: false),
                    EntraineLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodeDebut = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodeFin = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NbLignes = table.Column<int>(type: "int", nullable: false),
                    NbPositifs = table.Column<int>(type: "int", nullable: true),
                    MetriquesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SchemaHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ModeleBinaire = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Commentaire = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modele", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalarieJour",
                schema: "ml",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BddSage = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Matricule = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EstJourPlanifie = table.Column<bool>(type: "bit", nullable: false),
                    EstOff = table.Column<bool>(type: "bit", nullable: false),
                    EstFerie = table.Column<bool>(type: "bit", nullable: false),
                    EstWeekend = table.Column<bool>(type: "bit", nullable: false),
                    EstConge = table.Column<bool>(type: "bit", nullable: false),
                    PlanningConnu = table.Column<bool>(type: "bit", nullable: false),
                    HeuresPlanifiees = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    HeuresTravaillees = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    RetardMinutes = table.Column<int>(type: "int", nullable: true),
                    AbsenceNonJustifiee = table.Column<bool>(type: "bit", nullable: false),
                    AbsenceDeclaree = table.Column<bool>(type: "bit", nullable: false),
                    AbsenceHeures = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    JourSemaine = table.Column<byte>(type: "tinyint", nullable: false),
                    SemaineIso = table.Column<byte>(type: "tinyint", nullable: false),
                    AnneeIso = table.Column<short>(type: "smallint", nullable: false),
                    Mois = table.Column<byte>(type: "tinyint", nullable: false),
                    JourDuMois = table.Column<byte>(type: "tinyint", nullable: false),
                    VeilleFerie = table.Column<bool>(type: "bit", nullable: false),
                    LendemainFerie = table.Column<bool>(type: "bit", nullable: false),
                    DebutMois = table.Column<bool>(type: "bit", nullable: false),
                    FinMois = table.Column<bool>(type: "bit", nullable: false),
                    CategorieId = table.Column<int>(type: "int", nullable: true),
                    Departement = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    HeuresSemaineContrat = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    MultiPointage = table.Column<bool>(type: "bit", nullable: false),
                    AncienneteJours = table.Column<int>(type: "int", nullable: true),
                    Absences7j = table.Column<int>(type: "int", nullable: false),
                    Absences30j = table.Column<int>(type: "int", nullable: false),
                    Absences90j = table.Column<int>(type: "int", nullable: false),
                    TauxAbsence30j = table.Column<double>(type: "float", nullable: false),
                    Retards30j = table.Column<int>(type: "int", nullable: false),
                    JoursTravailles7j = table.Column<int>(type: "int", nullable: false),
                    JoursDepuisDerniereAbsence = table.Column<int>(type: "int", nullable: true),
                    HeuresSupp4Semaines = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    TauxAbsenceEquipe30j = table.Column<double>(type: "float", nullable: false),
                    EffectifPrevuJour = table.Column<int>(type: "int", nullable: false),
                    CongesPrevusJour = table.Column<int>(type: "int", nullable: false),
                    SourceGenereeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VersionSchema = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalarieJour", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalarieSemaine",
                schema: "ml",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BddSage = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Matricule = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SemaineDebut = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SemaineIso = table.Column<byte>(type: "tinyint", nullable: false),
                    AnneeIso = table.Column<short>(type: "smallint", nullable: false),
                    HeuresPlanifiees = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    HeuresTravaillees = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    NbJoursPlanifies = table.Column<int>(type: "int", nullable: false),
                    NbJoursTravailles = table.Column<int>(type: "int", nullable: false),
                    CongeJours = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    HS = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    HSValidee = table.Column<bool>(type: "bit", nullable: false),
                    Nuit = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    Dimanche = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    Ferie = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    RetardMinutes = table.Column<int>(type: "int", nullable: false),
                    AbsenceHeures = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    HsMoyenne4Semaines = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    HsMoyenne8Semaines = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    Absences4Semaines = table.Column<int>(type: "int", nullable: false),
                    Retards4Semaines = table.Column<int>(type: "int", nullable: false),
                    HeuresTravaillees4Semaines = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    CategorieId = table.Column<int>(type: "int", nullable: true),
                    Departement = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    HeuresSemaineContrat = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    MultiPointage = table.Column<bool>(type: "bit", nullable: false),
                    SourceGenereeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VersionSchema = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalarieSemaine", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Prediction",
                schema: "ml",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BddSage = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Matricule = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TypeModele = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Cible = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Probabilite = table.Column<double>(type: "float", nullable: true),
                    ValeurPrevue = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    BorneBasse = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    BorneHaute = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    NiveauRisque = table.Column<byte>(type: "tinyint", nullable: false),
                    ModeleId = table.Column<int>(type: "int", nullable: false),
                    GenereLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prediction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prediction_Modele_ModeleId",
                        column: x => x.ModeleId,
                        principalSchema: "ml",
                        principalTable: "Modele",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Modele_BddSage_TypeModele_EstActif",
                schema: "ml",
                table: "Modele",
                columns: new[] { "BddSage", "TypeModele", "EstActif" },
                unique: true,
                filter: "[EstActif] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Prediction_BddSage_TypeModele_Cible",
                schema: "ml",
                table: "Prediction",
                columns: new[] { "BddSage", "TypeModele", "Cible" });

            migrationBuilder.CreateIndex(
                name: "IX_Prediction_BddSage_TypeModele_Matricule_Cible_ModeleId",
                schema: "ml",
                table: "Prediction",
                columns: new[] { "BddSage", "TypeModele", "Matricule", "Cible", "ModeleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prediction_ModeleId",
                schema: "ml",
                table: "Prediction",
                column: "ModeleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalarieJour_BddSage_Date",
                schema: "ml",
                table: "SalarieJour",
                columns: new[] { "BddSage", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SalarieJour_BddSage_Matricule_Date",
                schema: "ml",
                table: "SalarieJour",
                columns: new[] { "BddSage", "Matricule", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalarieSemaine_BddSage_Matricule_SemaineDebut",
                schema: "ml",
                table: "SalarieSemaine",
                columns: new[] { "BddSage", "Matricule", "SemaineDebut" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalarieSemaine_BddSage_SemaineDebut",
                schema: "ml",
                table: "SalarieSemaine",
                columns: new[] { "BddSage", "SemaineDebut" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Prediction",
                schema: "ml");

            migrationBuilder.DropTable(
                name: "SalarieJour",
                schema: "ml");

            migrationBuilder.DropTable(
                name: "SalarieSemaine",
                schema: "ml");

            migrationBuilder.DropTable(
                name: "Modele",
                schema: "ml");
        }
    }
}
