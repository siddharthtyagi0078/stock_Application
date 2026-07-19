using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using MailKit.Security;
using MailKit.Net.Smtp;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace StockWebApplications.Controllers
{
    public class DailyMail : Controller
    {
        public IActionResult Index()
        {
            string fullUrl = "https://trendlyne.com/research-reports/broker/Motilal%20Oswal/?";
            fullUrl = "https://s.ifuturecorporation.com/FiiDaily";
            var response = CallUrl(fullUrl).Result;

            var strdate = DateTime.Now.AddDays(-1).ToString("dd-MMM-yyyy");
            SendMail("siddharth.tyagi@hotmail.com", "FII Daily Report- "+ strdate, response);
            return View();
        }

        private static async Task<string> CallUrl(string fullUrl)
        {
            HttpClient client = new HttpClient();
            //  ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;
            client.DefaultRequestHeaders.Accept.Clear();
            var response = client.GetStringAsync(fullUrl);
            return await response;
        }

        public void SendMail(string to, string subject, string message1)
        {
            // System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("NoReply", "postmaster@sandbox58b3e788328f49128a88c48f37ff939e.mailgun.org"));
            message.To.Add(new MailboxAddress(to, to));
            message.Bcc.Add(new MailboxAddress("Vijay", "vijindira2060@gmail.com"));
            message.Bcc.Add(new MailboxAddress("sid", "sid.saldi@gmail.com"));
            message.Bcc.Add(new MailboxAddress("Manoj", "tyagimanoj12@gmail.com"));
            //   message.Bcc.Add(new MailboxAddress("siddharth Tyagi", "siddharth.tyagi@hotmail.com"));


            message.Subject = subject;

            var body = new TextPart("html")
            {
                Text = message1
            };

            message.Body = body;

            using (var client = new SmtpClient())
            {
                client.Connect("smtp.mailgun.org", 587, SecureSocketOptions.StartTls);
                client.Authenticate("postmaster@sandbox58b3e788328f49128a88c48f37ff939e.mailgun.org", "2700658f08ef088a425b651537b1cbd7-3d4b3a2a-708a99c4");
                client.Send(message);
                client.Disconnect(true);
            }
        }
    }
}
