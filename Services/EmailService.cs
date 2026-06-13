using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace QuinielaApp.Services
{
    // ════════════════════════════════════════════════════
    //  EMAIL SERVICE — envío de correos vía SMTP (Gmail)
    // ════════════════════════════════════════════════════
    public class EmailService
    {
        private readonly IConfiguration _cfg;
        private readonly ILogger<EmailService> _log;

        public EmailService(IConfiguration cfg, ILogger<EmailService> log)
        { _cfg = cfg; _log = log; }

        public async Task SendPasswordResetAsync(string toEmail, string toName, string resetLink)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(
                    _cfg["Email:FromName"],
                    _cfg["Email:FromEmail"]!));
                message.To.Add(new MailboxAddress(toName, toEmail));
                message.Subject = "Recuperación de contraseña — Quiniela Mundial 2026";

                var body = new BodyBuilder();
                body.HtmlBody = $@"
                <div style='font-family:Arial,sans-serif;
                            max-width:500px;margin:0 auto'>
                  <div style='background:#1A1A1A;padding:20px;
                              text-align:center'>
                    <h2 style='color:#FFFFFF;margin:0;
                               font-size:20px'>
                      QUINIELA MUNDIAL 2026
                    </h2>
                  </div>
                  <div style='padding:24px;background:#F5F0E8'>
                    <p style='color:#1A1A1A;font-size:15px'>
                      Hola <strong>{toName}</strong>,
                    </p>
                    <p style='color:#555;font-size:14px'>
                      Recibimos una solicitud para restablecer
                      la contraseña de tu cuenta.
                      Haz clic en el botón para continuar:
                    </p>
                    <div style='text-align:center;margin:24px 0'>
                      <a href='{resetLink}'
                         style='background:#C8001E;color:#fff;
                                padding:12px 28px;border-radius:4px;
                                text-decoration:none;font-weight:700;
                                font-size:14px'>
                        Restablecer contraseña
                      </a>
                    </div>
                    <p style='color:#888;font-size:12px'>
                      Este enlace expira en 30 minutos.
                      Si no solicitaste este cambio
                      ignora este correo.
                    </p>
                  </div>
                  <div style='background:#1A1A1A;padding:12px;
                              text-align:center'>
                    <p style='color:rgba(255,255,255,.3);
                              font-size:11px;margin:0'>
                      Quiniela Mundial 2026
                    </p>
                  </div>
                </div>";

                message.Body = body.ToMessageBody();

                using var client = new SmtpClient();
                await client.ConnectAsync(
                    _cfg["Email:SmtpHost"]!,
                    int.Parse(_cfg["Email:SmtpPort"]!),
                    SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(
                    _cfg["Email:SmtpUser"]!,
                    _cfg["Email:SmtpPassword"]!);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error enviando email a {Email}", toEmail);
                throw;
            }
        }
    }
}
