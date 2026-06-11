using System.Net;
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

            // Construct the responsive HTML email template using inline CSS for mail client compatibility.
            string htmlBody = $@"
            <div style=""background-color: #f4f7f6; padding: 40px 20px; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;"">
                <div style=""max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 20px rgba(0,0,0,0.05); overflow: hidden;"">
                    
                    <div style=""background: linear-gradient(135deg, #2563eb, #1d4ed8); padding: 35px 20px; text-align: center;"">
                        <h1 style=""color: #ffffff; margin: 0; font-size: 28px; font-weight: 600; letter-spacing: 0.5px;"">Briefyn AI</h1>
                        <p style=""color: #bfdbfe; margin: 10px 0 0 0; font-size: 15px;"">Akıllı Toplantı ve Karar Raporu</p>
                    </div>

                    <div style=""padding: 35px 30px;"">
                        <h2 style=""color: #1e293b; font-size: 22px; margin-top: 0; margin-bottom: 30px; border-bottom: 2px solid #f1f5f9; padding-bottom: 15px;"">
                            {meeting.ToplantiKonusu}
                        </h2>

                        <div style=""margin-bottom: 30px; background-color: #f0f9ff; border-left: 4px solid #0ea5e9; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #475569; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">                                 
                                💬 Görüşülen Konular
                            </h3>
                            <ul style=""color: #334155; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.GorusulenKonular.Select(k => $"<li style='margin-bottom: 8px;'>{k.Split("|~|")[0]}</li>"))}
                            </ul>
                        </div>

                        <div style=""margin-bottom: 30px; background-color: #f0fdf4; border-left: 4px solid #22c55e; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #166534; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">
                                🎯 Alınan Kararlar
                            </h3>
                            <ul style=""color: #15803d; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.AlinanKararlar.Select(k => $"<li style='margin-bottom: 8px;'>{k.Split("|~|")[0]}</li>"))}
                            </ul>
                        </div>

                        <div style=""margin-bottom: 10px; background-color: #fff7ed; border-left: 4px solid #f97316; padding: 20px; border-radius: 6px;"">
                            <h3 style=""color: #9a3412; font-size: 16px; margin-top: 0; margin-bottom: 12px;"">
                                ⚡ Aksiyon Maddeleri
                            </h3>
                            <ul style=""color: #c2410c; line-height: 1.7; padding-left: 20px; margin: 0; font-size: 15px;"">
                                {string.Join("", meeting.AksiyonMaddeleri.Select(a => $"<li style='margin-bottom: 8px;'>{a.Text.Split("|~|")[0]}</li>"))}
                            </ul>
                        </div>

                    </div>

                    <div style=""background-color: #f8fafc; padding: 25px 20px; text-align: center; border-top: 1px solid #e2e8f0;"">
                        <p style=""color: #64748b; font-size: 13px; margin: 0; line-height: 1.5;"">
                            Bu e-posta <strong>Briefyn AI</strong> asistanı tarafından <br> {meeting.CreatedDate:dd MMMM yyyy HH:mm} tarihinde otomatik olarak oluşturulmuştur.
                        </p>
                        <p style=""color: #94a3b8; font-size: 12px; margin-top: 15px; margin-bottom: 0;"">
                            © {DateTime.Now.Year} Briefyn AI. Tüm hakları saklıdır.
                        </p>
                    </div>

                </div>
            </div>";

            // Compose and dispatch the mail message.
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_emailSettings.SenderEmail, _emailSettings.SenderName),
                Subject = $"Briefyn AI Raporu: {meeting.ToplantiKonusu}",
                IsBodyHtml = true,
                Body = htmlBody
            };

            mailMessage.To.Add(toEmail);

            await smtpClient.SendMailAsync(mailMessage);
        }
    }
}