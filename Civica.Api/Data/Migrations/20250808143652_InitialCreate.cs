using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Civica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Badges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Rarity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Common"),
                    RequirementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequirementValue = table.Column<int>(type: "integer", nullable: true),
                    RequirementDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Badges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupabaseUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PhotoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    County = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "București"),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "București"),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "Sector 5"),
                    ResidenceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IssueUpdatesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CommunityNewsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyDigestEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AchievementsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IssuesReported = table.Column<int>(type: "integer", nullable: false),
                    IssuesResolved = table.Column<int>(type: "integer", nullable: false),
                    CommunityVotes = table.Column<int>(type: "integer", nullable: false),
                    CommentsGiven = table.Column<int>(type: "integer", nullable: false),
                    HelpfulComments = table.Column<int>(type: "integer", nullable: false),
                    QualityScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    ApprovalRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    CurrentLoginStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestLoginStreak = table.Column<int>(type: "integer", nullable: false),
                    CurrentVotingStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestVotingStreak = table.Column<int>(type: "integer", nullable: false),
                    LastActivityDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MaxProgress = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    RewardPoints = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RewardBadgeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AchievementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequirementData = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Achievements_Badges_RewardBadgeId",
                        column: x => x.RewardBadgeId,
                        principalTable: "Badges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    LocationAccuracy = table.Column<int>(type: "integer", nullable: false),
                    Neighborhood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Urgency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Medium"),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Submitted"),
                    EmailsSent = table.Column<int>(type: "integer", nullable: false),
                    CurrentSituation = table.Column<string>(type: "text", nullable: true),
                    DesiredOutcome = table.Column<string>(type: "text", nullable: true),
                    CommunityImpact = table.Column<string>(type: "text", nullable: true),
                    AIGeneratedDescription = table.Column<string>(type: "text", nullable: true),
                    AIProposedSolution = table.Column<string>(type: "text", nullable: true),
                    AIConfidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Medium"),
                    AssignedDepartment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EstimatedResolutionTime = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PublicVisibility = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Issues_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserBadges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBadges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBadges_Badges_BadgeId",
                        column: x => x.BadgeId,
                        principalTable: "Badges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBadges_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AchievementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAchievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAchievements_Achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "Achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAchievements_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdminSupabaseId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ActionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PreviousStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AssignedDepartment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EstimatedResolutionTime = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminActions_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdminActions_UserProfiles_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmailTrackings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAddress = table.Column<string>(type: "text", nullable: false),
                    AuthorityEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AuthorityName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmailSubject = table.Column<string>(type: "text", nullable: true),
                    EmailBody = table.Column<string>(type: "text", nullable: true),
                    RecipientType = table.Column<string>(type: "text", nullable: false),
                    TrackingStatus = table.Column<string>(type: "text", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTrackings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailTrackings_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailTrackings_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssuePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Quality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Medium"),
                    FileSize = table.Column<int>(type: "integer", nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuePhotos_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Achievements",
                columns: new[] { "Id", "AchievementType", "CreatedAt", "Description", "IsActive", "MaxProgress", "RequirementData", "RewardBadgeId", "RewardPoints", "Title" },
                values: new object[] { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "issues_reported", new DateTime(2025, 8, 8, 14, 36, 52, 21, DateTimeKind.Utc).AddTicks(6698), "Report 10 issues", true, 10, "{\"target\": 10}", null, 200, "Community Champion" });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "Starter", new DateTime(2025, 8, 8, 14, 36, 52, 21, DateTimeKind.Utc).AddTicks(1248), "Reported your first community issue", "/assets/badges/civic-starter.svg", true, "Civic Starter", "Report your first issue", "issues_reported", 1 });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "Rarity", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[] { new Guid("22222222-2222-2222-2222-222222222222"), "Progress", new DateTime(2025, 8, 8, 14, 36, 52, 21, DateTimeKind.Utc).AddTicks(1328), "Uploaded high-quality photos with your report", "/assets/badges/picture-perfect.svg", true, "Picture Perfect", "Uncommon", "Upload 3 high-quality photos", "quality_photos", 3 });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[,]
                {
                    { new Guid("33333333-3333-3333-3333-333333333333"), "Starter", new DateTime(2025, 8, 8, 14, 36, 52, 21, DateTimeKind.Utc).AddTicks(1329), "Sent your first email to authorities", "/assets/badges/email-warrior.svg", true, "Email Warrior", "Send your first email", "emails_sent", 1 },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "Progress", new DateTime(2025, 8, 8, 14, 36, 52, 21, DateTimeKind.Utc).AddTicks(1331), "Voted on 10 community issues", "/assets/badges/community-voice.svg", true, "Community Voice", "Vote on 10 issues", "community_votes", 10 }
                });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "Rarity", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[] { new Guid("55555555-5555-5555-5555-555555555555"), "Achievement", new DateTime(2025, 8, 8, 14, 36, 52, 21, DateTimeKind.Utc).AddTicks(1333), "3 of your issues have been resolved", "/assets/badges/problem-solver.svg", true, "Problem Solver", "Rare", "3 issues resolved", "issues_resolved", 3 });

            migrationBuilder.InsertData(
                table: "Achievements",
                columns: new[] { "Id", "AchievementType", "CreatedAt", "Description", "IsActive", "MaxProgress", "RequirementData", "RewardBadgeId", "RewardPoints", "Title" },
                values: new object[] { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "issues_reported", new DateTime(2025, 8, 8, 14, 36, 52, 21, DateTimeKind.Utc).AddTicks(6604), "Report your first issue", true, 1, "{\"target\": 1}", new Guid("11111111-1111-1111-1111-111111111111"), 50, "First Steps" });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_AchievementType",
                table: "Achievements",
                column: "AchievementType");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_RewardBadgeId",
                table: "Achievements",
                column: "RewardBadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_ActionType",
                table: "AdminActions",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_AdminUserId",
                table: "AdminActions",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_CreatedAt",
                table: "AdminActions",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_IssueId",
                table: "AdminActions",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Badges_Category",
                table: "Badges",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Badges_Name",
                table: "Badges",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Badges_Rarity",
                table: "Badges",
                column: "Rarity");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTrackings_IssueId",
                table: "EmailTrackings",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTrackings_IssueId_UserId_AuthorityEmail",
                table: "EmailTrackings",
                columns: new[] { "IssueId", "UserId", "AuthorityEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTrackings_SentAt",
                table: "EmailTrackings",
                column: "SentAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTrackings_UserId",
                table: "EmailTrackings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuePhotos_CreatedAt",
                table: "IssuePhotos",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IssuePhotos_IssueId",
                table: "IssuePhotos",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Category",
                table: "Issues",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_CreatedAt",
                table: "Issues",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_EmailsSent",
                table: "Issues",
                column: "EmailsSent",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status",
                table: "Issues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status_PublicVisibility",
                table: "Issues",
                columns: new[] { "Status", "PublicVisibility" },
                filter: "\"Status\" = 'Approved' AND \"PublicVisibility\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status_PublicVisibility_CreatedAt",
                table: "Issues",
                columns: new[] { "Status", "PublicVisibility", "CreatedAt" },
                descending: new bool[0],
                filter: "\"Status\" = 'Approved' AND \"PublicVisibility\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Urgency",
                table: "Issues",
                column: "Urgency");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_UserId",
                table: "Issues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_AchievementId",
                table: "UserAchievements",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_Completed_CompletedAt",
                table: "UserAchievements",
                columns: new[] { "Completed", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_UserId",
                table: "UserAchievements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_UserId_AchievementId",
                table: "UserAchievements",
                columns: new[] { "UserId", "AchievementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_BadgeId",
                table: "UserBadges",
                column: "BadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_EarnedAt",
                table: "UserBadges",
                column: "EarnedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_UserId",
                table: "UserBadges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_UserId_BadgeId",
                table: "UserBadges",
                columns: new[] { "UserId", "BadgeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_District",
                table: "UserProfiles",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Email",
                table: "UserProfiles",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Level",
                table: "UserProfiles",
                column: "Level",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Points",
                table: "UserProfiles",
                column: "Points",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_SupabaseUserId",
                table: "UserProfiles",
                column: "SupabaseUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminActions");

            migrationBuilder.DropTable(
                name: "EmailTrackings");

            migrationBuilder.DropTable(
                name: "IssuePhotos");

            migrationBuilder.DropTable(
                name: "UserAchievements");

            migrationBuilder.DropTable(
                name: "UserBadges");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "Badges");
        }
    }
}
