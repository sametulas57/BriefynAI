using System.Reflection;
using System.Net;
using System.Text.Json;
using MeetingWeb.Models;
using MeetingWeb.Tests.Features;
using Microsoft.EntityFrameworkCore;
using static MeetingWeb.Tests.Infrastructure.FormHelpers;
using System.Text.RegularExpressions;
using MeetingWeb.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingWeb.Tests.Security;

public class WorkspaceBoundaryTests : IClassFixture<MeetingAccessFixture>
{
    private readonly BriefynFactory _app;

    public WorkspaceBoundaryTests(MeetingAccessFixture fixture) => _app = fixture.Factory;

    internal static HashSet<string> DiscoverHandlers(BriefynFactory factory)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var handlerName = new Regex(@"^On(Get|Post|Put|Delete|Patch)(\w*?)(Async)?$");
        foreach (var type in typeof(Program).Assembly.GetTypes().Where(t => !t.IsAbstract))
        {
            if (typeof(PageModel).IsAssignableFrom(type))
            {
                var folder = type.Namespace!["MeetingWeb.Pages".Length..].Trim('.').Replace('.', '/');
                var page = type.Name.EndsWith("Model", StringComparison.Ordinal) ? type.Name[..^5] : type.Name;
                var path = string.IsNullOrEmpty(folder) ? page : folder + "/" + page;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var match = handlerName.Match(method.Name);
                    if (match.Success) keys.Add($"{path}:{match.Groups[1].Value}{match.Groups[2].Value}");
                }
            }
            if (typeof(Hub).IsAssignableFrom(type))
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    keys.Add($"Hub/{type.Name}:{method.Name}");
        }

        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
            if (endpoint.Metadata.GetMetadata<PageActionDescriptor>() == null)
                keys.Add($"Endpoint:{endpoint.RoutePattern.RawText}");
        return keys;
    }

    internal abstract record Coverage;
    internal sealed record Covered(params (Type TestClass, string Method)[] Tests) : Coverage;
    internal sealed record NoForeignInput(string Reason) : Coverage;
    internal sealed record NotWorkspaceScoped(string Reason) : Coverage;

    internal static readonly IReadOnlyDictionary<string, Coverage> Registry = new Dictionary<string, Coverage>
    {
        ["Ask:Get"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_select_another_workspaces_ask_target))),
        ["Ask:Post"] = new Covered((typeof(AskPageTests), nameof(AskPageTests.Other_teams_meeting_cannot_be_asked)), (typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_ask_another_workspaces_series))),
        ["Auth/ForgotPassword:Get"] = new NotWorkspaceScoped("Authentication or registration operates on account credentials and does not select existing workspace data."),
        ["Auth/ForgotPassword:Post"] = new NotWorkspaceScoped("Authentication or registration operates on account credentials and does not select existing workspace data."),
        ["Auth/Login:Get"] = new NotWorkspaceScoped("Authentication or registration operates on account credentials and does not select existing workspace data."),
        ["Auth/Login:Post"] = new NotWorkspaceScoped("Authentication or registration operates on account credentials and does not select existing workspace data."),
        ["Auth/Register:Post"] = new NotWorkspaceScoped("Authentication or registration operates on account credentials and does not select existing workspace data."),
        ["Auth/ResetPassword:Get"] = new NotWorkspaceScoped("Authentication or registration operates on account credentials and does not select existing workspace data."),
        ["Auth/ResetPassword:Post"] = new NotWorkspaceScoped("Authentication or registration operates on account credentials and does not select existing workspace data."),
        ["Details:Get"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_is_sent_to_own_history_without_seeing_the_meeting))),
        ["Details:PostAddActionItem"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_add_topic_decision_or_action_item))),
        ["Details:PostAddComment"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_comment_or_react_on_another_workspaces_meeting)), (typeof(MeetingAccessTests), nameof(MeetingAccessTests.Reply_cannot_point_at_a_comment_from_another_meeting))),
        ["Details:PostAddDecision"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_add_topic_decision_or_action_item))),
        ["Details:PostAddPending"] = new Covered((typeof(PendingTopicsTests), nameof(PendingTopicsTests.Other_workspace_user_cannot_add_pending_topic))),
        ["Details:PostAddTopic"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_add_topic_decision_or_action_item))),
        ["Details:PostAssignAction"] = new Covered((typeof(MyActionsTests), nameof(MyActionsTests.Only_teammates_can_be_assigned_and_outsiders_cannot_assign)), (typeof(MyActionsTests), nameof(MyActionsTests.Assigning_an_outsider_from_the_page_is_refused_with_a_plain_message))),
        ["Details:PostDeleteComment"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_delete_another_workspaces_comment))),
        ["Details:PostNameSpeakers"] = new Covered((typeof(SpeakerNamingTests), nameof(SpeakerNamingTests.Outsider_cannot_rename_another_workspaces_meeting))),
        ["Details:PostRetryProcessing"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_retry_processing_of_another_workspaces_meeting))),
        ["Details:PostSetSeries"] = new Covered((typeof(SeriesPagesTests), nameof(SeriesPagesTests.Details_links_unlinks_and_rejects_another_teams_series)), (typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_change_another_workspaces_meeting_series))),
        ["Details:PostShare"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_email_another_workspaces_meeting)), (typeof(MeetingAccessTests), nameof(MeetingAccessTests.Share_sends_only_to_members_of_the_meetings_workspace))),
        ["Details:PostToggleReaction"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_comment_or_react_on_another_workspaces_meeting))),
        ["Details:PostToggleTask"] = new Covered((typeof(MeetingAccessTests), nameof(MeetingAccessTests.Outsider_cannot_toggle_someone_elses_task))),
        ["Endpoint:/Zoom/Webhook"] = new NotWorkspaceScoped("The webhook has no signed-in user and validates the Zoom signature before accepting events."),
        ["Endpoint:/meetingHub"] = new NotWorkspaceScoped("The authenticated SignalR hub is server-to-client only and declares no client-callable methods."),
        ["Endpoint:/meetingHub/negotiate"] = new NotWorkspaceScoped("The authenticated SignalR hub is server-to-client only and declares no client-callable methods."),
        ["Error:Get"] = new NotWorkspaceScoped("Only displays generic error information without reading workspace data."),
        ["History:Get"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_search_another_workspaces_history))),
        ["History:PostDelete"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_delete_another_workspaces_meeting))),
        ["Index:Get"] = new NotWorkspaceScoped("The public landing page has no workspace data or target input."),
        ["MyActions:Get"] = new NoForeignInput("Only lists tasks assigned to the signed-in caller within workspaces whose membership is verified; no target input is accepted."),
        ["MyActions:PostSendToCalendar"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_request_another_workspaces_calendar_task))),
        ["MyActions:PostCalendarFile"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_request_another_workspaces_calendar_task))),
        ["MyActions:PostComplete"] = new Covered((typeof(MyActionsTests), nameof(MyActionsTests.Outsider_cannot_complete_someone_elses_task))),
        ["MyActions:PostReopen"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_reopen_another_workspaces_task))),
        ["Profile/Profile:Get"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostChangePassword"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostOverdueReminders"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostCalendarInvites"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostRemoveAvatar"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostRequestDeletion"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostUpdateProfile"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostUploadAvatar"] = new NoForeignInput("Only reads or changes the signed-in caller's account and own memberships; bound profile fields cannot select foreign workspace data."),
        ["Profile/Profile:PostZoomAutoImport"] = new Covered((typeof(ZoomAutoImportTests), nameof(ZoomAutoImportTests.Profile_rejects_another_teams_id_and_does_not_list_its_name))),
        ["Series/Index:Get"] = new NoForeignInput("Only reads or creates data in the caller's active workspace after validating membership; no input names foreign data."),
        ["Series/Index:PostCreate"] = new NoForeignInput("Only reads or creates data in the caller's active workspace after validating membership; no input names foreign data."),
        ["Series/Show:Get"] = new Covered((typeof(SeriesPagesTests), nameof(SeriesPagesTests.Show_hides_other_team_and_lists_own_meetings))),
        ["Series/Show:PostDelete"] = new Covered((typeof(SeriesPagesTests), nameof(SeriesPagesTests.Only_creator_or_team_manager_can_rename_and_delete))),
        ["Series/Show:PostRename"] = new Covered((typeof(SeriesPagesTests), nameof(SeriesPagesTests.Only_creator_or_team_manager_can_rename_and_delete))),
        ["Series/Show:PostSendAgenda"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_send_another_workspaces_agenda))),
        ["StatusCode:Get"] = new NotWorkspaceScoped("Only displays generic error information without reading workspace data."),
        ["Upload:Get"] = new NoForeignInput("Only reads or creates data in the caller's active workspace after validating membership; no input names foreign data."),
        ["Upload:Post"] = new Covered((typeof(SeriesPagesTests), nameof(SeriesPagesTests.Upload_accepts_own_series_and_rejects_other_team_before_creating_meeting))),
        ["Workspace/Create:Post"] = new NoForeignInput("Only creates a new workspace owned by the signed-in caller from a name; no existing workspace can be selected."),
        ["Workspace/Invite:Get"] = new NoForeignInput("Only reads or creates data in the caller's active workspace after validating membership; no input names foreign data."),
        ["Workspace/Invite:Post"] = new NoForeignInput("Only reads or creates data in the caller's active workspace after validating membership; no input names foreign data."),
        ["Workspace/Join:Get"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_use_an_invalid_invitation))),
        ["Workspace/Join:Post"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_use_an_invalid_invitation))),
        ["Workspace/Members:Get"] = new NoForeignInput("Only reads or creates data in the caller's active workspace after validating membership; no input names foreign data."),
        ["Workspace/Members:PostAssignTitle"] = new Covered((typeof(TitlesTests), nameof(TitlesTests.Founder_cannot_change_titles_of_another_team)), (typeof(TitlesTests), nameof(TitlesTests.Another_teams_title_and_unknown_icons_are_refused))),
        ["Workspace/Members:PostCreateTitle"] = new NoForeignInput("Only reads or creates data in the caller's active workspace after validating membership; no input names foreign data."),
        ["Workspace/Members:PostDeleteTitle"] = new Covered((typeof(TitlesTests), nameof(TitlesTests.Founder_cannot_change_titles_of_another_team))),
        ["Workspace/Members:PostDeleteWorkspace"] = new NoForeignInput("Only acts on the caller's session workspace after rechecking membership and role; no input selects another workspace."),
        ["Workspace/Members:PostLeaveWorkspace"] = new NoForeignInput("Only acts on the caller's session workspace after rechecking membership and role; no input selects another workspace."),
        ["Workspace/Members:PostRemoveMember"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_change_another_workspaces_members))),
        ["Workspace/Members:PostSetRole"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_change_another_workspaces_members))),
        ["Workspace/Members:PostTransferOwnership"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_change_another_workspaces_members))),
        ["Workspace/Members:PostUpdateTitle"] = new Covered((typeof(TitlesTests), nameof(TitlesTests.Founder_cannot_change_titles_of_another_team))),
        ["Workspace/Switch:Get"] = new Covered((typeof(WorkspaceBoundaryAttackTests), nameof(WorkspaceBoundaryAttackTests.Outsider_cannot_switch_to_another_workspace))),
        ["Zoom/Callback:Get"] = new NotWorkspaceScoped("Zoom OAuth state is single-use and bound to the signed-in caller and session before connecting that caller's account."),
        ["Zoom/Connect:Get"] = new NotWorkspaceScoped("Always returns NotFound and accesses no workspace data."),
        ["Zoom/Connect:Post"] = new NoForeignInput("Only uses the signed-in caller's own Zoom connection; any displayed imported meetings are limited to validated active membership and no foreign workspace selector is accepted."),
        ["Zoom/Connect:PostDisconnect"] = new NoForeignInput("Only uses the signed-in caller's own Zoom connection; any displayed imported meetings are limited to validated active membership and no foreign workspace selector is accepted."),
        ["Zoom/Recordings:Get"] = new NoForeignInput("Only uses the signed-in caller's own Zoom connection; any displayed imported meetings are limited to validated active membership and no foreign workspace selector is accepted."),
        ["Zoom/Recordings:PostImport"] = new NoForeignInput("The UUID is resolved through the caller's own Zoom connection, existing imports are scoped to the caller's validated active workspace, and the import target comes from that membership."),
    };

    [Fact]
    public void Every_handler_is_registered()
    {
        var missing = DiscoverHandlers(_app).Except(Registry.Keys).Order().ToArray();
        Assert.True(missing.Length == 0, string.Join("\n", missing) +
            "\nYeni uç nokta: başka workspace'in verisine erişemediğini kanıtlayan testi yaz ve buraya kaydet.");
    }

    [Fact]
    public void Registry_has_no_stale_entries() =>
        Assert.Empty(Registry.Keys.Except(DiscoverHandlers(_app)));

    [Fact]
    public void Covered_tests_exist()
    {
        foreach (var (key, coverage) in Registry)
        {
            if (coverage is not Covered covered) continue;
            Assert.NotEmpty(covered.Tests);
            foreach (var (testClass, name) in covered.Tests)
            {
                var method = testClass.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.True(method != null, $"{key}: {testClass.Name}.{name} does not exist");
                Assert.True(method.GetCustomAttributes<FactAttribute>().Any(),
                    $"{key}: {testClass.Name}.{name} must carry [Fact] or [Theory]");
            }
        }
    }

    [Fact]
    public void Reasons_are_written()
    {
        foreach (var (key, coverage) in Registry)
        {
            var reason = coverage switch
            {
                NoForeignInput item => item.Reason,
                NotWorkspaceScoped item => item.Reason,
                _ => null
            };
            if (coverage is not Covered) Assert.False(string.IsNullOrWhiteSpace(reason), key);
        }
    }
}

