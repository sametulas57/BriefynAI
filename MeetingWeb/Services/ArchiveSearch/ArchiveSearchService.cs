using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Snowball;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Analysis.Tr;
using Lucene.Net.Util;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace MeetingWeb.Services.ArchiveSearch
{
    public sealed class ArchiveSearchService : IArchiveSearchService, IDisposable
    {
        private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
        private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
        private readonly IServiceScopeFactory _scopes;
        private readonly Analyzer _stemmed = new TurkishSearchAnalyzer(true, false);
        private readonly Analyzer _plain = new TurkishSearchAnalyzer(false, false);
        private readonly Analyzer _asciiStemmed = new TurkishSearchAnalyzer(true, true);
        private readonly Timer _expiry;

        public ArchiveSearchService(IServiceScopeFactory scopes)
        {
            _scopes = scopes;
            _expiry = new Timer(_ => ExpireIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private void ExpireIdle()
        {
            var now = DateTime.UtcNow;
            foreach (var entry in _cache.Values)
            {
                if (!entry.Gate.Wait(0)) continue;
                try
                {
                    if (now - entry.LastUsed > IdleLifetime) entry.Index = null;
                }
                finally { entry.Gate.Release(); }
            }
        }

        public void Dispose()
        {
            _expiry.Dispose();
            _stemmed.Dispose();
            _plain.Dispose();
            _asciiStemmed.Dispose();
        }

        public async Task<ArchiveSearchResult> SearchAsync(string? userId, int workspaceId, ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || workspaceId <= 0 || query == null) return ArchiveSearchResult.Empty;
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await db.WorkspaceMembers.AsNoTracking().AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken))
                return ArchiveSearchResult.Empty;

            if (query.Scope is not ArchiveScope.Archive and not ArchiveScope.Series and not ArchiveScope.Meeting ||
                (query.Scope != ArchiveScope.Archive && query.TargetId is null)) return ArchiveSearchResult.Empty;

            var entry = _cache.GetOrAdd(workspaceId, _ => new CacheEntry());
            await entry.Gate.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                if (entry.Index == null || entry.Generation != ArchiveIndexGeneration.Current || now - entry.LastUsed > IdleLifetime)
                {
                    // The generation is read before loading: a write that lands meanwhile makes the next search rebuild.
                    // It is stored only with the new index, so a failed rebuild never marks the old index current.
                    var generation = ArchiveIndexGeneration.Current;
                    entry.Index = await BuildAsync(db, workspaceId, cancellationToken);
                    entry.Generation = generation;
                }
                entry.LastUsed = now;
                return Rank(entry.Index!, query);
            }
            finally { entry.Gate.Release(); }
        }

        private async Task<Index> BuildAsync(ApplicationDbContext db, int workspaceId, CancellationToken cancellationToken)
        {
            var meetings = await db.Meetings.AsNoTracking().AsSplitQuery()
                .Where(m => m.WorkspaceId == workspaceId && m.Status == MeetingStatus.Completed)
                .Include(m => m.Points)
                .Include(m => m.ActionItems).ThenInclude(a => a.Assignee)
                .Include(m => m.Transcript)
                .Include(m => m.Series)
                .ToListAsync(cancellationToken);
            var passages = new List<IndexedPassage>();
            foreach (var meeting in meetings)
            {
                var date = DisplayTime.TodayIn(meeting.CreatedAtUtc);
                var prefix = $"{meeting.Title} {date:dd.MM.yyyy} {meeting.Series?.Name} ";
                foreach (var (kind, pointKind) in new[]
                {
                    (ArchivePassageKind.Topic, MeetingPointKind.Topic),
                    (ArchivePassageKind.Decision, MeetingPointKind.Decision),
                    (ArchivePassageKind.Pending, MeetingPointKind.Pending)
                })
                {
                    var number = 0;
                    foreach (var point in meeting.PointsOf(pointKind))
                        Add(meeting, date, kind, ++number, point.Text, prefix, null, false);
                }
                var taskNumber = 0;
                foreach (var task in meeting.OrderedActionItems)
                {
                    var text = task.Text;
                    if (!string.IsNullOrWhiteSpace(task.Assignee?.FullName)) text += $" · Sorumlu: {task.Assignee.FullName}";
                    if (task.DueDate.HasValue) text += $" · Son tarih: {task.DueDate.Value:dd.MM.yyyy}";
                    text += task.IsDone ? " · Durum: bitti" : " · Durum: açık";
                    Add(meeting, date, ArchivePassageKind.Task, ++taskNumber, text, prefix, task.Assignee?.FullName, !task.IsDone);
                }
                var chunkNumber = 0;
                foreach (var chunk in TranscriptChunks(meeting.Transcript?.Text))
                    Add(meeting, date, ArchivePassageKind.Transcript, ++chunkNumber, chunk, prefix, null, false);
            }
            return new Index(meetings.Select(m => new MeetingInfo(m.Id, m.SeriesId, DisplayTime.TodayIn(m.CreatedAtUtc))).ToArray(), passages);

            void Add(Meeting meeting, DateOnly date, ArchivePassageKind kind, int number, string text, string prefix,
                string? assignee, bool open)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                var id = $"T{meeting.Id}-{ "KRBAD"[(int)kind] }{number}";
                var passage = new ArchivePassage(id, meeting.Id, meeting.Title, date, kind, text, 0);
                var terms = Analyze(prefix + text);
                passages.Add(new IndexedPassage(passage, terms, terms.Values.Sum(), assignee, open));
            }
        }

        private static IEnumerable<string> TranscriptChunks(string? transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript)) yield break;
            // Uploaded recordings come back as one long line, so a line longer than a chunk is cut into word runs.
            var lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Chunk(300).Select(run => string.Join(" ", run)));
            var chunk = new List<string>();
            var words = 0;
            foreach (var line in lines)
            {
                var count = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                if (chunk.Count > 0 && words + count > 300 && words >= 200)
                {
                    yield return string.Join("\n", chunk);
                    chunk.Clear();
                    words = 0;
                }
                chunk.Add(line);
                words += count;
            }
            if (chunk.Count > 0) yield return string.Join("\n", chunk);
        }

        private Dictionary<string, int> Analyze(string text)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var analyzer in new[] { _stemmed, _plain, _asciiStemmed })
            {
                using var stream = analyzer.GetTokenStream("text", new StringReader(text));
                var term = stream.AddAttribute<ICharTermAttribute>();
                stream.Reset();
                while (stream.IncrementToken())
                {
                    var value = term.ToString();
                    if (!string.IsNullOrEmpty(value)) result[value] = result.GetValueOrDefault(value) + 1;
                }
                stream.End();
            }
            return result;
        }

        private ArchiveSearchResult Rank(Index index, ArchiveQuery query)
        {
            var meetings = index.Meetings.Where(m =>
                (query.Scope == ArchiveScope.Archive || query.Scope == ArchiveScope.Series && m.SeriesId == query.TargetId ||
                 query.Scope == ArchiveScope.Meeting && m.Id == query.TargetId) &&
                (!query.From.HasValue || m.Date >= query.From.Value) && (!query.To.HasValue || m.Date <= query.To.Value)).ToArray();
            var count = meetings.Length;
            if (count == 0 || string.IsNullOrWhiteSpace(query.Question) || query.Top <= 0)
                return new ArchiveSearchResult(count, Array.Empty<ArchivePassage>());
            var ids = meetings.Select(m => m.Id).ToHashSet();
            var passages = index.Passages.Where(p => ids.Contains(p.Passage.MeetingId)).ToArray();
            if (passages.Length == 0) return new ArchiveSearchResult(count, Array.Empty<ArchivePassage>());
            var queryTerms = Analyze(query.Question).Keys.ToArray();
            if (queryTerms.Length == 0) return new ArchiveSearchResult(count, Array.Empty<ArchivePassage>());
            var frequencies = queryTerms.ToDictionary(t => t, t => passages.Count(p => p.Terms.ContainsKey(t)));
            var avgLength = passages.Average(p => p.Length);
            var foldedQuestion = Fold(query.Question);
            var wantsOpen = new[] { "acik", "bitmemis", "bekleyen", "yapilmadi", "tamamlanmadi" }
                .Any(word => foldedQuestion.Contains(word, StringComparison.Ordinal));
            var scored = new List<ArchivePassage>();
            foreach (var p in passages)
            {
                var score = 0.0;
                foreach (var term in queryTerms)
                {
                    if (!p.Terms.TryGetValue(term, out var tf)) continue;
                    var df = frequencies[term];
                    var idf = Math.Log(1 + (passages.Length - df + 0.5) / (df + 0.5));
                    score += idf * tf * 2.2 / (tf + 1.2 * (0.25 + 0.75 * p.Length / avgLength));
                }
                if (score <= 0) continue;
                score *= p.Passage.Kind == ArchivePassageKind.Transcript ? 1 : 1.8;
                if (p.Passage.Kind == ArchivePassageKind.Task)
                {
                    if (p.Assignee != null && NameInQuestion(foldedQuestion, p.Assignee)) score *= 2.5;
                    if (wantsOpen) score *= p.Open ? 1.8 : 0.5;
                }
                scored.Add(p.Passage with { Score = score });
            }
            return new ArchiveSearchResult(count, scored.OrderByDescending(p => p.Score)
                .ThenByDescending(p => p.MeetingDate).ThenBy(p => p.Id, StringComparer.Ordinal).Take(query.Top).ToArray());
        }

        private static bool NameInQuestion(string question, string name)
        {
            var parts = Fold(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && (question.Contains(string.Join(' ', parts), StringComparison.Ordinal) ||
                question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.StartsWith(parts[0], StringComparison.Ordinal)));
        }

        private static string Fold(string value) => new string(value.Replace('ı', 'i').Replace('İ', 'i').Replace('I', 'i')
            .ToLower(DisplayTime.Culture).Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);

        private sealed class TurkishSearchAnalyzer(bool stem, bool asciiBeforeStem) : Analyzer
        {
            protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
            {
                var tokenizer = new StandardTokenizer(LuceneVersion.LUCENE_48, reader);
                TokenStream stream = new ApostropheFilter(tokenizer);
                stream = new TurkishLowerCaseFilter(stream);
                stream = new StopFilter(LuceneVersion.LUCENE_48, stream, TurkishAnalyzer.DefaultStopSet);
                if (asciiBeforeStem) stream = new ASCIIFoldingFilter(stream);
                if (stem) stream = new SnowballFilter(stream, "Turkish");
                if (!asciiBeforeStem) stream = new ASCIIFoldingFilter(stream);
                return new TokenStreamComponents(tokenizer, stream);
            }
        }

        private sealed class CacheEntry
        {
            public readonly SemaphoreSlim Gate = new(1, 1);
            public Index? Index;
            public long Generation;
            public DateTime LastUsed;
        }

        private sealed record MeetingInfo(int Id, int? SeriesId, DateOnly Date);
        private sealed record IndexedPassage(ArchivePassage Passage, Dictionary<string, int> Terms,
            int Length, string? Assignee, bool Open);
        private sealed record Index(MeetingInfo[] Meetings, List<IndexedPassage> Passages);
    }
}
