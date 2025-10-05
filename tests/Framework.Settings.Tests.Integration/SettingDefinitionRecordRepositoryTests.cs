using Framework.Settings;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Framework.Settings.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class SettingDefinitionRecordRepositoryTests(SettingsTestFixture fixture) : SettingsTestBase(fixture)
{
    private static readonly List<SettingDefinition> _Definition = TestData.CreateDefinitionFaker().Generate(2);

    [Fact]
    public async Task should_save_defined_settings()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddSettingDefinitionProvider<SettingDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISettingDefinitionRecordRepository>();
        var serializer = scope.ServiceProvider.GetRequiredService<ISettingDefinitionSerializer>();

        // pre conditions: no definitions
        var definitions = await repository.GetListAsync(AbortToken);
        definitions.Should().BeEmpty();

        // when: save definitions
        var addedRecords = serializer.Serialize(_Definition);
        await repository.SaveAsync(addedRecords, [], [], AbortToken);

        // then: definitions saved
        definitions = await repository.GetListAsync(AbortToken);
        definitions.Should().HaveCount(2);
        definitions.Should().BeEquivalentTo(addedRecords);

        // when: delete definitions
        await repository.SaveAsync([], [], [addedRecords[0]], AbortToken);

        // then: definitions deleted
        addedRecords.RemoveAt(0);
        definitions = await repository.GetListAsync(AbortToken);
        definitions.Should().ContainSingle();
        definitions.Should().BeEquivalentTo(addedRecords);

        // when: update definitions
        addedRecords[0].DisplayName = "Updated";
        await repository.SaveAsync([], [addedRecords[0]], [], AbortToken);

        // then: definitions updated
        definitions = await repository.GetListAsync(AbortToken);
        definitions.Should().ContainSingle();
        definitions.Should().BeEquivalentTo(addedRecords);
    }

    [UsedImplicitly]
    private sealed class SettingDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            foreach (var definition in _Definition)
            {
                context.Add(definition);
            }
        }
    }
}
