using System.Threading.Channels;
using System.Net.Mail;
namespace FixPoint.Api.Services;
public class MailQueue(IConfiguration config, ILogger<MailQueue> logger) : BackgroundService {
 private readonly Channel<(string Email,string Token)> queue=Channel.CreateUnbounded<(string,string)>();
 public void Enqueue(string email,string token)=>queue.Writer.TryWrite((email,token));
 protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
 await foreach(var item in queue.Reader.ReadAllAsync(stoppingToken)) { try {
 using var smtp=new SmtpClient(config["Mail:Host"]??"mail",int.Parse(config["Mail:Port"]??"1025"));
 using var message=new MailMessage(config["Mail:From"]??"fixpoint@example.test",item.Email,"FixPoint: новая смета",$"Откройте смету: {config["Mail:FrontendUrl"]??"http://localhost:5173"}/?token={item.Token}");
 await smtp.SendMailAsync(message);
 } catch(Exception ex) {logger.LogError(ex,"Не удалось отправить письмо");} }
 }
}
