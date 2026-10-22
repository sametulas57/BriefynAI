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

        // Constructor injection for DB context and configuration via Options Pattern.
        public EmailService(ApplicationDbContext context, IOptions<EmailSettings> emailSettings)
        {
            _context = context;
            _emailSettings = emailSettings.Value;
        }

        private const string RecipientPurpose = "HangFire.Job.Recipient";

        public static string ProtectRecipient(string email) => EncryptionHelper.Encrypt(email, RecipientPurpose);

        public static string? UnprotectRecipient(string protectedRecipient) =>
            EncryptionHelper.TryDecrypt(protectedRecipient, RecipientPurpose, out var email) ? email : null;

        public async Task SendMeetingSummaryAsync(int meetingId, string protectedRecipient)
        {
            if (UnprotectRecipient(protectedRecipient) is not { } toEmail) return;

            // Eagerly load the meeting data and its summary lines.
            var meeting = await _context.Meetings
                .Include(m => m.Points)
                .Include(m => m.ActionItems)
                .AsSplitQuery()
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

        // S8: line breaks in a subject could be used to smuggle extra mail headers; collapse them to spaces.
        public static string BuildSubject(Meeting meeting) =>
            $"Briefyn AI Raporu: {string.Join(' ', (meeting.Title ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))}";

        public static string BuildSummaryText(Meeting meeting)
        {
            var text = new StringBuilder();
            text.AppendLine("Briefyn AI · Toplantı ve Karar Raporu").AppendLine();
            text.AppendLine(meeting.Title).AppendLine();
            Section("Görüşülen Konular", meeting.PointsOf(MeetingPointKind.Topic).Select(p => p.Text));
            Section("Alınan Kararlar", meeting.PointsOf(MeetingPointKind.Decision).Select(p => p.Text));
            var pending = meeting.PointsOf(MeetingPointKind.Pending).Select(p => p.Text).ToList();
            if (pending.Count > 0) Section("Bekleyen Konular", pending);
            Section("Aksiyon Maddeleri", meeting.OrderedActionItems.Select(a => a.Text));
            text.AppendLine($"Bu e-posta Briefyn AI tarafından {DisplayTime.Format(meeting.CreatedAtUtc, "dd MMMM yyyy HH:mm")} tarihli toplantı için oluşturulmuştur.");
            return text.ToString();

            void Section(string title, IEnumerable<string> items)
            {
                text.AppendLine(title);
                foreach (var item in items) text.AppendLine("- " + item);
                text.AppendLine();
            }
        }

        // Encodes only what HTML needs (<, >, &, quotes); Turkish letters stay as real UTF-8 text.
        // WebUtility.HtmlEncode turned "ç" into "&#231;", which made the mail look obfuscated to spam filters.
        private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

        private static string Item(string value) => Html.Encode(value);

        // Construct the responsive HTML email template using inline CSS for mail client compatibility.
        // S8: every meeting value comes from the LLM or from users, so all of it is HTML-encoded.
        public static string BuildSummaryHtml(Meeting meeting)
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
                            {Html.Encode(meeting.Title)}
                        </h2>

                        <div style=""margin-bottom: 30px; background-color: #f0f9ff; border-left: 4px solid #0ea5e9; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #475569; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">                                 
                                💬 Görüşülen Konular
                            </h3>
                            <ul style=""color: #334155; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.PointsOf(MeetingPointKind.Topic).Select(k => $"<li style='margin-bottom: 8px;'>{Item(k.Text)}</li>"))}
                            </ul>
                        </div>

                        <div style=""margin-bottom: 30px; background-color: #f0fdf4; border-left: 4px solid #22c55e; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #166534; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">
                                🎯 Alınan Kararlar
                            </h3>
                            <ul style=""color: #15803d; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.PointsOf(MeetingPointKind.Decision).Select(k => $"<li style='margin-bottom: 8px;'>{Item(k.Text)}</li>"))}
                            </ul>
                        </div>

                        {(meeting.PointsOf(MeetingPointKind.Pending).Any() ? $@"
                        <div style=""margin-bottom: 30px; background-color: #eff6ff; border-left: 4px solid #3b82f6; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #1d4ed8; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">Bekleyen Konular</h3>
                            <ul style=""color: #1e40af; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.PointsOf(MeetingPointKind.Pending).Select(k => $"<li style='margin-bottom: 8px;'>{Item(k.Text)}</li>"))}
                            </ul>
                        </div>" : "")}

                        <div style=""margin-bottom: 10px; background-color: #fff7ed; border-left: 4px solid #f97316; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #9a3412; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">
                                ⚡ Aksiyon Maddeleri
                            </h3>
                            <ul style=""color: #c2410c; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.OrderedActionItems.Select(a => $"<li style='margin-bottom: 8px;'>{Item(a.Text)}</li>"))}
                            </ul>
                        </div>

                    </div>

                    <div style=""background-color: #f8fafc; padding: 25px 20px; text-align: center; border-top: 1px solid #e2e8f0;"">
                        <p style=""color: #64748b; font-size: 13px; margin: 0; line-height: 1.5;"">
                            Bu e-posta <strong>Briefyn AI</strong> asistanı tarafından <br> {DisplayTime.Format(meeting.CreatedAtUtc, "dd MMMM yyyy HH:mm")} tarihinde otomatik olarak oluşturulmuştur.
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
