namespace FixPoint.Api.Models;
public class StatusEvent { public int Id {get;set;} public int RequestId {get;set;} public string OldStatus {get;set;}=""; public string NewStatus {get;set;}=""; public DateTime CreatedAt {get;set;}=DateTime.UtcNow; }
public class DecisionEvent { public int Id {get;set;} public int EstimateId {get;set;} public string Decision {get;set;}=""; public string Comment {get;set;}=""; public DateTime CreatedAt {get;set;}=DateTime.UtcNow; }
public class Notification { public int Id {get;set;} public int UserId {get;set;} public string Message {get;set;}=""; public bool IsRead {get;set;} public DateTime CreatedAt {get;set;}=DateTime.UtcNow; }
