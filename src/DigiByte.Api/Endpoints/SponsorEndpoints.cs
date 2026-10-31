using System.Net;
using DigiByte.Api.Services;
using DigiByte.P2P.Shared.Sponsors;
using Microsoft.AspNetCore.Http.Features;

namespace DigiByte.Api.Endpoints;

/// <summary>
/// POST /api/sponsors/submissions: the wallet's "add me to the sponsors list" form. Anonymous on purpose (a key in a static app would be
/// public), so it is limited instead: per-visitor rate limit, a cap on open pull requests, a 300 KB body limit, a 200 KB picture limit,
/// an automatic donation check, and a bot that can only open pull requests. Constitution 1.1.0 (intake relay) governs this endpoint.
/// </summary>
public static class SponsorEndpoints
{
    public const string RateLimitPolicy = "sponsor-submissions";
    public const int MaxBodyBytes = 300 * 1024;

    public static RouteGroupBuilder MapSponsorEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/submissions", Submit)
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("SubmitSponsor");
        return group;
    }

    public const string ClientKeyItem = "sponsor-client-key";

    /// <summary>
    /// The visitor key for rate limiting, read from <see cref="ClientKeyItem"/>, which <see cref="ComputeClientKey"/> fills in at the very start
    /// of the pipeline (before the forwarded-headers middleware rewrites the connection address and header).
    /// </summary>
    public static string ClientKey(HttpContext context) =>
        context.Items.TryGetValue(ClientKeyItem, out var key) && key is string s ? s : ComputeClientKey(context);

    /// <summary>
    /// The API sits behind a proxy, and the leftmost X-Forwarded-For entries are whatever the client claims, so the rightmost entry (appended by
    /// the proxy itself) is the visitor. Without the header, the socket address is used.
    /// </summary>
    public static string ComputeClientKey(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var last = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(last) && IPAddress.TryParse(last, out var ip)) return ip.ToString();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static async Task<IResult> Submit(HttpRequest request, SponsorSubmissionService service, CancellationToken ct)
    {
        if (request.ContentLength is null || request.ContentLength > MaxBodyBytes)
            return Json(new SponsorSubmissionResult { State = SponsorSubmissionState.Invalid, Fields = ["image"] }, StatusCodes.Status413PayloadTooLarge);
        if (!request.HasFormContentType)
            return Json(new SponsorSubmissionResult { State = SponsorSubmissionState.Invalid }, StatusCodes.Status400BadRequest);

        IFormCollection form;
        try
        {
            // Kestrel enforces the cap while reading; the in-process test host has no such feature, hence the checks.
            var limit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = MaxBodyBytes;
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return Json(new SponsorSubmissionResult { State = SponsorSubmissionState.Invalid, Fields = ["image"] }, StatusCodes.Status413PayloadTooLarge);
        }

        var typeText = form["type"].ToString();
        if (!Enum.TryParse<SponsorSubmissionType>(typeText, ignoreCase: true, out var type) || typeText.Length == 0)
            return Json(new SponsorSubmissionResult { State = SponsorSubmissionState.Invalid, Fields = ["type"] }, StatusCodes.Status400BadRequest);

        byte[]? image = null;
        var file = form.Files.GetFile("image");
        if (file is { Length: > 0 })
        {
            if (file.Length > SponsorRules.ImageMaxBytes)
                return Json(new SponsorSubmissionResult { State = SponsorSubmissionState.Invalid, Fields = ["image"] }, StatusCodes.Status413PayloadTooLarge);
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            image = ms.ToArray();
        }

        var result = await service.SubmitAsync(new SponsorSubmissionRequest
        {
            Type = type,
            Name = form["name"].ToString(),
            Link = form["link"].ToString(),
            TxId = form["txid"].ToString(),
            Consent = string.Equals(form["consent"].ToString(), "true", StringComparison.OrdinalIgnoreCase),
            Website = form["website"].ToString(),
            Image = image,
        }, ct);

        return Json(result, StatusFor(result.State));
    }

    public static int StatusFor(SponsorSubmissionState state) => state switch
    {
        SponsorSubmissionState.Created => StatusCodes.Status201Created,
        SponsorSubmissionState.Duplicate => StatusCodes.Status200OK,
        SponsorSubmissionState.Invalid => StatusCodes.Status400BadRequest,
        SponsorSubmissionState.AlreadyUsed => StatusCodes.Status409Conflict,
        SponsorSubmissionState.NotVerified => StatusCodes.Status422UnprocessableEntity,
        SponsorSubmissionState.RateLimited => StatusCodes.Status429TooManyRequests,
        SponsorSubmissionState.QueueFull => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status502BadGateway,
    };

    private static IResult Json(SponsorSubmissionResult result, int status) =>
        Results.Json(result, SponsorJson.Options, statusCode: status);
}
