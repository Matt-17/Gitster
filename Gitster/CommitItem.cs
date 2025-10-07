namespace Gitster;

public class CommitItem
{
    public string Message { get; set; }
    public string Date { get; set; }
    public string CommitId { get; set; }

    public CommitItem(string message, string date, string commitId)
    {
        Message = message;
        Date = date;
        CommitId = commitId;
    }
}
