using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yustore.Migrations
{
    /// <inheritdoc />
    public partial class M4_AdminGovernanceAndSettlementRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settlements");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationRejectionReason",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApplicationStatus",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SettlementBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayeeId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementBatches_AspNetUsers_PayeeId",
                        column: x => x.PayeeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PlatformFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RestaurantPayout = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    DriverPayout = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DriverId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OwnerSettlementBatchId = table.Column<int>(type: "int", nullable: true),
                    DriverSettlementBatchId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderTransactions_AspNetUsers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderTransactions_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderTransactions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderTransactions_SettlementBatches_DriverSettlementBatchId",
                        column: x => x.DriverSettlementBatchId,
                        principalTable: "SettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderTransactions_SettlementBatches_OwnerSettlementBatchId",
                        column: x => x.OwnerSettlementBatchId,
                        principalTable: "SettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderTransactions_DriverId",
                table: "OrderTransactions",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderTransactions_DriverSettlementBatchId",
                table: "OrderTransactions",
                column: "DriverSettlementBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderTransactions_OrderId",
                table: "OrderTransactions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderTransactions_OwnerId",
                table: "OrderTransactions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderTransactions_OwnerSettlementBatchId",
                table: "OrderTransactions",
                column: "OwnerSettlementBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_PayeeId_Year_Month",
                table: "SettlementBatches",
                columns: new[] { "PayeeId", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderTransactions");

            migrationBuilder.DropTable(
                name: "SettlementBatches");

            migrationBuilder.DropColumn(
                name: "ApplicationRejectionReason",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ApplicationStatus",
                table: "AspNetUsers");

            migrationBuilder.CreateTable(
                name: "Settlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DriverId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FoodAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Settlements_AspNetUsers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Settlements_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Settlements_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_DriverId",
                table: "Settlements",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_OrderId",
                table: "Settlements",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_OwnerId",
                table: "Settlements",
                column: "OwnerId");
        }
    }
}
