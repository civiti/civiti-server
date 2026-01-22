using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Civica.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Authorities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    County = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authorities", x => x.Id);
                });

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
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Urgency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Medium"),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Submitted"),
                    EmailsSent = table.Column<int>(type: "integer", nullable: false),
                    DesiredOutcome = table.Column<string>(type: "text", nullable: true),
                    CommunityImpact = table.Column<string>(type: "text", nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
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
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueOwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    IssueTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorDisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AggregatedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activities_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Activities_UserProfiles_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Activities_UserProfiles_IssueOwnerUserId",
                        column: x => x.IssueOwnerUserId,
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
                name: "IssueAuthorities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueAuthorities", x => x.Id);
                    table.CheckConstraint("CK_IssueAuthority_AuthorityOrCustom", "(\"AuthorityId\" IS NOT NULL AND \"CustomName\" IS NULL AND \"CustomEmail\" IS NULL) OR (\"AuthorityId\" IS NULL AND \"CustomName\" IS NOT NULL AND \"CustomEmail\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_IssueAuthorities_Authorities_AuthorityId",
                        column: x => x.AuthorityId,
                        principalTable: "Authorities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IssueAuthorities_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
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
                table: "Authorities",
                columns: new[] { "Id", "City", "County", "CreatedAt", "District", "Email", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("a0000001-0000-0000-0000-000000000001"), "București", "București", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "pmb@pmb.ro", true, "Primăria Municipiului București" },
                    { new Guid("a0000001-0000-0000-0000-000000000002"), "București", "București", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sector 1", "primarie@primarias1.ro", true, "Primăria Sectorului 1 București" },
                    { new Guid("a0000001-0000-0000-0000-000000000003"), "București", "București", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sector 2", "primarie@ps2.ro", true, "Primăria Sectorului 2 București" },
                    { new Guid("a0000001-0000-0000-0000-000000000004"), "București", "București", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sector 3", "primarie@primarie3.ro", true, "Primăria Sectorului 3 București" },
                    { new Guid("a0000001-0000-0000-0000-000000000005"), "București", "București", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sector 4", "primarie@ps4.ro", true, "Primăria Sectorului 4 București" },
                    { new Guid("a0000001-0000-0000-0000-000000000006"), "București", "București", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sector 5", "primarie@sector5.ro", true, "Primăria Sectorului 5 București" },
                    { new Guid("a0000001-0000-0000-0000-000000000007"), "București", "București", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sector 6", "primarie@primarie6.ro", true, "Primăria Sectorului 6 București" },
                    { new Guid("a0000002-0000-0000-0000-000000000001"), "Cluj-Napoca", "Cluj", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "primarie@primariaclujnapoca.ro", true, "Primăria Municipiului Cluj-Napoca" },
                    { new Guid("a0000002-0000-0000-0000-000000000002"), "Timișoara", "Timiș", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "primarie@primariatm.ro", true, "Primăria Municipiului Timișoara" },
                    { new Guid("a0000002-0000-0000-0000-000000000003"), "Iași", "Iași", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "primarie@primaria-iasi.ro", true, "Primăria Municipiului Iași" },
                    { new Guid("a0000002-0000-0000-0000-000000000004"), "Constanța", "Constanța", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "primarie@primaria-constanta.ro", true, "Primăria Municipiului Constanța" },
                    { new Guid("a0000002-0000-0000-0000-000000000005"), "Brașov", "Brașov", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "primarie@brasovcity.ro", true, "Primăria Municipiului Brașov" }
                });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "Starter", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reported your first community issue", "/assets/badges/civic-starter.svg", true, "Civic Starter", "Report your first issue", "issues_reported", 1 });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "Rarity", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111112"), "Progress", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reported 5 community issues", "/assets/badges/active-citizen.svg", true, "Active Citizen", "Uncommon", "Report 5 issues", "issues_reported", 5 },
                    { new Guid("11111111-1111-1111-1111-111111111113"), "Progress", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reported 15 community issues", "/assets/badges/community-champion.svg", true, "Community Champion", "Rare", "Report 15 issues", "issues_reported", 15 },
                    { new Guid("11111111-1111-1111-1111-111111111114"), "Achievement", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reported 30 community issues - a true leader!", "/assets/badges/civic-leader.svg", true, "Civic Leader", "Epic", "Report 30 issues", "issues_reported", 30 },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "Progress", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Uploaded high-quality photos with your reports", "/assets/badges/picture-perfect.svg", true, "Picture Perfect", "Uncommon", "Upload 3 issues with quality photos", "quality_photos", 3 },
                    { new Guid("22222222-2222-2222-2222-222222222223"), "Achievement", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Documented 10 issues with quality photos", "/assets/badges/photo-journalist.svg", true, "Photo Journalist", "Rare", "Upload 10 issues with quality photos", "quality_photos", 10 }
                });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[] { new Guid("44444444-4444-4444-4444-444444444444"), "Progress", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Voted on 10 community issues", "/assets/badges/community-voice.svg", true, "Community Voice", "Vote on 10 issues", "community_votes", 10 });

            migrationBuilder.InsertData(
                table: "Badges",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "IconUrl", "IsActive", "Name", "Rarity", "RequirementDescription", "RequirementType", "RequirementValue" },
                values: new object[,]
                {
                    { new Guid("55555555-5555-5555-5555-555555555555"), "Progress", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Had your first issue resolved", "/assets/badges/problem-solver.svg", true, "Problem Solver", "Uncommon", "1 issue resolved", "issues_resolved", 1 },
                    { new Guid("55555555-5555-5555-5555-555555555556"), "Achievement", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Had 5 of your issues resolved", "/assets/badges/resolution-expert.svg", true, "Resolution Expert", "Rare", "5 issues resolved", "issues_resolved", 5 },
                    { new Guid("55555555-5555-5555-5555-555555555557"), "Achievement", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Had 10 of your issues resolved - making real impact!", "/assets/badges/master-resolver.svg", true, "Master Resolver", "Epic", "10 issues resolved", "issues_resolved", 10 },
                    { new Guid("66666666-6666-6666-6666-666666666666"), "Progress", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Logged in 7 days in a row", "/assets/badges/dedicated-citizen.svg", true, "Dedicated Citizen", "Uncommon", "7-day login streak", "login_streak", 7 },
                    { new Guid("66666666-6666-6666-6666-666666666667"), "Achievement", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Logged in 30 days in a row - incredible dedication!", "/assets/badges/consistency-king.svg", true, "Consistency King", "Rare", "30-day login streak", "login_streak", 30 },
                    { new Guid("77777777-7777-7777-7777-777777777777"), "Progress", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reached level 5", "/assets/badges/rising-star.svg", true, "Rising Star", "Uncommon", "Reach level 5", "level", 5 },
                    { new Guid("77777777-7777-7777-7777-777777777778"), "Achievement", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reached level 10 - a seasoned civic advocate", "/assets/badges/veteran.svg", true, "Veteran", "Rare", "Reach level 10", "level", 10 },
                    { new Guid("77777777-7777-7777-7777-777777777779"), "Special", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reached level 20 - a true civic legend!", "/assets/badges/legend.svg", true, "Legend", "Legendary", "Reach level 20", "level", 20 }
                });

            migrationBuilder.InsertData(
                table: "Achievements",
                columns: new[] { "Id", "AchievementType", "CreatedAt", "Description", "IsActive", "MaxProgress", "RequirementData", "RewardBadgeId", "RewardPoints", "Title" },
                values: new object[,]
                {
                    { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "issues_reported", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Report your first issue to start making a difference", true, 1, "{\"target\": 1}", new Guid("11111111-1111-1111-1111-111111111111"), 25, "First Steps" },
                    { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab"), "issues_reported", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Report 5 issues and become an active citizen", true, 5, "{\"target\": 5}", new Guid("11111111-1111-1111-1111-111111111112"), 75, "Getting Started" },
                    { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaac"), "issues_reported", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Report 15 issues to become a community champion", true, 15, "{\"target\": 15}", new Guid("11111111-1111-1111-1111-111111111113"), 150, "Community Champion" },
                    { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaad"), "issues_reported", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Report 30 issues and become a civic leader", true, 30, "{\"target\": 30}", new Guid("11111111-1111-1111-1111-111111111114"), 300, "Civic Leadership" },
                    { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "issues_resolved", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Get your first issue resolved", true, 1, "{\"target\": 1}", new Guid("55555555-5555-5555-5555-555555555555"), 50, "First Resolution" },
                    { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbc"), "issues_resolved", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Get 5 of your issues resolved", true, 5, "{\"target\": 5}", new Guid("55555555-5555-5555-5555-555555555556"), 150, "Resolution Streak" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"), "login_streak", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Log in 7 days in a row", true, 7, "{\"target\": 7}", new Guid("66666666-6666-6666-6666-666666666666"), 75, "Week Warrior" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccccd"), "login_streak", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Log in 30 days in a row", true, 30, "{\"target\": 30}", new Guid("66666666-6666-6666-6666-666666666667"), 200, "Monthly Dedication" },
                    { new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"), "quality_photos", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Submit 3 issues with quality photos", true, 3, "{\"target\": 3}", new Guid("22222222-2222-2222-2222-222222222222"), 50, "Photography Basics" },
                    { new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "level_up", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Reach level 5", true, 5, "{\"target\": 5}", new Guid("77777777-7777-7777-7777-777777777777"), 100, "Level Up!" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_AchievementType",
                table: "Achievements",
                column: "AchievementType");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_RewardBadgeId",
                table: "Achievements",
                column: "RewardBadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ActorUserId",
                table: "Activities",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CreatedAt",
                table: "Activities",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_IssueId",
                table: "Activities",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_IssueOwnerUserId",
                table: "Activities",
                column: "IssueOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_IssueOwnerUserId_CreatedAt",
                table: "Activities",
                columns: new[] { "IssueOwnerUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Type",
                table: "Activities",
                column: "Type");

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
                name: "IX_Authorities_City",
                table: "Authorities",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_Authorities_City_District",
                table: "Authorities",
                columns: new[] { "City", "District" });

            migrationBuilder.CreateIndex(
                name: "IX_Authorities_Email",
                table: "Authorities",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Authorities_IsActive",
                table: "Authorities",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Authorities_Name",
                table: "Authorities",
                column: "Name");

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
                name: "IX_IssueAuthorities_AuthorityId",
                table: "IssueAuthorities",
                column: "AuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAuthorities_IssueId",
                table: "IssueAuthorities",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAuthorities_IssueId_AuthorityId",
                table: "IssueAuthorities",
                columns: new[] { "IssueId", "AuthorityId" },
                unique: true,
                filter: "\"AuthorityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IssueAuthorities_IssueId_CustomEmail",
                table: "IssueAuthorities",
                columns: new[] { "IssueId", "CustomEmail" },
                unique: true,
                filter: "\"CustomEmail\" IS NOT NULL");

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
                name: "IX_Issues_District",
                table: "Issues",
                column: "District");

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
                filter: "\"Status\" = 'Active' AND \"PublicVisibility\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status_PublicVisibility_CreatedAt",
                table: "Issues",
                columns: new[] { "Status", "PublicVisibility", "CreatedAt" },
                descending: new bool[0],
                filter: "\"Status\" = 'Active' AND \"PublicVisibility\" = true");

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
                name: "Activities");

            migrationBuilder.DropTable(
                name: "AdminActions");

            migrationBuilder.DropTable(
                name: "IssueAuthorities");

            migrationBuilder.DropTable(
                name: "IssuePhotos");

            migrationBuilder.DropTable(
                name: "UserAchievements");

            migrationBuilder.DropTable(
                name: "UserBadges");

            migrationBuilder.DropTable(
                name: "Authorities");

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
