using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MeetingWeb.Tests.Security;

public class PostgresModelTests
{
    [Fact]
    public void Workspace_titles_use_case_insensitive_Turkish_collation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new ApplicationDbContext(options);
        var model = db.GetService<IDesignTimeModel>().Model;

        var name = model.FindEntityType(typeof(WorkspaceTitle))!.FindProperty(nameof(WorkspaceTitle.Name))!;

        Assert.Equal("tr_ci", name.GetCollation());
    }

    [Theory]
    [InlineData(typeof(Meeting))]
    [InlineData(typeof(ActionItem))]
    [InlineData(typeof(ZoomConnection))]
    [InlineData(typeof(CalendarInvite))]
    public void Row_versions_use_xmin_concurrency_tokens(Type entityType)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new ApplicationDbContext(options);
        var model = db.GetService<IDesignTimeModel>().Model;

        var version = model.FindEntityType(entityType)!.FindProperty("RowVersion")!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(typeof(uint), version.ClrType);
        Assert.Equal("xmin", version.GetColumnName());
    }
}
