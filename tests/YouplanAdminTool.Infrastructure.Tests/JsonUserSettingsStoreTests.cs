using MsOptions = Microsoft.Extensions.Options.Options;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Options;
using YouplanAdminTool.Infrastructure.Persistence;

namespace YouplanAdminTool.Infrastructure.Tests;

public class JsonUserSettingsStoreTests : IDisposable
{
    private readonly string _tempFilePath = Path.Combine(Path.GetTempPath(), $"youplan-settings-{Guid.NewGuid()}.json");

    private JsonUserSettingsStore CreateSut() =>
        new(MsOptions.Create(new AppOptions { UserSettingsFileName = _tempFilePath }));

    [Fact]
    public async Task ReturnsEmptySettingsWhenFileDoesNotExist()
    {
        var sut = CreateSut();

        var result = await sut.LoadAsync();

        Assert.Equal(UserSettings.Empty, result);
    }

    [Fact]
    public async Task SavedSettingsCanBeLoadedBack()
    {
        var sut = CreateSut();
        var settings = new UserSettings(30, AbsenceType.Vacation, 42, 99);

        await sut.SaveAsync(settings);
        var loaded = await sut.LoadAsync();

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task SavingTwiceOverwritesPreviousValue()
    {
        var sut = CreateSut();

        await sut.SaveAsync(new UserSettings(15, null, null, null));
        await sut.SaveAsync(new UserSettings(60, AbsenceType.Flextime, 7, 3));
        var loaded = await sut.LoadAsync();

        Assert.Equal(new UserSettings(60, AbsenceType.Flextime, 7, 3), loaded);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }
}
