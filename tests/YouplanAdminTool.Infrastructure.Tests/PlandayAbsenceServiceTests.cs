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

    // requestedAccounts[].id referenziert eine Konto-INSTANZ (id=500), nicht die Kontoart direkt.
    // Erst über deren typeId (=10) wird die Kontoart ("Urlaub"/Vacation) aufgelöst.
    private const string AccountsSinglePage = """
        {
            "data": [
                { "id": 500, "employeeId": 42, "typeId": 10 }
            ],
            "paging": { "offset": 0, "limit": 100, "total": 1 }
        }
        """;

    [Fact]
    public async Task MapsAbsenceRequestAndResolvesAbsenceTypeViaAccountIndirection()
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
                        "requestedAccounts": [ { "id": 500, "name": "Urlaub" } ]
                    }
                ],
                "paging": { "offset": 0, "limit": 100, "total": 1 }
            }
            """;

        var handler = new StubHttpMessageHandler(request => JsonResponse(RouteResponse(request, requestsJson)));
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
    public async Task FallsBackToUnknownWhenAccountCannotBeResolved()
    {
        var requestsJson = """
            {
                "data": [
                    {
                        "id": 501,
                        "employeeId": 42,
                        "absencePeriod": { "start": "2026-07-10", "end": "2026-07-20" },
                        "status": "Approved",
                        "requestedAccounts": [ { "id": 999999, "name": "Unbekanntes Konto" } ]
                    }
                ],
                "paging": { "offset": 0, "limit": 100, "total": 1 }
            }
            """;

        var handler = new StubHttpMessageHandler(request => JsonResponse(RouteResponse(request, requestsJson)));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openapi.planday.test") };
        var sut = new PlandayAbsenceService(httpClient);

        var query = new AbsenceRequestQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var result = await sut.GetAbsenceRequestsAsync(query);

        Assert.Equal(AbsenceType.Unknown, Assert.Single(result).PrimaryAbsenceType);
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

            if (url.Contains("absence/v1.0/accounts"))
            {
                return JsonResponse(AccountsSinglePage);
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

    [Fact]
    public async Task ReusesAccountsCacheForSameDateRangeButRefetchesForDifferentRange()
    {
        var accountsCallCount = 0;
        var requestsJson = """
            {
                "data": [
                    {
                        "id": 501,
                        "employeeId": 42,
                        "absencePeriod": { "start": "2026-07-10", "end": "2026-07-20" },
                        "status": "Approved",
                        "requestedAccounts": []
                    }
                ],
                "paging": { "offset": 0, "limit": 100, "total": 1 }
            }
            """;

        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("accounttypes"))
            {
                return JsonResponse(AccountTypesSinglePage);
            }

            if (url.Contains("absence/v1.0/accounts"))
            {
                accountsCallCount++;
                return JsonResponse(AccountsSinglePage);
            }

            return JsonResponse(requestsJson);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openapi.planday.test") };
        var sut = new PlandayAbsenceService(httpClient);

        var rangeA = new AbsenceRequestQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        await sut.GetAbsenceRequestsAsync(rangeA);
        await sut.GetAbsenceRequestsAsync(rangeA);
        Assert.Equal(1, accountsCallCount);

        var rangeB = new AbsenceRequestQuery(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        await sut.GetAbsenceRequestsAsync(rangeB);
        Assert.Equal(2, accountsCallCount);
    }

    private static string RouteResponse(HttpRequestMessage request, string requestsJson)
    {
        var url = request.RequestUri!.ToString();
        if (url.Contains("accounttypes"))
        {
            return AccountTypesSinglePage;
        }

        if (url.Contains("absence/v1.0/accounts"))
        {
            return AccountsSinglePage;
        }

        return requestsJson;
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
