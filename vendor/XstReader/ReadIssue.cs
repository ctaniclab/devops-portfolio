namespace XstReader;

public sealed record ReadIssue(string NodeId, bool MessageSkipped, string Reason, string Details);
