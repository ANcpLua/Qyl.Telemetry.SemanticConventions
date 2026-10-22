namespace Qyl.Telemetry.SemanticConventions;

/// <summary>
/// The one identity of a data store a database span talks to: <c>db.system.name</c>,
/// <c>server.address</c>, <c>server.port</c> and <c>db.namespace</c>. Tests and the service map
/// compare stores by this value, so "the same database" means the same thing everywhere.
/// </summary>
/// <param name="System">The <c>db.system.name</c> value.</param>
/// <param name="ServerAddress">The <c>server.address</c> value.</param>
/// <param name="ServerPort">The <c>server.port</c> value.</param>
/// <param name="Namespace">The <c>db.namespace</c> value.</param>
[JetBrains.Annotations.PublicAPI]
public readonly record struct StoreIdentity(string? System, string? ServerAddress, long? ServerPort, string? Namespace)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"{System}://{ServerAddress}{(ServerPort is { } port ? ":" + port.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : string.Empty)}/{Namespace}";
}
