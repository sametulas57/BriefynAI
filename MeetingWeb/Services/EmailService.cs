using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Net.Mail;
using MeetingWeb.Data;
using MeetingWeb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingWeb.Services
{
    public class EmailService : IEmailService
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailSettings _emailSettings;
        private readonly string _publicBaseUrl;

        // Constructor injection for DB context and configuration via Options Pattern.
        public EmailService(ApplicationDbContext context, IOptions<EmailSettings> emailSettings, IConfiguration configuration)
        {
            _context = context;
            _emailSettings = emailSettings.Value;
            _publicBaseUrl = (configuration["App:PublicBaseUrl"] ?? "http://localhost:8080").TrimEnd('/');
        }

        public async Task SendMeetingSummaryAsync(int meetingId, string toEmail)
        {
            // Eagerly load the meeting data and its associated action items.
            var meeting = await _context.MeetingSummaries
                .Include(m => m.AksiyonMaddeleri)
                .FirstOrDefaultAsync(m => m.Id == meetingId);

            if (meeting == null) return;

            // Initialize SMTP client with secure credentials using unmanaged resource disposal.
            using var smtpClient = new SmtpClient(_emailSettings.SmtpServer)
            {
                Port = _emailSettings.Port,
                Credentials = new NetworkCredential(_emailSettings.SenderEmail, _emailSettings.Password),
                EnableSsl = true,
            };

            // Compose and dispatch the mail message: a plain-text body plus an HTML alternative.
            // Mail with both parts (and real UTF-8 instead of character entities) is far less likely to be marked as spam.
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_emailSettings.SenderEmail, _emailSettings.SenderName),
                Subject = BuildSubject(meeting),
                SubjectEncoding = Encoding.UTF8,
                HeadersEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false,
                Body = BuildSummaryText(meeting)
            };
            mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(BuildSummaryHtml(meeting), Encoding.UTF8, "text/html"));

            mailMessage.To.Add(toEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }

        public async Task SendActionReminderAsync(int actionItemId, string reminderKey)
        {
            // Re-check everything at send time: the task may have been finished, re-assigned or re-dated since the scan,
            // or the assignee may have left the team. Reminders go only to the assignee's own account address.
            var task = await _context.Set<ActionItem>()
                .Include(a => a.MeetingSummary)
                .Include(a => a.Assignee)
                .FirstOrDefaultAsync(a => a.Id == actionItemId);
            if (task == null || task.IsDone || task.DueDate == null || task.ReminderKey() != reminderKey) return;
            if (string.IsNullOrWhiteSpace(task.Assignee?.Email)) return;
            if (!await _context.WorkspaceUsers.AnyAsync(w => w.UserId == task.AssigneeUserId && w.WorkspaceId == task.MeetingSummary!.WorkspaceId)) return;

            var taskText = task.Text.Split("|~|")[0];
            var meetingTopic = task.MeetingSummary!.ToplantiKonusu;
            var date = task.DueDate.Value.ToString("d MMMM yyyy, dddd", DisplayTime.Culture);
            var link = $"{_publicBaseUrl}/AssignedToMe";

            var text = $"Hatırlatma: yarın son gün.\n\n{taskText}\nToplantı: {meetingTopic}\nSon tarih: {date}\n\nGörevleriniz: {link}\n";
            var html = $@"<div style=""font-family: 'Segoe UI', Arial, sans-serif; color: #1e293b; max-width: 560px;"">
                <p style=""color:#64748b; margin:0 0 8px 0;"">Hatırlatma: yarın son gün.</p>
                <p style=""font-size:17px; font-weight:600; margin:0 0 12px 0;"">{Html.Encode(taskText)}</p>
                <p style=""margin:0;"">Toplantı: {Html.Encode(meetingTopic)}<br>Son tarih: {Html.Encode(date)}</p>
                <p style=""margin:16px 0 0 0;""><a href=""{Html.Encode(link)}"" style=""color:#2563eb;"">Bana atananlar</a></p>
            </div>";

            using var smtpClient = new SmtpClient(_emailSettings.SmtpServer)
            {
                Port = _emailSettings.Port,
                Credentials = new NetworkCredential(_emailSettings.SenderEmail, _emailSettings.Password),
                EnableSsl = true,
            };
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_emailSettings.SenderEmail, _emailSettings.SenderName),
                Subject = $"Yarın son gün: {string.Join(' ', taskText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))}",
                SubjectEncoding = Encoding.UTF8,
                HeadersEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false,
                Body = text
            };
            mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
            mailMessage.To.Add(task.Assignee!.Email!);

            await smtpClient.SendMailAsync(mailMessage);
        }

        // S8: line breaks in a subject could be used to smuggle extra mail headers; collapse them to spaces.
        public static string BuildSubject(MeetingSummary meeting) =>
            $"Briefyn AI Raporu: {string.Join(' ', (meeting.ToplantiKonusu ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))}";

        public static string BuildSummaryText(MeetingSummary meeting)
        {
            var text = new StringBuilder();
            text.AppendLine("Briefyn AI · Toplantı ve Karar Raporu").AppendLine();
            text.AppendLine(meeting.ToplantiKonusu).AppendLine();
            Section("Görüşülen Konular", meeting.GorusulenKonular);
            Section("Alınan Kararlar", meeting.AlinanKararlar);
            Section("Aksiyon Maddeleri", meeting.AksiyonMaddeleri.Select(a => a.Text));
            text.AppendLine($"Bu e-posta Briefyn AI tarafından {DisplayTime.Format(meeting.CreatedDate, "dd MMMM yyyy HH:mm")} tarihli toplantı için oluşturulmuştur.");
            return text.ToString();

            void Section(string title, IEnumerable<string> items)
            {
                text.AppendLine(title);
                foreach (var item in items) text.AppendLine("- " + item.Split("|~|")[0]);
                text.AppendLine();
            }
        }

        // Encodes only what HTML needs (<, >, &, quotes); Turkish letters stay as real UTF-8 text.
        // WebUtility.HtmlEncode turned "ç" into "&#231;", which made the mail look obfuscated to spam filters.
        private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

        // Manual entries carry "text|~|authorId"; only the text belongs in the email, HTML-encoded.
        private static string Item(string value) => Html.Encode(value.Split("|~|")[0]);

        // Construct the responsive HTML email template using inline CSS for mail client compatibility.
        // S8: every meeting value comes from the LLM or from users, so all of it is HTML-encoded.
        public static string BuildSummaryHtml(MeetingSummary meeting)
        {
            return $@"
            <div style=""background-color: #f4f7f6; padding: 40px 20px; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 20px rgba(0,0,0,0.05); overflow: hidden;"">
                    
                    <div style=""background: linear-gradient(135deg, #2563eb, #1d4ed8); padding: 35px 20px; text-align: center;"">
                        <h1 style=""color: #ffffff; margin: 0; font-size: 28px; font-weight: 600; letter-spacing: 0.5px;"">Briefyn AI</h1>
                        <p style=""color: #bfdbfe; margin: 10px 0 0 0; font-size: 15px;"">Akıllı Toplantı ve Karar Raporu</p>
                    </div>

                    <div style=""padding: 35px 30px;"">
                        <h2 style=""color: #1e293b; font-size: 22px; margin-top: 0; margin-bottom: 30px; border-bottom: 2px solid #f1f5f9; padding-bottom: 15px;"">
                            {Html.Encode(meeting.ToplantiKonusu)}
                        </h2>

                        <div style=""margin-bottom: 30px; background-color: #f0f9ff; border-left: 4px solid #0ea5e9; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #475569; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">                                 
                                💬 Görüşülen Konular
                            </h3>
                            <ul style=""color: #334155; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.GorusulenKonular.Select(k => $"<li style='margin-bottom: 8px;'>{Item(k)}</li>"))}
                            </ul>
                        </div>

                        <div style=""margin-bottom: 30px; background-color: #f0fdf4; border-left: 4px solid #22c55e; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #166534; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">
                                🎯 Alınan Kararlar
                            </h3>
                            <ul style=""color: #15803d; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.AlinanKararlar.Select(k => $"<li style='margin-bottom: 8px;'>{Item(k)}</li>"))}
                            </ul>
                        </div>

                        <div style=""margin-bottom: 10px; background-color: #fff7ed; border-left: 4px solid #f97316; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #9a3412; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">
                                ⚡ Aksiyon Maddeleri
                            </h3>
                            <ul style=""color: #c2410c; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.AksiyonMaddeleri.Select(a => $"<li style='margin-bottom: 8px;'>{Item(a.Text)}</li>"))}
                            </ul>
                        </div>

                    </div>

                    <div style=""background-color: #f8fafc; padding: 25px 20px; text-align: center; border-top: 1px solid #e2e8f0;"">
                        <p style=""color: #64748b; font-size: 13px; margin: 0; line-height: 1.5;"">
                            Bu e-posta <strong>Briefyn AI</strong> asistanı tarafından <br> {DisplayTime.Format(meeting.CreatedDate, "dd MMMM yyyy HH:mm")} tarihinde otomatik olarak oluşturulmuştur.
                        </p>
                        <p style=""color: #94a3b8; font-size: 12px; margin-top: 15px; margin-bottom: 0;"">
                            © {DateTime.Now.Year} Briefyn AI. Tüm hakları saklıdır.
                        </p>
                    </div>

                </div>
            </div>";
        }
    }
}