using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuinielaApp.Migrations
{
    /// <inheritdoc />
    public partial class AddStageBanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiLeagueId = table.Column<int>(type: "int", nullable: false),
                    ApiSeason = table.Column<int>(type: "int", nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentQrPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiRound = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntryFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OrderNum = table.Column<int>(type: "int", nullable: false),
                    PredictionDeadline = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BannerImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stages_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TournamentEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TournamentId = table.Column<int>(type: "int", nullable: false),
                    TotalPoints = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StageId = table.Column<int>(type: "int", nullable: false),
                    ApiMatchId = table.Column<int>(type: "int", nullable: false),
                    HomeTeam = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AwayTeam = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HomeTeamLogo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AwayTeamLogo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: true),
                    AwayScore = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Elapsed = table.Column<int>(type: "int", nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HtHomeScore = table.Column<int>(type: "int", nullable: true),
                    HtAwayScore = table.Column<int>(type: "int", nullable: true),
                    YellowCards = table.Column<int>(type: "int", nullable: false),
                    RedCards = table.Column<int>(type: "int", nullable: false),
                    HasPenalty = table.Column<bool>(type: "bit", nullable: false),
                    FirstScorer = table.Column<int>(type: "int", nullable: true),
                    BothScored = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    StageId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    VoucherPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    StageId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageEntries_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StageId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    ResultHits = table.Column<int>(type: "int", nullable: false),
                    ScoreHits = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageResults_Stages_StageId",
                        column: x => x.StageId,
                        principalTable: "Stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageResults_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Predictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MatchId = table.Column<int>(type: "int", nullable: false),
                    ResultPrediction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HomeScorePred = table.Column<int>(type: "int", nullable: true),
                    AwayScorePred = table.Column<int>(type: "int", nullable: true),
                    HtHomePred = table.Column<int>(type: "int", nullable: true),
                    HtAwayPred = table.Column<int>(type: "int", nullable: true),
                    YellowCardsPred = table.Column<int>(type: "int", nullable: true),
                    RedCardsPred = table.Column<int>(type: "int", nullable: true),
                    HasPenaltyPred = table.Column<bool>(type: "bit", nullable: true),
                    FirstScorerPred = table.Column<int>(type: "int", nullable: true),
                    BothScoredPred = table.Column<bool>(type: "bit", nullable: true),
                    TotalGoalsPred = table.Column<int>(type: "int", nullable: true),
                    GoalDiffPred = table.Column<int>(type: "int", nullable: true),
                    PointsEarned = table.Column<int>(type: "int", nullable: false),
                    ResultCorrect = table.Column<bool>(type: "bit", nullable: true),
                    ScoreCorrect = table.Column<bool>(type: "bit", nullable: true),
                    SpecialPoints = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Predictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Predictions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Predictions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ApiMatchId",
                table: "Matches",
                column: "ApiMatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_StageId",
                table: "Matches",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_StageId",
                table: "Payments",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId",
                table: "Payments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_MatchId",
                table: "Predictions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_UserId_MatchId",
                table: "Predictions",
                columns: new[] { "UserId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageEntries_StageId",
                table: "StageEntries",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_StageEntries_UserId_StageId",
                table: "StageEntries",
                columns: new[] { "UserId", "StageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageResults_StageId_UserId",
                table: "StageResults",
                columns: new[] { "StageId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageResults_UserId",
                table: "StageResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Stages_TournamentId",
                table: "Stages",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_TournamentId",
                table: "TournamentEntries",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntries_UserId_TournamentId",
                table: "TournamentEntries",
                columns: new[] { "UserId", "TournamentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Predictions");

            migrationBuilder.DropTable(
                name: "StageEntries");

            migrationBuilder.DropTable(
                name: "StageResults");

            migrationBuilder.DropTable(
                name: "TournamentEntries");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Stages");

            migrationBuilder.DropTable(
                name: "Tournaments");
        }
    }
}
