namespace YouplanAdminTool.Core.Models;

public sealed record Employee(long Id, string FirstName, string LastName, IReadOnlyList<long> DepartmentIds)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
}