public class WorkspaceBoundaryAttackFixture : IAsyncLifetime
{
    public BriefynFactory Factory { get; } = new();
    public int SeriesA { get; private set; }
    public int TitleA { get; private set; }

    public async Task InitializeAsync()
    {
        await Factory.InitializeAsync();
        Factory.WithDb(db =>
        {
            var seed = Factory.Seed;
            var series = new MeetingSeries { WorkspaceId = seed.WorkspaceA, CreatedByUserId = seed.AliceId, Name = "A gizli seri" };
            var title = new WorkspaceTitle { WorkspaceId = seed.WorkspaceA, Name = "A gizli unvan" };
            db.AddRange(series, title);
            db.SaveChanges();
            SeriesA = series.Id;
            TitleA = title.Id;
            // Give the foreign series searchable content so an empty archive cannot hide a missing scope check.
            var meeting = db.Meetings.Single(m => m.Id == seed.MeetingA);
            meeting.SeriesId = SeriesA;
            meeting.Status = MeetingStatus.Completed;
            db.MeetingTranscripts.Add(new MeetingTranscript { MeetingId = seed.MeetingA, Text = "A gizli toplantı için gizli bütçe kararı alındı." });
            db.WorkspaceMembers.Single(m => m.WorkspaceId == seed.WorkspaceA && m.UserId == seed.AliceId).TitleId = TitleA;
            return db.SaveChanges();
        });
    }

