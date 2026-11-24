using AIQueryGenerator.Domain.Enums;
using FluentValidation;

namespace AIQueryGenerator.Application.Connections;

public sealed class SaveConnectionRequestValidator : AbstractValidator<SaveConnectionRequest>
{
    // Server names: host, host\instance, host,port, tcp:host,port. No connection-string syntax characters.
    private const string ServerPattern = @"^(tcp:)?[A-Za-z0-9\.\-_]+(\\[A-Za-z0-9_\-\$]+)?(,\d{1,5})?$";
    private const string IdentifierPattern = @"^[^;=\[\]'""\r\n\0]+$";

    public SaveConnectionRequestValidator()
    {
        RuleFor(x => x.ConnectionName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ServerName).NotEmpty().MaximumLength(256).Matches(ServerPattern)
            .WithMessage("Server must be a host name, host\\instance or host,port. Connection strings are not accepted.");
        RuleFor(x => x.DatabaseName).NotEmpty().MaximumLength(128).Matches(IdentifierPattern)
            .WithMessage("Database name contains invalid characters.");
        RuleFor(x => x.AuthenticationType).IsInEnum();
        RuleFor(x => x.Username).NotEmpty().MaximumLength(128).Matches(IdentifierPattern)
            .When(x => x.AuthenticationType == SqlAuthenticationType.SqlPassword)
            .WithMessage("A username without connection-string characters is required for SQL authentication.");
        RuleFor(x => x.Password).MaximumLength(512);
        RuleFor(x => x.ConnectionTimeoutSeconds).InclusiveBetween(1, 120);
        RuleFor(x => x.CommandTimeoutSeconds).InclusiveBetween(1, 600);
        RuleFor(x => x.MaxRows).InclusiveBetween(1, 100_000);
    }
}

public sealed class UpdateAllowedObjectsRequestValidator : AbstractValidator<UpdateAllowedObjectsRequest>
{
    public UpdateAllowedObjectsRequestValidator()
    {
        RuleFor(x => x.Rules).NotNull().Must(r => r.Count <= 5000).WithMessage("Too many rules.");
        RuleForEach(x => x.Rules).ChildRules(rule =>
        {
            rule.RuleFor(r => r.SchemaName).NotEmpty().MaximumLength(128);
            rule.RuleFor(r => r.ObjectName).NotEmpty().MaximumLength(128).When(r => r.ObjectType != DbObjectType.Schema);
            rule.RuleFor(r => r.ColumnName).NotEmpty().MaximumLength(128).When(r => r.ObjectType == DbObjectType.Column);
            rule.RuleFor(r => r.ObjectType).IsInEnum();
            rule.RuleFor(r => r.AccessLevel).IsInEnum();
        });
    }
}
