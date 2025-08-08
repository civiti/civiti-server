namespace Civica.Api.Models.Responses.Admin;

public class BulkApproveResponse
{
    public int TotalRequested { get; set; }
    public int SuccessfullyApproved { get; set; }
    public int Failed { get; set; }
    public string? Message { get; set; }
    public List<BulkApproveResult> Results { get; set; } = [];
}

public class BulkApproveResult
{
    public Guid IssueId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
}