using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Civica.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create UserProfiles table
            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupabaseId = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfilePictureUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    County = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    City = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    District = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ResidenceType = table.Column<int>(type: "integer", nullable: true),
                    Points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Level = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Streak = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalIssuesReported = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalEmailsSent = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IssueUpdatesEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CommunityNewsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MonthlyDigestEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AchievementsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            // Create Issues table
            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    LocationAccuracy = table.Column<int>(type: "integer", nullable: false),
                    Neighborhood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Urgency = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EmailsSent = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CurrentSituation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DesiredOutcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CommunityImpact = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AIGeneratedDescription = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    AIProposedSolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AIConfidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AssignedDepartment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EstimatedResolutionTime = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PublicVisibility = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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

            // Create Badges table
            migrationBuilder.CreateTable(
                name: "Badges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Rarity = table.Column<int>(type: "integer", nullable: false),
                    RequirementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequirementValue = table.Column<int>(type: "integer", nullable: false),
                    RequirementDescription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Badges", x => x.Id);
                });

            // Create Achievements table
            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MaxProgress = table.Column<int>(type: "integer", nullable: false),
                    RewardPoints = table.Column<int>(type: "integer", nullable: false),
                    RewardBadgeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AchievementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequirementData = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            // Create IssuePhotos table
            migrationBuilder.CreateTable(
                name: "IssuePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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

            // Create AdminActions table
            migrationBuilder.CreateTable(
                name: "AdminActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                        onDelete: ReferentialAction.Restrict);
                });

            // Create EmailTrackings table
            migrationBuilder.CreateTable(
                name: "EmailTrackings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmailSubject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EmailBody = table.Column<string>(type: "text", nullable: true),
                    RecipientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "authority"),
                    TrackingStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "sent")
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
                        onDelete: ReferentialAction.Restrict);
                });

            // Create UserBadges table
            migrationBuilder.CreateTable(
                name: "UserBadges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    AwardReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
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

            // Create UserAchievements table
            migrationBuilder.CreateTable(
                name: "UserAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AchievementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            // Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_Achievements_RewardBadgeId",
                table: "Achievements",
                column: "RewardBadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_AdminUserId",
                table: "AdminActions",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_IssueId",
                table: "AdminActions",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTrackings_IssueId",
                table: "EmailTrackings",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTrackings_UserId",
                table: "EmailTrackings",
                column: "UserId");

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
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status",
                table: "Issues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_UserId",
                table: "Issues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_AchievementId",
                table: "UserAchievements",
                column: "AchievementId");

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
                name: "IX_UserBadges_UserId_BadgeId",
                table: "UserBadges",
                columns: new[] { "UserId", "BadgeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Email",
                table: "UserProfiles",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_SupabaseId",
                table: "UserProfiles",
                column: "SupabaseId",
                unique: true);

            // Insert seed data for badges
            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Name", "Description", "IconUrl", "Category", "Rarity", "RequirementType", "RequirementValue", "RequirementDescription", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "Civic Starter", "Reported your first community issue", "/assets/badges/civic-starter.svg", 0, 0, "issues_reported", 1, "Report your first issue", new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "Picture Perfect", "Uploaded high-quality photos with your report", "/assets/badges/picture-perfect.svg", 1, 1, "quality_photos", 3, "Upload 3 high-quality photos", new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "Email Warrior", "Sent your first email to authorities", "/assets/badges/email-warrior.svg", 0, 0, "emails_sent", 1, "Send your first email", new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "Community Voice", "Voted on 10 community issues", "/assets/badges/community-voice.svg", 1, 0, "community_votes", 10, "Vote on 10 issues", new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("55555555-5555-5555-5555-555555555555"), "Problem Solver", "3 of your issues have been resolved", "/assets/badges/problem-solver.svg", 2, 2, "issues_resolved", 3, "3 issues resolved", new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            // Insert seed data for achievements
            migrationBuilder.InsertData(
                table: "Achievements",
                columns: new[] { "Id", "Title", "Description", "MaxProgress", "RewardPoints", "RewardBadgeId", "AchievementType", "RequirementData", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "First Steps", "Report your first issue", 1, 50, new Guid("11111111-1111-1111-1111-111111111111"), "issues_reported", "{\"target\": 1}", new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Community Champion", "Report 10 issues", 10, 200, null, "issues_reported", "{\"target\": 10}", new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 7, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
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
                name: "Badges");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}