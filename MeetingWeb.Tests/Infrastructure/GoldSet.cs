using System.Text.Json;
using System.Text.RegularExpressions;
using MeetingWeb.Data;
using MeetingWeb.Models;

namespace MeetingWeb.Tests.Infrastructure
{
    // The made-up meetings and questions of ai_engine/tests/altin (format in its README), loaded into the test database
    // the way the app stores an analysed meeting: title = summary topic, points and tasks from the summary, transcript lines.
    public class GoldSet
    {
        public record Question(string Id, string Team, string Text, string ScopeKind, string? ScopeTarget, string Kind,
            IReadOnlyList<(string Meeting, string Evidence)> Expected, string? Trace);

        public static readonly string[] Unanswered = { "cevapsiz", "kapsam", "sizma" };

        public Dictionary<string, int> Workspaces { get; } = new();   // "A" → workspace id
        public Dictionary<string, string> Askers { get; } = new();    // "A" → id of a plain member who asks
        public Dictionary<string, int> Meetings { get; } = new();     // "A3" → meeting id
        public Dictionary<string, int> Series { get; } = new();       // "S1" → series id
        public Dictionary<string, string> MeetingTeam { get; } = new();
        public Dictionary<string, DateOnly> MeetingDate { get; } = new();
        public List<Question> Questions { get; } = new();

        public IEnumerable<int> MeetingIdsOf(string team) => Meetings.Where(m => MeetingTeam[m.Key] == team).Select(m => m.Value);

        public static GoldSet Load(ApplicationDbContext db)
        {
            var dir = FindDirectory();
            using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "toplantilar.json")));
            using var questions = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "sorular.json")));
            var gold = new GoldSet();
            var users = new Dictionary<(string Team, string Name), ApplicationUser>();

            ApplicationUser Member(string team, string name, WorkspaceRole role)
            {
                if (users.TryGetValue((team, name), out var existing)) return existing;
                var email = $"{Guid.NewGuid():N}@altin.test";
                var user = new ApplicationUser
                {
                    FullName = name, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString()
                };
                db.Users.Add(user);
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = gold.Workspaces[team], User = user, Role = role });
                return users[(team, name)] = user;
            }

            foreach (var team in data.RootElement.GetProperty("ekipler").EnumerateArray())
            {
                var workspace = new Workspace { Name = team.GetProperty("ad").GetString()! };
                db.Workspaces.Add(workspace);
                db.SaveChanges();
                gold.Workspaces[team.GetProperty("id").GetString()!] = workspace.Id;
            }
            foreach (var team in gold.Workspaces.Keys)
                gold.Askers[team] = Member(team, $"Soran {team}", WorkspaceRole.Member).Id;

            var series = new Dictionary<string, MeetingSeries>();
            foreach (var s in data.RootElement.GetProperty("seriler").EnumerateArray())
            {
                var entity = new MeetingSeries { WorkspaceId = gold.Workspaces[s.GetProperty("ekip").GetString()!], Name = s.GetProperty("ad").GetString()! };
                db.MeetingSeries.Add(entity);
                series[s.GetProperty("id").GetString()!] = entity;
            }

            var meetings = new Dictionary<string, Meeting>();
            foreach (var m in data.RootElement.GetProperty("toplantilar").EnumerateArray())
            {
                var id = m.GetProperty("id").GetString()!;
                var team = m.GetProperty("ekip").GetString()!;
                var date = DateOnly.Parse(m.GetProperty("tarih").GetString()!);
                var summary = m.GetProperty("ozet");
                var seriesId = m.GetProperty("seri").ValueKind == JsonValueKind.Null ? null : m.GetProperty("seri").GetString();
                var meeting = new Meeting
                {
                    WorkspaceId = gold.Workspaces[team],
                    Series = seriesId == null ? null : series[seriesId],
                    Title = summary.GetProperty("toplanti_konusu").GetString()!,
                    Status = MeetingStatus.Completed,
                    CreatedAtUtc = date.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc), // 10:00 in Türkiye
                    CompletedAtUtc = date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc),
                    Transcript = new MeetingTranscript { Text = string.Join("\n", m.GetProperty("dokum").EnumerateArray().Select(l => l.GetString())) }
                };
                var order = 0;
                foreach (var (field, kind) in new[] { ("konular", MeetingPointKind.Topic), ("kararlar", MeetingPointKind.Decision), ("bekleyen", MeetingPointKind.Pending) })
                    foreach (var text in summary.GetProperty(field).EnumerateArray())
                        meeting.Points.Add(new MeetingPoint { Kind = kind, Text = text.GetString()!, SortOrder = order++ });
                order = 0;
                foreach (var a in summary.GetProperty("aksiyonlar").EnumerateArray())
                {
                    var owner = a.GetProperty("sorumlu").GetString();
                    var due = a.GetProperty("son_tarih").GetString();
                    var item = new ActionItem
                    {
                        Text = a.GetProperty("metin").GetString()!,
                        Assignee = owner == null ? null : Member(team, owner, WorkspaceRole.Member),
                        DueDate = due == null ? null : DateOnly.Parse(due),
                        SortOrder = order++
                    };
                    if (a.GetProperty("bitti").GetBoolean()) item.CompletedAtUtc = meeting.CompletedAtUtc;
                    meeting.ActionItems.Add(item);
                }
                db.Meetings.Add(meeting);
                meetings[id] = meeting;
                gold.MeetingTeam[id] = team;
                gold.MeetingDate[id] = date;
            }
            db.SaveChanges();
            foreach (var (id, m) in meetings) gold.Meetings[id] = m.Id;
            foreach (var (id, s) in series) gold.Series[id] = s.Id;

            foreach (var q in questions.RootElement.EnumerateArray())
            {
                var scope = q.GetProperty("kapsam");
                gold.Questions.Add(new Question(
                    q.GetProperty("id").GetString()!, q.GetProperty("ekip").GetString()!, q.GetProperty("soru").GetString()!,
                    scope.GetProperty("tur").GetString()!,
                    scope.GetProperty("hedef").ValueKind == JsonValueKind.Null ? null : scope.GetProperty("hedef").GetString(),
                    q.GetProperty("tur").GetString()!,
                    q.GetProperty("beklenen").EnumerateArray().Select(e => (e.GetProperty("toplanti").GetString()!, e.GetProperty("kanit").GetString()!)).ToList(),
                    q.TryGetProperty("iz", out var trace) ? trace.GetString() : null));
            }
            return gold;
        }

        // Same normalisation as ai_engine/tests/test_altin_set.py: Turkish lower case, single spaces.
        public static string Norm(string text) =>
            Regex.Replace(text.Replace("İ", "i").Replace("I", "ı").ToLowerInvariant(), @"\s+", " ").Trim();

        private static string FindDirectory()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "ai_engine", "tests", "altin");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("ai_engine/tests/altin bulunamadı");
        }
    }
}