    public Task DisposeAsync() { Factory.Dispose(); return Task.CompletedTask; }
}

public class WorkspaceBoundaryAttackTests : IClassFixture<WorkspaceBoundaryAttackFixture>
{
    private readonly WorkspaceBoundaryAttackFixture _fixture;
    private readonly BriefynFactory _app;
    private SeedData S => _app.Seed;

    public WorkspaceBoundaryAttackTests(WorkspaceBoundaryAttackFixture fixture)
    {
        _fixture = fixture;
        _app = fixture.Factory;
    }

    private async Task<(HttpClient Client, string Token)> BobAsync()
    {
        var bob = _app.ClientFor(S.BobId);
        return (bob, await GetAntiforgeryTokenAsync(bob, $"/Details?id={S.MeetingB}"));
    }

    // Compare every mapped scalar in both teams, including dependent rows and caller-owned targets.
    // Fresh, untracked reads catch deletes, inserts and partial writes as well as value changes.
    private string[] Snapshot() => _app.WithDb(db =>
    {
        IEnumerable<string> Rows<T>(IQueryable<T> query) where T : class => query.AsNoTracking().AsEnumerable()
            .Select(row => typeof(T).Name + ":" + JsonSerializer.Serialize(db.Entry(row).Properties
                .OrderBy(p => p.Metadata.Name).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)));

        return Rows(db.Workspaces).Concat(Rows(db.WorkspaceMembers)).Concat(Rows(db.WorkspaceInvitations))
            .Concat(Rows(db.WorkspaceTitles)).Concat(Rows(db.MeetingSeries)).Concat(Rows(db.Meetings))
            .Concat(Rows(db.MeetingTranscripts)).Concat(Rows(db.MeetingPoints)).Concat(Rows(db.ActionItems))
            .Concat(Rows(db.Comments)).Concat(Rows(db.CommentReactions)).Concat(Rows(db.ZoomConnections))
            .Concat(Rows(db.Users)).Order(StringComparer.Ordinal).ToArray();
    });

    private async Task<string> AssertRejectedAsync(HttpResponseMessage response, HttpStatusCode status, string[] before,
        string? redirect = null)
    {
        Assert.Equal(status, response.StatusCode);
        if (redirect != null) Assert.Equal(redirect, response.Headers.Location!.OriginalString);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        foreach (var marker in new[] { "A gizli toplantı", "A yorumu", "A gizli seri", "A gizli unvan" })
            Assert.DoesNotContain(marker, body);
        Assert.Equal(before, Snapshot());
        return body;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outsider_cannot_change_another_workspaces_meeting_series(bool createSeries)
    {
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var response = await PostFormAsync(bob, "/Details?handler=SetSeries", token,
                ("meetingId", S.MeetingA.ToString()), ("seriesId", createSeries ? "-1" : ""),
                ("newSeriesName", "Unauthorized series"));
            await AssertRejectedAsync(response, HttpStatusCode.Redirect, before, "/History");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outsider_cannot_delete_another_workspaces_comment(bool ownMeetingRedirect)
    {
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var response = await PostFormAsync(bob, "/Details?handler=DeleteComment", token,
                ("commentId", S.CommentA.ToString()), ("meetingId", (ownMeetingRedirect ? S.MeetingB : S.MeetingA).ToString()));
            await AssertRejectedAsync(response, HttpStatusCode.Redirect, before, "/History");
        }
    }

    [Fact]
    public async Task Outsider_cannot_delete_another_workspaces_meeting()
    {
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var response = await PostFormAsync(bob, "/History?handler=Delete", token, ("id", S.MeetingA.ToString()));
            await AssertRejectedAsync(response, HttpStatusCode.Redirect, before, "/History");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?SearchString=gizli&StartDate=2000-01-01&EndDate=2100-01-01")]
    public async Task Outsider_cannot_search_another_workspaces_history(string query)
    {
        using var bob = _app.ClientFor(S.BobId);
        var before = Snapshot();
        // History has date and text filters, but no bound series/workspace identifier.
        var response = await bob.GetAsync("/History" + query);
        var body = await AssertRejectedAsync(response, HttpStatusCode.OK, before);
        Assert.Contains("B gizli toplantı", body);
    }

    [Theory]
    [InlineData("toplanti")]
    [InlineData("seri")]
    public async Task Outsider_cannot_select_another_workspaces_ask_target(string scope)
    {
        using var bob = _app.ClientFor(S.BobId);
        var before = Snapshot();
        var calls = _app.PythonApi.CallCount;
        var id = scope == "seri" ? _fixture.SeriesA : S.MeetingA;
        var response = await bob.GetAsync($"/Ask?kapsam={scope}&hedef={id}");
        await AssertRejectedAsync(response, HttpStatusCode.OK, before);
        Assert.Equal(calls, _app.PythonApi.CallCount);
    }

    [Fact]
    public async Task Outsider_cannot_ask_another_workspaces_series()
    {
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var calls = _app.PythonApi.CallCount;
            var response = await PostFormAsync(bob, "/Ask", token, ("Question", "Gizli bütçe kararı nedir?"),
                ("Scope", "Series"), ("SeriesId", _fixture.SeriesA.ToString()));
            // This page reports an empty permitted scope with HTTP 200, without asking the AI.
            var body = await AssertRejectedAsync(response, HttpStatusCode.OK, before);
            Assert.Contains("Bu kapsamda bakılacak tamamlanmış toplantı yok.", body);
            Assert.Equal(calls, _app.PythonApi.CallCount);
        }
    }

    [Fact]
    public async Task Outsider_cannot_send_another_workspaces_agenda()
    {
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var jobs = _app.Jobs.CountFor("SendSeriesAgendaAsync", a => (int)a[0]! == _fixture.SeriesA);
            var response = await PostFormAsync(bob, $"/Series/{_fixture.SeriesA}?handler=SendAgenda", token,
                ("selectedEmails", SeedData.AliceEmail), ("extraEmails", "attacker@evil.test"));
            await AssertRejectedAsync(response, HttpStatusCode.NotFound, before);
            Assert.Equal(jobs, _app.Jobs.CountFor("SendSeriesAgendaAsync", a => (int)a[0]! == _fixture.SeriesA));
        }
    }

    [Fact]
    public async Task Outsider_cannot_reopen_another_workspaces_task()
    {
        _app.WithDb(db =>
        {
            var task = db.ActionItems.Single(a => a.Id == S.TaskA);
            task.AssigneeUserId = S.AliceId;
            task.SetDone(true, S.AliceId);
            return db.SaveChanges();
        });
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var response = await PostFormAsync(bob, "/MyActions?handler=Reopen", token, ("taskId", S.TaskA.ToString()));
            await AssertRejectedAsync(response, HttpStatusCode.Redirect, before, "/MyActions");
        }
    }

    [Theory]
    [InlineData("SendToCalendar")]
    [InlineData("CalendarFile")]
    public async Task Outsider_cannot_request_another_workspaces_calendar_task(string handler)
    {
        _app.WithDb(db =>
        {
            var task = db.ActionItems.Single(a => a.Id == S.TaskA);
            task.AssigneeUserId = S.AliceId;
            task.DueDate = new DateOnly(2026, 11, 22);
            task.SetDone(false, S.AliceId);
            return db.SaveChanges();
        });
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var jobs = _app.Jobs.Created.Count;
            var invites = _app.WithDb(db => db.CalendarInvites.Count());
            var response = await PostFormAsync(bob, "/MyActions?handler=" + handler, token, ("taskId", S.TaskA.ToString()));
            await AssertRejectedAsync(response, HttpStatusCode.Redirect, before, "/MyActions");
            Assert.Equal(jobs, _app.Jobs.Created.Count);
            Assert.Equal(invites, _app.WithDb(db => db.CalendarInvites.Count()));
        }
    }

    [Theory]
    [InlineData("RemoveMember", false, false)]
    [InlineData("RemoveMember", true, false)]
    [InlineData("TransferOwnership", false, false)]
    [InlineData("TransferOwnership", true, false)]
    [InlineData("SetRole", false, false)]
    [InlineData("SetRole", true, false)]
    [InlineData("SetRole", true, true)]
    public async Task Outsider_cannot_change_another_workspaces_members(string handler, bool targetCarol, bool demote)
    {
        // Alice is an owner: also attack Carol so target-role restrictions cannot mask a missing workspace check.
        _app.WithDb(db =>
        {
            db.WorkspaceMembers.Single(m => m.WorkspaceId == S.WorkspaceA && m.UserId == S.CarolId).Role =
                demote ? WorkspaceRole.Admin : WorkspaceRole.Member;
            return db.SaveChanges();
        });
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var response = await PostFormAsync(bob, $"/Workspace/Members?handler={handler}", token,
                ("targetUserId", targetCarol ? S.CarolId : S.AliceId), ("makeAdmin", demote ? "false" : "true"));
            await AssertRejectedAsync(response, HttpStatusCode.Redirect, before, "/Workspace/Members");
        }
    }

    [Fact]
    public async Task Outsider_cannot_switch_to_another_workspace()
    {
        using var bob = _app.ClientFor(S.BobId);
        await bob.GetAsync("/History");
        var before = Snapshot();
        var response = await bob.GetAsync($"/Workspace/Switch?workspaceId={S.WorkspaceA}&returnUrl=/History");
        await AssertRejectedAsync(response, HttpStatusCode.Redirect, before, "/History");
        var history = await AssertRejectedAsync(await bob.GetAsync("/History"), HttpStatusCode.OK, before);
        Assert.Contains("B gizli toplantı", history);
    }

    [Theory]
    [InlineData("unknown", false)]
    [InlineData("unknown", true)]
    [InlineData("expired", false)]
    [InlineData("expired", true)]
    [InlineData("used", false)]
    [InlineData("used", true)]
    [InlineData("revoked", false)]
    [InlineData("revoked", true)]
    public async Task Outsider_cannot_use_an_invalid_invitation(string kind, bool post)
    {
        var invitationToken = WorkspaceInvitation.NewToken();
        if (kind != "unknown")
            _app.WithDb(db =>
            {
                db.WorkspaceInvitations.Add(new WorkspaceInvitation
                {
                    WorkspaceId = S.WorkspaceA,
                    TokenHash = WorkspaceInvitation.HashOf(invitationToken),
                    CreatedByUserId = kind == "revoked" ? S.CarolId : S.AliceId,
                    ExpiresAtUtc = kind == "expired" ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(1),
                    UsedAtUtc = kind == "used" ? DateTime.UtcNow : null,
                    UsedByUserId = kind == "used" ? S.CarolId : null
                });
                // Carol is still a member, but cannot issue invitations.
                db.WorkspaceMembers.Single(m => m.WorkspaceId == S.WorkspaceA && m.UserId == S.CarolId).Role = WorkspaceRole.Member;
                return db.SaveChanges();
            });
        var (bob, token) = await BobAsync();
        using (bob)
        {
            var before = Snapshot();
            var response = post
                ? await PostFormAsync(bob, "/Workspace/Join", token, ("Token", invitationToken))
                : await bob.GetAsync($"/Workspace/Join?token={invitationToken}");
            // Invalid invitations render an error page with HTTP 200 and grant no membership.
            var body = await AssertRejectedAsync(response, HttpStatusCode.OK, before);
            Assert.DoesNotContain("A Şirketi", body);
        }
    }
}
