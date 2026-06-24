using Xunit;
using FluentAssertions;
using Moq;
using Liuvis.Core.Interfaces;
using Liuvis.NLU.Services;
using Microsoft.Extensions.Logging;

namespace Liuvis.UnitTests.NLU;

public class NluServiceTests
{
    private readonly Mock<ILlmClient> _llmMock;
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly Mock<ILogger<NluService>> _loggerMock;
    private readonly NluService _sut;

    public NluServiceTests()
    {
        _llmMock = new Mock<ILlmClient>();
        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(x => x.GetPromptSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptSettings());
        _loggerMock = new Mock<ILogger<NluService>>();
        _sut = new NluService(_llmMock.Object, _settingsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ParseIntent_WithCreateIntent_ReturnsCreateIntentType()
    {
        // Arrange
        var llmResponse = @"{ ""Intent"": ""Create"", ""Confidence"": 0.95, ""Entities"": [{ ""Type"": ""object_type"", ""Value"": ""cylinder"", ""Start"": 0, ""End"": 8 }], ""Parameters"": {} }";
        _llmMock.Setup(x => x.CompleteWithThinkingAsync(It.IsAny<string>(), null, It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _sut.ParseIntent("Create a cylinder");

        // Assert
        result.IntentType.Should().Be(Core.Enums.IntentType.Create);
        result.Confidence.Should().Be(0.95);
        result.Entities.Should().HaveCount(1);
        result.Entities[0].Type.Should().Be("object_type");
    }

    [Fact]
    public async Task ParseIntent_WithEmptyInput_ReturnsUnknown()
    {
        // Arrange
        _llmMock.Setup(x => x.CompleteWithThinkingAsync(It.IsAny<string>(), null, It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"{ ""Intent"": ""Unknown"", ""Confidence"": 0.0, ""Entities"": [], ""Parameters"": {} }");

        // Act
        var result = await _sut.ParseIntent("");

        // Assert
        result.IntentType.Should().Be(Core.Enums.IntentType.Unknown);
    }
}
