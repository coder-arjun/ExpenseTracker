using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddEventBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                schema: "finoma",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "finoma",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EventContributions",
                schema: "finoma",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromWhom = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventContributions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "finoma",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventContributions_Events_EventId",
                        column: x => x.EventId,
                        principalSchema: "finoma",
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubEvents",
                schema: "finoma",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Allocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "finoma",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubEvents_Events_EventId",
                        column: x => x.EventId,
                        principalSchema: "finoma",
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventSpends",
                schema: "finoma",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubEventId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidTo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSpends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSpends_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "finoma",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventSpends_SubEvents_SubEventId",
                        column: x => x.SubEventId,
                        principalSchema: "finoma",
                        principalTable: "SubEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventContributions_EventId",
                schema: "finoma",
                table: "EventContributions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventContributions_UserId",
                schema: "finoma",
                table: "EventContributions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_UserId_EventDate",
                schema: "finoma",
                table: "Events",
                columns: new[] { "UserId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_UserId_Status",
                schema: "finoma",
                table: "Events",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EventSpends_SubEventId",
                schema: "finoma",
                table: "EventSpends",
                column: "SubEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventSpends_UserId_Date",
                schema: "finoma",
                table: "EventSpends",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SubEvents_EventId",
                schema: "finoma",
                table: "SubEvents",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_SubEvents_EventId_Name",
                schema: "finoma",
                table: "SubEvents",
                columns: new[] { "EventId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubEvents_UserId",
                schema: "finoma",
                table: "SubEvents",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventContributions",
                schema: "finoma");

            migrationBuilder.DropTable(
                name: "EventSpends",
                schema: "finoma");

            migrationBuilder.DropTable(
                name: "SubEvents",
                schema: "finoma");

            migrationBuilder.DropTable(
                name: "Events",
                schema: "finoma");
        }
    }
}
