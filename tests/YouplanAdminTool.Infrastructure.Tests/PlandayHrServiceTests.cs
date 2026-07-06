using System.Net;
using System.Text;
using YouplanAdminTool.Infrastructure.Hr;

namespace YouplanAdminTool.Infrastructure.Tests;

public class PlandayHrServiceTests
{
    private const string EmployeesSinglePage = """
        {
            "data": [
                { "id": 42, "firstName": "Maria", "lastName": "Muster", "departments": [7] }
            ],
            "paging": { "offset": 0, "limit": 50, "total": 1 }
        }
        """;

    private const string DepartmentsSinglePage = """
        {
            "data": [
                { "id": 7, "name": "Verkauf" }
            ],
            "paging": { "offset": 0, "limit": 50, "total": 1 }
        }
        """;

    [Fact]
    public async Task MapsEmployeeFields()
    {
        var handler = StubHttpMessageHandler.ReturningJson(EmployeesSinglePage);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openapi.planday.test") };
        var sut = new PlandayHrService(httpClient);

        var result = await sut.GetEmployeesAsync();

        var employee = Assert.Single(result);
        Assert.Equal(42, employee.Id);
        Assert.Equal("Maria Muster", employee.FullName);
        Assert.Equal([7L], employee.DepartmentIds);
    }

    [Fact]
    public async Task SecondCallWithinCacheDurationDoesNotHitHttpAgain()
    {
        var handler = StubHttpMessageHandler.ReturningJson(EmployeesSinglePage);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openapi.planday.test") };
        var sut = new PlandayHrService(httpClient);

        await sut.GetEmployeesAsync();
        await sut.GetEmployeesAsync();

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EmployeesAndDepartmentsAreCachedIndependently()
    {
        var handler = new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri!.ToString().Contains("departments") ? DepartmentsSinglePage : EmployeesSinglePage,
                Encoding.UTF8,
                "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openapi.planday.test") };
        var sut = new PlandayHrService(httpClient);

        await sut.GetEmployeesAsync();
        await sut.GetDepartmentsAsync();
        await sut.GetEmployeesAsync();
        await sut.GetDepartmentsAsync();

        Assert.Equal(2, handler.RequestCount);
    }
}
