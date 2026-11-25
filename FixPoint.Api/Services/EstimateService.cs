using System.Security.Cryptography;
using System.Text;
using FixPoint.Api.Data;
using FixPoint.Api.DTOs;
using FixPoint.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FixPoint.Api.Services;

public class EstimateService
{
    private readonly AppDbContext _context;
    private readonly MailQueue _mail;

    public EstimateService(AppDbContext context, MailQueue mail)
    {
        _context = context; _mail=mail;
    }

    public async Task<string> CreateEstimateAsync(CreateEstimateDto dto, int masterId)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var request = await _context.RepairRequests.FindAsync(dto.RequestId);
        if (request == null) throw new Exception("Заявка не найдена");
        if (request.MasterId != masterId) throw new Exception("Вы не назначены на эту заявку");

        // Генерируем случайный токен
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = HashToken(rawToken);

        var previous = await _context.RepairEstimates.Where(e => e.RequestId == dto.RequestId).ToListAsync();
        if (!new[] { "Assigned", "Diagnosing", "RevisionRequested", "Approved", "AwaitingApproval" }.Contains(request.Status) || dto.Items.Count == 0 || dto.Items.Count > 100 || dto.DurationDays < 1 || dto.Items.Any(i=>string.IsNullOrWhiteSpace(i.Name)||i.Quantity<1||i.UnitPrice<0||i.UnitPrice>10000000)) throw new Exception("Недопустимая смета");
        foreach (var old in previous) { old.Status = "Superseded"; old.ApprovalTokenHash = null; }
        var estimate = new RepairEstimate
        {
            RequestId = dto.RequestId,
            TotalPrice = dto.Items.Sum(i=>i.Quantity*i.UnitPrice),
            ItemsJson = System.Text.Json.JsonSerializer.Serialize(dto.Items, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            DurationDays = dto.DurationDays,
            Description = dto.Description,
            Version = previous.Count == 0 ? 1 : previous.Max(e => e.Version) + 1,
            Status = "Sent",
            ApprovalTokenHash = tokenHash,
            TokenExpiresAt = DateTime.UtcNow.AddDays(3)
        };

        _context.RepairEstimates.Add(estimate);
        
        request.Status = "AwaitingApproval";
        request.UpdatedAt = DateTime.UtcNow;
        
        _context.Notifications.Add(new Notification {UserId=request.ClientId,Message=$"Новая смета по заявке #{request.Id}"});
        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        var client=await _context.Users.FindAsync(request.ClientId);
        if(client!=null) _mail.Enqueue(client.Email,rawToken);

        // В реальном проекте здесь мы бы отправили Email.
        // Пока просто возвращаем токен для теста.
        return rawToken; 
    }

    public string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}