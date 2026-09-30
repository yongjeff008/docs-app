using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentsApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInheritanceControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InheritPermissions",
                table: "Folders",
                type: "INTEGER",
                nullable: false,
                // 已有数据默认继承
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "InheritPermissions",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                // 已有数据默认继承
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "DocumentPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Actions = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentPermissions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissions_DocumentId",
                table: "DocumentPermissions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPermissions_SubjectType_SubjectId",
                table: "DocumentPermissions",
                columns: new[] { "SubjectType", "SubjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPermissions");

            migrationBuilder.DropColumn(
                name: "InheritPermissions",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "InheritPermissions",
                table: "Documents");
        }
    }
}
