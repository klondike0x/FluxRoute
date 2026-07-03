# 08 — Тестирование

> **Навигация:** [AGENTS/README.md](README.md)  
> **Связанные файлы:** [04-ERROR-HANDLING.md](04-ERROR-HANDLING.md)

---

## 🧪 Обязательный минимум

- **≥80% покрытие** для новой бизнес-логики.
- **Каждый public метод** сервиса → минимум 3 теста: success, failure, edge case.
- **ViewModels** тестируем через подмену сервисов.

---

## 📐 Паттерн Arrange-Act-Assert

```csharp
public class DataServiceTests
{
    private readonly Mock<IHttpClientFactory> _clientFactoryMock;
    private readonly Mock<IOptions<ApiSettings>> _optionsMock;
    private readonly Mock<ILogger<DataService>> _loggerMock;
    private readonly DataService _service;

    public DataServiceTests()
    {
        _clientFactoryMock = new Mock<IHttpClientFactory>();
        _optionsMock = new Mock<IOptions<ApiSettings>>();
        _loggerMock = new Mock<ILogger<DataService>>();

        _optionsMock.Setup(x => x.Value)
            .Returns(new ApiSettings { BaseUrl = "https://api.test.com" });

        _service = new DataService(
            _clientFactoryMock.Object,
            _optionsMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetDataAsync_ValidResponse_ReturnsSuccess()
    {
        // Arrange
        var expectedData = new Data { Id = 1, Name = "Test" };
        var mockHandler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expectedData)
            });

        _clientFactoryMock.Setup(x => x.CreateClient("FluxApi"))
            .Returns(new HttpClient(mockHandler));

        // Act
        var result = await _service.GetDataAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(expectedData);
    }
}
```

---

## 🧪 Тестирование ViewModel

```
public class UserViewModelTests
{
    [Fact]
    public void SaveCommand_CanExecute_FalseWhenNameEmpty()
    {
        var serviceMock = new Mock<IUserService>();
        var vm = new UserViewModel(serviceMock.Object);

        vm.Name = "";
        vm.SaveCommand.CanExecute(null).Should().BeFalse();
    }
}
```

---

**Следующий файл:** [09-COMMON-MISTAKES.md](https://09-COMMON-MISTAKES.md)

