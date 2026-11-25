using System.Security.Claims;
using FixPoint.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FixPoint.Api.Controllers;
[ApiController, Route("api/workspace"), Authorize]
public class WorkspaceController(AppDbContext db) : ControllerBase {
 int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
 [HttpGet("requests")]
 public async Task<IActionResult> Requests() {
 var q=db.RepairRequests.AsQueryable();
 if(User.IsInRole("Master")) q=q.Where(r=>r.MasterId==UserId); else if(!User.IsInRole("Manager")) q=q.Where(r=>r.ClientId==UserId);
 return Ok(await q.OrderByDescending(r=>r.Id).Select(r=>new {r.Id,r.Status,r.Description,r.Diagnosis,History=db.StatusEvents.Where(h=>h.RequestId==r.Id).OrderBy(h=>h.Id),r.CreatedAt,r.MasterId,Device=new {r.Device.Type,r.Device.Brand,r.Device.Model,r.Device.SerialNumber},Estimates=r.Estimates.Select(e=>new {e.Id,e.Version,e.Status,e.TotalPrice,e.Description,e.ItemsJson,e.DurationDays,Decisions=db.DecisionEvents.Where(d=>d.EstimateId==e.Id).OrderBy(d=>d.Id),Feedback=db.EstimateFeedbacks.Where(f=>f.EstimateId==e.Id).OrderBy(f=>f.CreatedAt).Select(f=>new {f.AuthorRole,f.Type,f.Message,f.CreatedAt})})}).ToListAsync()); }
 [HttpGet("masters"),Authorize(Roles="Manager")]
 public async Task<IActionResult> Masters()=>Ok(await db.Users.Where(u=>u.Role=="Master").Select(u=>new {u.Id,u.FullName}).ToListAsync());
 [HttpGet("services"),Authorize(Roles="Admin,Master")]
 public async Task<IActionResult> Services()=>Ok(await db.RepairServices.ToListAsync());
 [HttpGet("notifications")] public async Task<IActionResult> Notifications()=>Ok(await db.Notifications.Where(n=>n.UserId==UserId).OrderByDescending(n=>n.Id).ToListAsync());
 [HttpPost("notifications/{id}/read")] public async Task<IActionResult> Read(int id){var n=await db.Notifications.FindAsync(id);if(n==null||n.UserId!=UserId)return NotFound();n.IsRead=true;await db.SaveChangesAsync();return Ok();}
 public record ClientActionDto(string Type,string Message="");
 [HttpPost("estimates/{id}/action"),Authorize(Roles="Client")]
 public async Task<IActionResult> ClientAction(int id,ClientActionDto dto){
 await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
 var e=await db.RepairEstimates.Include(x=>x.Request).FirstOrDefaultAsync(x=>x.Id==id && x.Request.ClientId==UserId);
 if(e==null)return NotFound();if(!new[]{"Sent","RevisionRequested"}.Contains(e.Status)||e.TokenExpiresAt<DateTime.UtcNow)return BadRequest("Смета недоступна");
 if(!new[]{"Approved","Rejected","Question","Comment","RevisionRequest"}.Contains(dto.Type)||dto.Message.Length>2000||(dto.Type!="Approved"&&string.IsNullOrWhiteSpace(dto.Message)))return BadRequest("Проверьте сообщение");
 if(dto.Type=="Approved"&&e.Status!="Sent")return BadRequest("Ожидается новая версия");
 if(dto.Type is "Approved" or "Rejected"){e.Status=dto.Type;e.Request.Status=dto.Type;e.ApprovalTokenHash=null;db.DecisionEvents.Add(new FixPoint.Api.Models.DecisionEvent{EstimateId=id,Decision=dto.Type,Comment=dto.Message});}
 if(dto.Type!="Approved")db.EstimateFeedbacks.Add(new FixPoint.Api.Models.EstimateFeedback{EstimateId=id,AuthorId=UserId,AuthorRole="Client",Type=dto.Type=="Rejected"?"RejectionReason":dto.Type,Message=dto.Message});
 if(dto.Type=="RevisionRequest"){e.Status="RevisionRequested";e.Request.Status="RevisionRequested";}
 if(e.Request.MasterId is int master)db.Notifications.Add(new FixPoint.Api.Models.Notification{UserId=master,Message=$"Действие клиента по смете #{id}: {dto.Type}"});
 await db.SaveChangesAsync();await tx.CommitAsync();return Ok();}
 public record DiagnosisDto(string Diagnosis);
 [HttpPut("requests/{id}/diagnosis"),Authorize(Roles="Master")] public async Task<IActionResult> Diagnosis(int id,DiagnosisDto dto){var r=await db.RepairRequests.FindAsync(id);if(r==null||r.MasterId!=UserId)return NotFound();if(r.Status!="Diagnosing"||string.IsNullOrWhiteSpace(dto.Diagnosis))return BadRequest("Сначала начните диагностику");r.Diagnosis=dto.Diagnosis;await db.SaveChangesAsync();return Ok();}
 public record StatusDto(string Status);
 [HttpPost("requests/{id}/status"),Authorize(Roles="Master")]
 public async Task<IActionResult> Status(int id,StatusDto dto){var r=await db.RepairRequests.FindAsync(id);if(r==null||r.MasterId!=UserId)return NotFound(); var next=r.Status switch {"Assigned"=>"Diagnosing","Approved"=>"InRepair","InRepair"=>"Ready","Ready"=>"Completed",_=>""};if(next!=dto.Status||next=="")return BadRequest("Недопустимый переход");r.Status=next;r.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync();return Ok(new {message="Статус обновлён"});}
}
