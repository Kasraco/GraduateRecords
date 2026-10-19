namespace GraduateRecords.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class WebApplicationCollection : ICollectionFixture<GraduateRecordsWebApplicationFactory>
{
    public const string Name = "WebApplication";
}
