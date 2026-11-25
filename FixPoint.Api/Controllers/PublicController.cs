using FixPoint.Api.Data;
using FixPoint.Api.Services;
using FixPoint.Api.DTOs;
using FixPoint.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixPoint.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public")]
[AllowAnonymous] // Разрешаем всем
public class PublicController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EstimateService _estimateService;

    public PublicController(AppDbContext context, EstimateService estimateService)
    {
        _context = context;
        _estimateService = estimateService;
    }

    // 1. Гость смотрит смету
    [HttpGet("estimate/{token}")]
    public async Task<IActionResult> ViewEstimate(string token)
    {
        var tokenHash = _estimateService.HashToken(token);
        var estimate = await _context.RepairEstimates
            .Include(e => e.Request)
            .FirstOrDefaultAsync(e => e.ApprovalTokenHash == tokenHash);

        if (estimate == null) return NotFound("Смета не найдена или ссылка недействительна");
        if (estimate.TokenExpiresAt < DateTime.UtcNow) return BadRequest("Срок действия ссылки истек");

        return Ok(new {
            id = estimate.Id,
            requestId = estimate.RequestId,
            description = estimate.Description, version = estimate.Version,
            totalPrice = estimate.TotalPrice, estimate.ItemsJson, estimate.DurationDays,
            status = estimate.Status
        });
    }

    // 2. Гость согласовывает смету
    [HttpPost("estimate/{token}/approve")]
    public async Task<IActionResult> ApproveEstimate(string token)
    {
        var tokenHash = _estimateService.HashToken(token);
        var estimate = await _context.RepairEstimates
            .Include(e => e.Request)
            .FirstOrDefaultAsync(e => e.ApprovalTokenHash == tokenHash);

        if (estimate == null) return NotFound("Смета не найдена");
        if (estimate.TokenExpiresAt < DateTime.UtcNow) return BadRequest("Срок действия ссылки истек");
        if (estimate.Status != "Sent") return BadRequest("Смета уже обработана");

        _context.DecisionEvents.Add(new DecisionEvent {EstimateId=estimate.Id,Decision="Approved"});
        estimate.Status = "Approved";
        estimate.Request.Status = "Approved";
        
        // Токен можно "сжечь" (обнулить), чтобы по нему больше нельзя было зайти
        estimate.ApprovalTokenHash = null; 
        
        await _context.SaveChangesAsync();

        return Ok(new { message = "Ремонт согласован!" });
    }
    // 3. Гость оставляет обратную связь
[HttpPost("estimate/{token}/feedback")]
public async Task<IActionResult> LeaveFeedback(string token, [FromBody] CreateFeedbackDto dto)
{
    var tokenHash = _estimateService.HashToken(token);
    var estimate = await _context.RepairEstimates
        .Include(e => e.Request)
        .FirstOrDefaultAsync(e => e.ApprovalTokenHash == tokenHash);

    if (estimate == null) return NotFound("Смета не найдена или ссылка устарела");
    if (estimate.TokenExpiresAt < DateTime.UtcNow) return BadRequest("Срок действия ссылки истек");
    if (estimate.Status != "Sent" && estimate.Status != "RevisionRequested") 
        return BadRequest("По этой смете уже принято окончательное решение");

    if ((string.IsNullOrWhiteSpace(dto.Message) || dto.Message.Length>2000))
        return BadRequest("Сообщение не может быть пустым");

    if(!new[]{"Question","Comment","RevisionRequest"}.Contains(dto.Type)) return BadRequest("Недопустимый тип сообщения");
    if(estimate.Request.MasterId is int master) _context.Notifications.Add(new Notification {UserId=master,Message=$"Обратная связь по смете #{estimate.Id}"});
    var feedback = new EstimateFeedback
    {
        EstimateId = estimate.Id,
        AuthorRole = "Client",
        Type = dto.Type,
        Message = dto.Message
    };

    _context.EstimateFeedbacks.Add(feedback);

    // Если клиент просит пересмотр, меняем статус заявки
    if (dto.Type == "RevisionRequest")
    {
        estimate.Status = "RevisionRequested";
        estimate.Request.Status = "RevisionRequested";
    }

    await _context.SaveChangesAsync();
    return Ok(new { message = "Обратная связь сохранена" });
}

// 4. Просмотр треда обратной связи
[HttpGet("estimate/{token}/feedbacks")]
public async Task<IActionResult> GetFeedbacks(string token)
{
    var tokenHash = _estimateService.HashToken(token);
    var estimate = await _context.RepairEstimates
        .FirstOrDefaultAsync(e => e.ApprovalTokenHash == tokenHash);

    if (estimate == null || estimate.TokenExpiresAt<DateTime.UtcNow) return NotFound("Смета не найдена");

    var feedbacks = await _context.EstimateFeedbacks
        .Where(f => f.EstimateId == estimate.Id)
        .OrderBy(f => f.CreatedAt)
        .Select(f => new { f.AuthorRole, f.Type, f.Message, f.CreatedAt })
        .ToListAsync();

    return Ok(feedbacks);
}
[HttpPost("estimate/{token}/reject")]
public async Task<IActionResult> Reject(string token, CreateFeedbackDto dto) {
 var hash = _estimateService.HashToken(token);
 var e = await _context.RepairEstimates.Include(x=>x.Request).FirstOrDefaultAsync(x=>x.ApprovalTokenHash==hash);
 if(e==null || e.TokenExpiresAt<DateTime.UtcNow || !new[]{"Sent","RevisionRequested"}.Contains(e.Status) || (string.IsNullOrWhiteSpace(dto.Message) || dto.Message.Length>2000)) return BadRequest("Недействительная ссылка или отсутствует причина");
 _context.DecisionEvents.Add(new DecisionEvent {EstimateId=e.Id,Decision="Rejected",Comment=dto.Message});
 e.Status="Rejected"; e.Request.Status="Rejected"; e.ApprovalTokenHash=null;
 _context.EstimateFeedbacks.Add(new EstimateFeedback { EstimateId=e.Id, AuthorRole="Client", Type="RejectionReason", Message=dto.Message }); await _context.SaveChangesAsync(); return Ok(new {message="Смета отклонена"});
}
}