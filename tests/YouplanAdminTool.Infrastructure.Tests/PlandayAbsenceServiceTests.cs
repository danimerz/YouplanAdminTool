using System.Net;
using System.Text;
using YouplanAdminTool.Core.Abstractions;
using YouplanAdminTool.Core.Models;
using YouplanAdminTool.Infrastructure.Absence;

namespace YouplanAdminTool.Infrastructure.Tests;

public class PlandayAbsenceServiceTests
{
    private const string AccountTypesSinglePage = """
        {
            "data": [
                { "id": 10, "name": "Urlaub", "absenceType": "Vacation" }
            ],
            "paging": { "offset": 0, "limit": 100, "total": 1 }
        }
        """;

    [Fact]
    public async Task MapsAbsenceRequestAndResolvesAccountTypeFromAbsenceType()
    {
        var requestsJson = """
            {
                "data": [
                    {
                        "id": 501,
                        "employeeId": 42,
                        "note": "Sommerurlaub",
                        "absencePeriod": { "start": "2026-07-10", "end": "2026-07-20" },
                        "status": "Approved",
                        "requestedAccounts": [ { "id": 10, "name": "Urlaub" } ]
                    }
                ],
                "paging": { "offset": 0, "limit": 100, "total": 1 }
            }
            """;

        var handler = new StubHttpMessageHandler(request => JsonResponse(
            request.RequestUri!.ToString().Contains("accounttypes") ? AccountTypesSinglePage : requestsJson));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openapi.planday.test") };
        var sut = new PlandayAbsenceService(httpClient);

        var query = new AbsenceRequestQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var result = await sut.GetAbsenceRequestsAsync(query);

        var request = Assert.Single(result);
        Assert.Equal(501, request.Id);
        Assert.Equal(42, request.EmployeeId);
        Assert.Equal(AbsenceRequestStatus.Approved, request.Status);
        Assert.Equal(new DateOnly(2026, 7, 10), request.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 20), request.EndDate);
        Assert.Equal(AbsenceType.Vacation, request.PrimaryAbsenceType);
    }

    [Fact]
    public async Task FollowsPaginationUntilAllPagesFetched()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("accounttypes"))
            {
                return JsonResponse(AccountTypesSinglePage);
            }

            callCount++;
            var offset = callCount == 1 ? 0 : 100;
            var id = callCount == 1 ? 1 : 2;
            var json = $$"""
                {
                    "data": [
                        {
                            "id": {{id}},
                            "employeeId": 1,
                            "absencePeriod": { "start": "2026-07-01", "end": "2026-07-02" },
                            "status": "Approved",
                            "requestedAccounts": []
                        }
                    ],
                    "paging": { "offset": {{offset}}, "limit": 100, "total": 150 }
                }
                """;
            return JsonResponse(json);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openapi.planday.test") };
        var sut = new PlandayAbsenceService(httpClient);

        var query = new AbsenceRequestQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var result = await sut.GetAbsenceRequestsAsync(query);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, callCount);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
