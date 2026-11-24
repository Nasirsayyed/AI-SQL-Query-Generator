namespace AIQueryGenerator.Shared.Security;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Developer = "Developer";
    public const string Analyst = "Analyst";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, Developer, Analyst, Viewer];

    /// <summary>Roles that only a Super Admin may grant or revoke.</summary>
    public static readonly IReadOnlySet<string> Privileged = new HashSet<string> { SuperAdmin, Admin };
}
