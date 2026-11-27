using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;
using PanelForge.Application.Features.Bible.Commands.EndorseBibleRevision;
using PanelForge.Application.Features.Bible.Commands.SubmitBibleConcept;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using Xunit;

namespace PanelForge.UnitTests.Integration;

public class BibleWorkflowPostgresIntegrationTests
{
    private static readonly string AdminConn = PostgresTestConnection.Admin;

    private static async Task ExecuteAdminSqlAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(AdminConn);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task EndToEnd_MigrationsClean_And_StubToDraftToSubmitToEndorseToCanonToGate1PassesOnPostgres()
    {
        var tempDbName = $"panelforge_it_{Guid.NewGuid():N}";
        var tempConn = PostgresTestConnection.ForDatabase(tempDbName);

        // 1. Create isolated database
        await ExecuteAdminSqlAsync($"CREATE DATABASE \"{tempDbName}\";");

        try
        {
            var options = new DbContextOptionsBuilder<PanelForgeDbContext>()
                .UseNpgsql(tempConn)
                .Options;

            await using var db = new PanelForgeDbContext(options);
            await db.Database.MigrateAsync();

            // Verify clean EF Core migration snapshot (0 pending model changes)
            db.Database.HasPendingModelChanges().Should().BeFalse();

            // Seed Studio, Series, Users, Bible + Stub
            var writer = User.Create("writer.pg@panelforge.local", "Writer PG");
            var artist = User.Create("artist.pg@panelforge.local", "Artist PG");
            var editor = User.Create("editor.pg@panelforge.local", "Editor PG");
            var workspace = StudioWorkspace.Create("PG Test Studio", writer.Id);
            var series = Series.Create(workspace.Id, "PG Manga", ReadingDirection.RightToLeft);
            var bible = SeriesBible.Create(series.Id, workspace.Id, "PG Bible");
            var stub = BibleEntry.CreateStub(bible.Id, BibleEntryCategory.Character, "Kenji");
            var stubRev = stub.AddRevision("Stub v1", "{}", "hash-stub", authorId: writer.Id, isInitialVersion: true);

            db.Users.AddRange(writer, artist, editor);
            db.StudioWorkspaces.Add(workspace);
            db.Series.Add(series);
            db.SeriesBibles.Add(bible);
            db.BibleEntries.Add(stub);
            db.BibleEntryRevisions.Add(stubRev);
            await db.SaveChangesAsync();

            // Stub -> Draft
            var defineHandler = new DefineBibleEntryCommandHandler(db);
            var defineResult = await defineHandler.Handle(
                new DefineBibleEntryCommand(series.Id, stub.Id, writer.Id, "Kenji Sato", "Main hero", "Hero"),
                CancellationToken.None);
            defineResult.IsSuccess.Should().BeTrue();
            defineResult.Value!.IsStub.Should().BeFalse();

            // Draft -> ConceptSubmitted
            var senderMock = new Mock<ISender>();
            var submitHandler = new SubmitBibleConceptCommandHandler(db, senderMock.Object);
            var submitResult = await submitHandler.Handle(
                new SubmitBibleConceptCommand(series.Id, stub.Id, artist.Id,
                    new SubmitConceptArtRequest(ReferenceImageUrl: "https://cdn.panelforge.local/kenji-v1.png")),
                CancellationToken.None);
            submitResult.IsSuccess.Should().BeTrue();
            var targetRevisionId = submitResult.Value!.Id;

            // Soft Writer Endorsement
            var endorseHandler = new EndorseBibleRevisionCommandHandler(db);
            var endorseResult = await endorseHandler.Handle(
                new EndorseBibleRevisionCommand(series.Id, targetRevisionId, writer.Id, true),
                CancellationToken.None);
            endorseResult.IsSuccess.Should().BeTrue();
            endorseResult.Value!.WriterEndorsement.Should().NotBeNull();
            endorseResult.Value.WriterEndorsement!.Endorsed.Should().BeTrue();
            endorseResult.Value.WriterEndorsement.EndorsedByName.Should().Be("Writer PG");
            endorseResult.Value.WriterEndorsement.ConceptVersion.Should().Be(1);

            // Editor Confirm Canon (CanonLocked)
            var inheritanceResolver = new ChapterCastInheritanceResolver(db);
            var confirmHandler = new ConfirmBibleEntryRevisionCommandHandler(db, inheritanceResolver);
            var confirmResult = await confirmHandler.Handle(
                new ConfirmBibleEntryRevisionCommand(series.Id, stub.Id, targetRevisionId, editor.Id),
                CancellationToken.None);
            confirmResult.IsSuccess.Should().BeTrue();
            confirmResult.Value!.ReviewStatus.Should().Be(BibleRevisionStatus.CanonLocked);

            // Create Chapter with speaker referencing the newly CanonLocked entry and run Gate 1
            var chapter = Chapter.Create(series.Id, 1m, "Chapter 1");
            var scene = Scene.CreateDefault(chapter.Id);
            var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1600, 2400, 300);
            var panel = Panel.Create(page.Id, 1, 1);
            var line = ScriptLine.Create(scene.Id, 1, dialogueText: "Ready!", speakerLabel: "Kenji Sato");
            line.AssignToPanel(panel.Id);
            chapter.SubmitScript(writer.Id);

            db.Chapters.Add(chapter);
            db.Scenes.Add(scene);
            db.Pages.Add(page);
            db.Panels.Add(panel);
            db.ScriptLines.Add(line);
            await db.SaveChangesAsync();

            var castService = new ChapterCastService(db, inheritanceResolver);
            var castGate = new ChapterCastGate(db, castService, inheritanceResolver);
            var gateResult = await castGate.EvaluateStartAsync(chapter.Id, CancellationToken.None);

            gateResult.CanStart.Should().BeTrue();
            gateResult.Blockers.Should().BeEmpty();

            // Q12 / AUD-BE-08: Verify DB partial unique index blocks a second active Publisher even bypassing app guards
            var pub1 = User.Create("pub1.pg@panelforge.local", "Publisher 1", role: SystemRole.Publisher);
            db.Users.Add(pub1);
            await db.SaveChangesAsync();

            var pub2 = User.Create("pub2.pg@panelforge.local", "Publisher 2", role: SystemRole.Publisher);
            db.Users.Add(pub2);
            var dupAct = async () => await db.SaveChangesAsync();
            await dupAct.Should().ThrowAsync<DbUpdateException>();
            db.Entry(pub2).State = EntityState.Detached;

            // Deactivating first Publisher allows creating a new active Publisher
            pub1.Deactivate();
            await db.SaveChangesAsync();
            db.Users.Add(pub2);
            await db.SaveChangesAsync();
        }
        finally
        {
            await ExecuteAdminSqlAsync(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{tempDbName}' AND pid <> pg_backend_pid();");
            await ExecuteAdminSqlAsync($"DROP DATABASE IF EXISTS \"{tempDbName}\";");
        }
    }
}
